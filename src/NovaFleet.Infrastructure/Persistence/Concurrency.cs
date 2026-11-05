using Microsoft.EntityFrameworkCore;

namespace NovaFleet.Infrastructure.Persistence;

/// <summary>ابزار مشترک کنترل هم‌زمانی؛ نسخه ردیف از کلاینت به مقدار اصلی EF منتقل می‌شود.</summary>
public static class Concurrency
{
    public static void ApplyRowVersion(this DbContext db, object entity, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return;
        }

        try
        {
            db.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            // مقدار نامعتبر نادیده گرفته می‌شود؛ EF با نسخه فعلی ادامه می‌دهد.
        }
    }

    public static string ToRowVersionString(this byte[]? rowVersion) =>
        rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : string.Empty;
}
