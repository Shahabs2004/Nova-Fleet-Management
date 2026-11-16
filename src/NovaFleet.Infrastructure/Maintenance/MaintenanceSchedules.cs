using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Maintenance;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Maintenance;
using NovaFleet.Domain.Vehicles;

namespace NovaFleet.Infrastructure.Maintenance;

public sealed partial class MaintenanceService
{
    private static TemplateDto ToTemplate(MaintenanceTemplate t) => new(t.Id, t.Name, t.Category, t.AssetKey,
        t.IntervalKm, t.IntervalMonths, t.WarningKm, t.WarningDays, t.IsActive, t.Version);

    public async Task<IReadOnlyList<TemplateDto>> TemplatesAsync(CancellationToken ct)
    {
        View();
        return (await db.MaintenanceTemplates.AsNoTracking().OrderBy(t => t.Category).ThenBy(t => t.Name).ToListAsync(ct)).Select(ToTemplate).ToList();
    }

    public async Task<TemplateDto> SaveTemplateAsync(Guid? id, TemplateInput input, CancellationToken ct)
    {
        View();
        user.Require(PermissionCatalog.Maintenance.Manage);
        MaintenanceRules.ValidateIntervals(input.IntervalKm, input.IntervalMonths, input.WarningKm, input.WarningDays);
        var name = MaintenanceRules.Required(input.Name, 100, "نام الگو");
        if (!MaintenanceRules.AssetKeys.Contains(input.AssetKey)) throw new ValidationAppException("تصویر انتخاب‌شده معتبر نیست.");
        if (await db.MaintenanceTemplates.AnyAsync(t => t.Name == name && t.Id != id, ct))
            throw new ValidationAppException("الگویی با این نام وجود دارد.");
        var template = id.HasValue ? await db.MaintenanceTemplates.SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundAppException("الگو پیدا نشد.") : new MaintenanceTemplate();
        if (id.HasValue) { Version(template.Version, input.Version); template.Version++; }
        else db.MaintenanceTemplates.Add(template);
        template.Name = name;
        template.Category = MaintenanceRules.Required(input.Category, 100, "دسته");
        template.AssetKey = input.AssetKey;
        template.IntervalKm = input.IntervalKm;
        template.IntervalMonths = input.IntervalMonths;
        template.WarningKm = input.WarningKm;
        template.WarningDays = input.WarningDays;
        template.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return ToTemplate(template);
    }

