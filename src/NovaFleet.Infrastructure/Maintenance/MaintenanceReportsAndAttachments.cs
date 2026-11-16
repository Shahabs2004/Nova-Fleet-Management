using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Maintenance;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Maintenance;

namespace NovaFleet.Infrastructure.Maintenance;

public sealed partial class MaintenanceService
{
    public async Task<MaintenanceReport> ReportAsync(Guid vehicleId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        View();
        await LoadVehicleAsync(vehicleId, ct);
        if (from.Year < 1900 || to < from || to-from > TimeSpan.FromDays(3660))
            throw new ValidationAppException("بازهٔ گزارش باید معتبر و حداکثر ده سال باشد.");
        var records = await RecordQuery.AsNoTracking().Where(r => r.VehicleId == vehicleId && !r.IsVoided && r.PerformedAtUtc >= from && r.PerformedAtUtc <= to).ToListAsync(ct);
        var allReadings = await db.VehicleOdometerReadings.AsNoTracking().Where(r => r.VehicleId == vehicleId && !r.IsVoided).ToListAsync(ct);
        var readings = allReadings.Where(r => !r.IsTimeEstimated && r.ObservedAtUtc >= from && r.ObservedAtUtc <= to && r.ObservedAtUtc <= clock.UtcNow).OrderBy(r => r.ObservedAtUtc).ToList();
        var first = readings.FirstOrDefault();
        var last = readings.LastOrDefault();
        var validDistance = first is not null && last is not null && last.ObservedAtUtc > first.ObservedAtUtc
            && last.Kilometers > first.Kilometers && OdometerLedger.ConflictIds(allReadings).Count == 0;
        long? distance = validDistance ? last!.Kilometers-first!.Kilometers : null;
        decimal? matchingCost = validDistance ? records.Where(r => r.PerformedAtUtc >= first!.ObservedAtUtc && r.PerformedAtUtc <= last!.ObservedAtUtc).Sum(Total) : null;
        var months = records.GroupBy(r => JalaliDate.FormatDate(r.PerformedAtUtc)[..7]).OrderBy(g => g.Key)
            .Select(g => new ExpensePoint(g.Key, g.Sum(Total))).ToList();
        var categories = records.SelectMany(r => r.Items).GroupBy(i => i.Category)
            .Select(g => new CategoryExpense(g.Key, g.Sum(i => i.LaborCostToman+i.Parts.Sum(p => p.Quantity*p.UnitCostToman)))).ToList();
        var other = records.Sum(r => r.OtherCostToman);
        if (other > 0) categories.Add(new CategoryExpense("هزینهٔ جانبی", other));
        return new MaintenanceReport(from, to, records.Sum(Total), distance,
            distance.HasValue ? decimal.Round(matchingCost!.Value/distance.Value, 2) : null,
            validDistance ? first!.ObservedAtUtc : null, validDistance ? last!.ObservedAtUtc : null,
            matchingCost, months, categories);
    }

    public async Task<AttachmentDto> AddAttachmentAsync(Guid vehicleId, Guid recordId, string name, byte[] content, CancellationToken ct)
    {
        View();
        user.Require(PermissionCatalog.Maintenance.Attach);
        if (content.Length is < 8 or > 5242880) throw new ValidationAppException("حجم فایل باید حداکثر ۵ مگابایت باشد.");
        var filename = MaintenanceRules.Required(Path.GetFileName(name.Replace('\\', '/')), 200, "نام فایل");
        if (filename.Any(char.IsControl)) throw new ValidationAppException("نام فایل معتبر نیست.");
        string mime;
        if (content.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) mime = "image/png";
        else if (content[0] == 255 && content[1] == 216 && content[^2] == 255 && content[^1] == 217) mime = "image/jpeg";
        else if (content.AsSpan().StartsWith("%PDF-"u8) && Encoding.ASCII.GetString(content.AsSpan(Math.Max(0, content.Length-1024))).Contains("%%EOF", StringComparison.Ordinal)) mime = "application/pdf";
        else throw new ValidationAppException("فقط فایل PDF، PNG یا JPEG مجاز است.");
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (!(mime == "image/png" && extension == ".png" || mime == "image/jpeg" && extension is ".jpg" or ".jpeg" || mime == "application/pdf" && extension == ".pdf"))
            throw new ValidationAppException("پسوند فایل با محتوای آن مطابقت ندارد.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await OdometerLedger.LockVehicleAsync(db, vehicleId, ct);
        var record = await db.MaintenanceRecords.SingleOrDefaultAsync(r => r.Id == recordId && r.VehicleId == vehicleId, ct)
            ?? throw new NotFoundAppException("سابقه پیدا نشد.");
        if (record.IsVoided) throw new ValidationAppException("افزودن پیوست به سابقهٔ باطل‌شده ممکن نیست.");
        if (await db.MaintenanceAttachments.CountAsync(a => a.RecordId == recordId, ct) >= 20)
            throw new ValidationAppException("حداکثر ۲۰ پیوست برای هر مراجعه مجاز است.");
        var attachment = new MaintenanceAttachment { RecordId = recordId, FileName = filename, ContentType = mime, Content = content, UploadedBy = user.Username };
        db.MaintenanceAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new AttachmentDto(attachment.Id, attachment.FileName, mime, content.Length, user.Username, attachment.CreatedAtUtc);
    }

    public async Task<AttachmentDownload> DownloadAsync(Guid vehicleId, Guid recordId, Guid attachmentId, CancellationToken ct)
    {
        View();
        if (!await db.MaintenanceRecords.AnyAsync(r => r.Id == recordId && r.VehicleId == vehicleId, ct))
            throw new NotFoundAppException("سابقه پیدا نشد.");
        var attachment = await db.MaintenanceAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == attachmentId && a.RecordId == recordId, ct)
            ?? throw new NotFoundAppException("پیوست پیدا نشد.");
        return new AttachmentDownload(attachment.FileName, attachment.ContentType, attachment.Content);
    }
}
