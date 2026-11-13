using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Setup;

/// <summary>سلامت رشته اتصالی که همین حالا برای سرویس فعال است.</summary>
public sealed record DatabaseHealthResult(bool CanConnect, bool HasPendingMigrations);

/// <summary>
/// آزمون فقط‌خواندنی اتصال پایگاه داده. چیزی نمی‌سازد و مهاجرت اجرا نمی‌کند؛
/// ساخت یا تعمیر پایگاه داده کار ویزارد راه‌اندازی است.
/// </summary>
public interface IDatabaseHealthProbe
{
    Task<DatabaseHealthResult> ProbeAsync(string connectionString, CancellationToken cancellationToken = default);
}

public sealed class DatabaseHealthProbe(ILogger<DatabaseHealthProbe> logger) : IDatabaseHealthProbe
{
    public async Task<DatabaseHealthResult> ProbeAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NovaFleetDbContext>().UseSqlServer(connectionString).Options;
            await using var db = new NovaFleetDbContext(options);
            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            return new DatabaseHealthResult(true, pending.Any());
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "آزمون اتصال پایگاه داده ناموفق بود.");
            return new DatabaseHealthResult(false, false);
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "رشته اتصال ذخیره‌شده قابل‌تحلیل نیست.");
            return new DatabaseHealthResult(false, false);
        }
    }
}