    public async Task<ScheduleDto> SaveScheduleAsync(Guid vehicleId, Guid? id, ScheduleInput input, CancellationToken ct)
    {
        View();
        user.Require(PermissionCatalog.Maintenance.Manage);
        await LoadVehicleAsync(vehicleId, ct, !id.HasValue);
        MaintenanceRules.ValidateIntervals(input.IntervalKm, input.IntervalMonths, input.WarningKm, input.WarningDays);
        MaintenanceRules.ValidateKm(input.BaselineKm);
        if (input.BaselineAtUtc.HasValue) Past(input.BaselineAtUtc.Value);
        if (input.IntervalKm.HasValue && !input.BaselineKm.HasValue || input.IntervalMonths.HasValue && !input.BaselineAtUtc.HasValue)
            throw new ValidationAppException("مبنای کیلومتر یا تاریخ متناسب با نوع دوره الزامی است.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await OdometerLedger.LockVehicleAsync(db, vehicleId, ct);
        var template = await db.MaintenanceTemplates.SingleOrDefaultAsync(t => t.Id == input.TemplateId, ct)
            ?? throw new ValidationAppException("نوع سرویس پیدا نشد.");
        if (!id.HasValue && !template.IsActive) throw new ValidationAppException("این الگو غیرفعال است.");
        if (await db.MaintenanceSchedules.AnyAsync(s => s.VehicleId == vehicleId && s.TemplateId == input.TemplateId && s.Id != id, ct))
            throw new ValidationAppException("برای این نوع سرویس برنامه وجود دارد؛ همان برنامه را ویرایش کنید.");
        var schedule = id.HasValue ? await db.MaintenanceSchedules.SingleOrDefaultAsync(s => s.Id == id && s.VehicleId == vehicleId, ct)
            ?? throw new NotFoundAppException("برنامه پیدا نشد.") : new MaintenanceSchedule { VehicleId = vehicleId, TemplateId = input.TemplateId };
        if (id.HasValue)
        {
            Version(schedule.Version, input.Version);
            if (schedule.TemplateId != input.TemplateId) throw new ValidationAppException("نوع برنامهٔ موجود قابل تغییر نیست.");
            Revision("schedule", schedule.Id, vehicleId, schedule.Version, schedule, "ویرایش برنامهٔ سرویس");
            schedule.Version++;
        }
        else db.MaintenanceSchedules.Add(schedule);
        schedule.Name = MaintenanceRules.Required(input.Name, 100, "نام برنامه");
        schedule.IntervalKm = input.IntervalKm;
        schedule.IntervalMonths = input.IntervalMonths;
        schedule.WarningKm = input.WarningKm;
        schedule.WarningDays = input.WarningDays;
        schedule.BaselineKm = input.BaselineKm;
        schedule.BaselineAtUtc = input.BaselineAtUtc;
        schedule.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await VehicleAsync(vehicleId, ct)).Schedules.Single(s => s.Id == schedule.Id);
    }

