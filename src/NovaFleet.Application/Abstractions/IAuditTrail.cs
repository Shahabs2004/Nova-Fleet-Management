namespace NovaFleet.Application.Abstractions;

/// <summary>یک رویداد ممیزی؛ قبل/بعد فقط شامل فیلدهای غیرحساس است.</summary>
public sealed record AuditRecord(
    string Action,
    string EntityType,
    string? EntityId,
    string Outcome,
    string? Reason = null,
    object? Before = null,
    object? After = null);

/// <summary>ثبت ممیزی؛ پیاده‌سازی باید خطای ثبت را به گردش‌کار اصلی تحمیل نکند.</summary>
public interface IAuditTrail
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
