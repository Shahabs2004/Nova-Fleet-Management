using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Maintenance;
using NovaFleet.Domain.Maintenance;

namespace NovaFleet.Infrastructure.Maintenance;

public sealed partial class MaintenanceService
{
    private IQueryable<MaintenanceRecord> RecordQuery => db.MaintenanceRecords.Include(r => r.Items).ThenInclude(i => i.Parts);

    public async Task<PagedResult<RecordDto>> RecordsAsync(Guid vehicleId, int page, int pageSize, bool includeVoided, CancellationToken ct)
    {
        View();
        await LoadVehicleAsync(vehicleId, ct);
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = RecordQuery.AsNoTracking().Where(r => r.VehicleId == vehicleId && (includeVoided || !r.IsVoided));
        var count = await query.CountAsync(ct);
        var records = await query.OrderByDescending(r => r.PerformedAtUtc).ThenBy(r => r.Id).Skip((p-1)*size).Take(size).ToListAsync(ct);
        return new PagedResult<RecordDto>(await ToRecordsAsync(records, ct), count, p, size);
    }

    public async Task<RecordDto> RecordAsync(Guid vehicleId, Guid id, CancellationToken ct)
    {
        View();
        var record = await RecordQuery.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.VehicleId == vehicleId, ct)
            ?? throw new NotFoundAppException("سابقهٔ سرویس پیدا نشد.");
        return (await ToRecordsAsync([record], ct))[0];
    }

    private async Task<List<RecordDto>> ToRecordsAsync(IReadOnlyList<MaintenanceRecord> records, CancellationToken ct)
    {
        var ids = records.Select(r => r.Id).ToArray();
        var readings = await db.VehicleOdometerReadings.AsNoTracking().Where(r => r.SourceKind == "maintenance" && ids.Contains(r.SourceId) && !r.IsVoided).ToDictionaryAsync(r => r.SourceId, ct);
        var attachments = await db.MaintenanceAttachments.AsNoTracking().Where(a => ids.Contains(a.RecordId))
            .Select(a => new { a.RecordId, Dto = new AttachmentDto(a.Id, a.FileName, a.ContentType, a.Content.Length, a.UploadedBy, a.CreatedAtUtc) }).ToListAsync(ct);
        return records.Select(r => new RecordDto(r.Id, r.VehicleId, r.PerformedAtUtc, readings.GetValueOrDefault(r.Id)?.Kilometers,
            r.Workshop, r.Notes, r.OtherCostToman, Total(r), r.IsVoided, r.CreatedBy, r.Version,
            r.Items.Select(i => new ItemDto(i.Id, i.TemplateId, i.ScheduleId, i.Name, i.Category, i.AssetKey, i.LaborCostToman,
                i.Parts.Select(p => new PartDto(p.Id, p.Name, p.Specification, p.Quantity, p.UnitCostToman, p.WarrantyUntil)).ToList())).ToList(),
            attachments.Where(a => a.RecordId == r.Id).Select(a => a.Dto).ToList())).ToList();
    }

    private static decimal Total(MaintenanceRecord r) => r.OtherCostToman + r.Items.Sum(i => i.LaborCostToman + i.Parts.Sum(p => p.Quantity * p.UnitCostToman));

    public async Task<RecordDto> SaveRecordAsync(Guid vehicleId, Guid? id, RecordInput input, CancellationToken ct)
    {
        View();
        user.Require(id.HasValue ? PermissionCatalog.Maintenance.Correct : PermissionCatalog.Maintenance.Create);
        await LoadVehicleAsync(vehicleId, ct, !id.HasValue);
        Past(input.PerformedAtUtc);
        MaintenanceRules.ValidateKm(input.OdometerKm);
        if (input.Items is null || input.Items.Count is < 1 or > 30 || input.Items.Any(i => i is null))
            throw new ValidationAppException("هر مراجعه باید بین ۱ تا ۳۰ عملیات داشته باشد.");
        if (input.Items.Where(i => i.ScheduleId.HasValue).Select(i => i.ScheduleId).Distinct().Count() != input.Items.Count(i => i.ScheduleId.HasValue))
            throw new ValidationAppException("یک برنامه نمی‌تواند دو بار در یک مراجعه تکمیل شود.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, Json))));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await OdometerLedger.LockVehicleAsync(db, vehicleId, ct);
        if (!id.HasValue)
        {
            if (input.RequestId == Guid.Empty) throw new ValidationAppException("شناسهٔ درخواست الزامی است.");
            var duplicate = await db.MaintenanceRecords.AsNoTracking().SingleOrDefaultAsync(r => r.VehicleId == vehicleId && r.RequestId == input.RequestId, ct);
            if (duplicate is not null)
            {
                if (duplicate.RequestHash != hash || duplicate.CreatedBy != user.Username)
                    throw new ConflictAppException("شناسهٔ درخواست قبلاً استفاده شده است.");
                return await RecordAsync(vehicleId, duplicate.Id, ct);
            }
        }
        var record = id.HasValue ? await RecordQuery.SingleOrDefaultAsync(r => r.Id == id && r.VehicleId == vehicleId, ct)
            ?? throw new NotFoundAppException("سابقهٔ سرویس پیدا نشد.")
            : new MaintenanceRecord { VehicleId = vehicleId, CreatedBy = user.Username, RequestId = input.RequestId, RequestHash = hash };
        if (record.IsVoided) throw new ValidationAppException("سابقهٔ باطل‌شده قابل ویرایش نیست.");
        if (id.HasValue)
        {
            Version(record.Version, input.Version);
            Revision("record", record.Id, vehicleId, record.Version, (await ToRecordsAsync([record], ct))[0], input.Reason);
            db.MaintenanceRecordItems.RemoveRange(record.Items);
            record.Items = [];
            record.Version++;
        }
        else db.MaintenanceRecords.Add(record);
        record.PerformedAtUtc = input.PerformedAtUtc;
        record.Workshop = MaintenanceRules.Optional(input.Workshop, 200);
        record.Notes = MaintenanceRules.Optional(input.Notes, 2000);
        record.OtherCostToman = MaintenanceRules.Money(input.OtherCostToman);
        var templateIds = input.Items.Select(i => i.TemplateId).Distinct().ToArray();
        var templates = await db.MaintenanceTemplates.Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var scheduleIds = input.Items.Where(i => i.ScheduleId.HasValue).Select(i => i.ScheduleId!.Value).ToArray();
        var schedules = await db.MaintenanceSchedules.Where(s => s.VehicleId == vehicleId && scheduleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        foreach (var item in input.Items)
        {
            if (!templates.TryGetValue(item.TemplateId, out var template) || (!id.HasValue && !template.IsActive))
                throw new ValidationAppException("نوع سرویس معتبر یا فعال نیست.");
            if (item.ScheduleId.HasValue && (!schedules.TryGetValue(item.ScheduleId.Value, out var schedule) || schedule.TemplateId != item.TemplateId))
                throw new ValidationAppException("برنامهٔ سرویس به این خودرو و عملیات تعلق ندارد.");
            if (item.Parts is null || item.Parts.Count > 30 || item.Parts.Any(p => p is null)) throw new ValidationAppException("حداکثر ۳۰ قطعه برای هر عملیات مجاز است.");
            var entity = new MaintenanceRecordItem { RecordId = record.Id, TemplateId = template.Id, ScheduleId = item.ScheduleId,
                Name = template.Name, Category = template.Category, AssetKey = template.AssetKey, LaborCostToman = MaintenanceRules.Money(item.LaborCostToman) };
            foreach (var part in item.Parts)
            {
                if (part.Quantity is < 1 or > 1000) throw new ValidationAppException("تعداد قطعه باید بین ۱ و ۱۰۰۰ باشد.");
                entity.Parts.Add(new MaintenanceRecordPart { ItemId = entity.Id,
                    Name = MaintenanceRules.Required(part.Name, 150, "نام قطعه"), Specification = MaintenanceRules.Optional(part.Specification, 500),
                    Quantity = part.Quantity, UnitCostToman = MaintenanceRules.Money(part.UnitCostToman), WarrantyUntil = part.WarrantyUntil });
            }
            record.Items.Add(entity);
            db.MaintenanceRecordItems.Add(entity);
        }
        MaintenanceRules.Money(Total(record));
        var reading = await db.VehicleOdometerReadings.SingleOrDefaultAsync(r => r.SourceKind == "maintenance" && r.SourceId == record.Id, ct);
        if (reading is not null)
        {
            Revision("odometer", reading.Id, vehicleId, reading.Version, ToOdometer(reading), input.Reason ?? "اصلاح سابقهٔ سرویس");
            reading.Version++;
            reading.IsVoided = !input.OdometerKm.HasValue;
        }
        else if (input.OdometerKm.HasValue)
        {
            reading = new VehicleOdometerReading { VehicleId = vehicleId, SourceId = record.Id, SourceKind = "maintenance", RecordedBy = user.Username };
            db.VehicleOdometerReadings.Add(reading);
        }
        if (reading is not null)
        {
            reading.Kilometers = input.OdometerKm ?? reading.Kilometers;
            reading.ObservedAtUtc = input.PerformedAtUtc;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await RecordAsync(vehicleId, record.Id, ct);
    }

    public async Task VoidRecordAsync(Guid vehicleId, Guid id, RevisionInput input, CancellationToken ct)
    {
        View();
        user.Require(PermissionCatalog.Maintenance.Correct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await OdometerLedger.LockVehicleAsync(db, vehicleId, ct);
        var record = await RecordQuery.SingleOrDefaultAsync(r => r.Id == id && r.VehicleId == vehicleId, ct)
            ?? throw new NotFoundAppException("سابقه پیدا نشد.");
        Version(record.Version, input.Version);
        if (record.IsVoided) throw new ValidationAppException("این سابقه قبلاً باطل شده است.");
        Revision("record", record.Id, vehicleId, record.Version, (await ToRecordsAsync([record], ct))[0], input.Reason);
        record.IsVoided = true;
        record.Version++;
        var reading = await db.VehicleOdometerReadings.SingleOrDefaultAsync(r => r.SourceKind == "maintenance" && r.SourceId == id, ct);
        if (reading is not null && !reading.IsVoided)
        {
            Revision("odometer", reading.Id, vehicleId, reading.Version, ToOdometer(reading), input.Reason);
            reading.IsVoided = true;
            reading.Version++;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