    public async Task<PagedResult<MaintenanceVehicleDto>> FleetAsync(string? search, string? state, int page, int pageSize, CancellationToken ct)
    {
        View();
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.Vehicles.AsNoTracking().Where(v => !v.IsArchived);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = MaintenanceRules.Required(search, 100, "جست‌وجو");
            query = query.Where(v => v.Brand.Contains(term) || v.Model.Contains(term) || v.PlateNextThree.Contains(term));
        }
        if (state is not (null or "" or "due" or "approaching" or "unknown" or "healthy"))
            throw new ValidationAppException("فیلتر وضعیت معتبر نیست.");
        if (string.IsNullOrEmpty(state))
        {
            var count = await query.CountAsync(ct);
            var vehicles = await query.OrderBy(v => v.Brand).ThenBy(v => v.Id).Skip((p-1)*size).Take(size).ToListAsync(ct);
            return new PagedResult<MaintenanceVehicleDto>(await BuildVehiclesAsync(vehicles, ct), count, p, size);
        }
        var filtered = (await BuildVehiclesAsync(await query.OrderBy(v => v.Brand).ThenBy(v => v.Id).ToListAsync(ct), ct))
            .Where(v => state == "unknown" ? v.HasOdometerConflicts || v.CurrentKm is null || v.Schedules.Any(s => s.IsActive && s.State == "unknown")
                : state == "healthy" ? !v.HasOdometerConflicts && v.CurrentKm.HasValue && v.DueCount == 0 && v.ApproachingCount == 0
                : v.Schedules.Any(s => s.IsActive && s.State == state)).ToList();
        return new PagedResult<MaintenanceVehicleDto>(filtered.Skip((p-1)*size).Take(size).ToList(), filtered.Count, p, size);
    }

    public async Task<MaintenanceVehicleDto> VehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        View();
        return (await BuildVehiclesAsync([await LoadVehicleAsync(vehicleId, ct)], ct))[0];
    }

    internal async Task<List<MaintenanceVehicleDto>> BuildVehiclesAsync(IReadOnlyList<Vehicle> vehicles, CancellationToken ct)
    {
        var ids = vehicles.Select(v => v.Id).ToArray();
        var readings = await db.VehicleOdometerReadings.AsNoTracking().Where(r => ids.Contains(r.VehicleId)).ToListAsync(ct);
        var schedules = await db.MaintenanceSchedules.AsNoTracking().Where(s => ids.Contains(s.VehicleId)).ToListAsync(ct);
        var templateIds = schedules.Select(s => s.TemplateId).Distinct().ToArray();
        var assets = await db.MaintenanceTemplates.AsNoTracking().Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.AssetKey, ct);
        var records = await db.MaintenanceRecords.AsNoTracking().Where(r => ids.Contains(r.VehicleId) && !r.IsVoided)
            .Select(r => new { r.Id, r.VehicleId, r.PerformedAtUtc, Cost = r.OtherCostToman + r.Items.Sum(i => i.LaborCostToman + i.Parts.Sum(p => p.Quantity*p.UnitCostToman)) }).ToListAsync(ct);
        var completions = await (from i in db.MaintenanceRecordItems.AsNoTracking()
            join r in db.MaintenanceRecords.AsNoTracking() on i.RecordId equals r.Id
            where ids.Contains(r.VehicleId) && !r.IsVoided && i.ScheduleId != null
            select new { ScheduleId = i.ScheduleId!.Value, RecordId = r.Id, r.PerformedAtUtc }).ToListAsync(ct);
        var result = new List<MaintenanceVehicleDto>();
        foreach (var v in vehicles)
        {
            var odo = SummarizeOdometer(readings.Where(r => r.VehicleId == v.Id).ToList());
            var scheduleDtos = new List<ScheduleDto>();
            foreach (var s in schedules.Where(s => s.VehicleId == v.Id))
            {
                var last = completions.Where(c => c.ScheduleId == s.Id && (!s.BaselineAtUtc.HasValue || c.PerformedAtUtc >= s.BaselineAtUtc))
                    .OrderByDescending(c => c.PerformedAtUtc).ThenBy(c => c.RecordId).FirstOrDefault();
                var lastKm = last is null ? s.BaselineKm : readings.SingleOrDefault(r => r.SourceKind == "maintenance" && r.SourceId == last.RecordId && !r.IsVoided)?.Kilometers;
                var lastAt = last?.PerformedAtUtc ?? s.BaselineAtUtc;
                long? dueKm = s.IntervalKm.HasValue && lastKm.HasValue ? checked(lastKm.Value+s.IntervalKm.Value) : null;
                DateTimeOffset? dueAt = s.IntervalMonths.HasValue && lastAt.HasValue ? JalaliDate.AddMonths(lastAt.Value, s.IntervalMonths.Value) : null;
                var state = s.IsActive ? MaintenanceRules.DueState(dueKm, dueAt, odo.CurrentKm, clock.UtcNow, s.WarningKm, s.WarningDays, odo.HasConflicts) : "inactive";
                if (state == "healthy" && s.IntervalKm.HasValue && dueKm is null) state = "unknown";
                var cycle = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{last?.RecordId}|{dueKm}|{dueAt?.ToUnixTimeSeconds()}")));
                scheduleDtos.Add(new ScheduleDto(s.Id, s.TemplateId, s.Name, assets[s.TemplateId], s.IntervalKm, s.IntervalMonths,
                    s.WarningKm, s.WarningDays, s.BaselineKm, s.BaselineAtUtc, s.IsActive, s.Version,
                    lastKm, lastAt, dueKm, dueAt, state, cycle));
            }
            var history = records.Where(r => r.VehicleId == v.Id).ToList();
            result.Add(new MaintenanceVehicleDto(v.Id, v.DisplayName, v.PlateDisplay, v.IsArchived, odo.CurrentKm,
                odo.ObservedAtUtc, odo.IsTimeEstimated, odo.HasConflicts, history.Sum(r => r.Cost),
                history.Count > 0 ? history.Max(r => r.PerformedAtUtc) : null,
                scheduleDtos.Count(s => s.State == "due"), scheduleDtos.Count(s => s.State == "approaching"), scheduleDtos));
        }
        return result;
    }
}
