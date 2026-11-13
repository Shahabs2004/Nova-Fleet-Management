using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Common;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Setup;

/// <summary>نتیجه آماده‌سازی هدف: آیا پایگاه داده تازه ساخته شد یا از قبل وجود داشت.</summary>
public sealed record DatabaseProbeResult(bool CreatedDatabase);

/// <summary>
/// ساخت و مهاجرت پایگاه داده هدف پیش از آنکه رشته اتصال ذخیره شود؛ اگر اتصال یا مهاجرت
/// شکست بخورد هیچ چیزی در فایل بوت‌استرپ نوشته نمی‌شود.
/// </summary>
public interface IDatabaseSetupProbe
{
    Task<DatabaseProbeResult> PrepareAsync(string connectionString, CancellationToken cancellationToken = default);
}

public sealed class DatabaseSetupProbe : IDatabaseSetupProbe
{
    public async Task<DatabaseProbeResult> PrepareAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new ValidationAppException("نام پایگاه داده در رشته اتصال تنظیم نشده است.");
        }

        var created = false;
        builder.InitialCatalog = "master";
        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync(cancellationToken);
            if (!await DatabaseExistsAsync(master, database, cancellationToken))
            {
                await using var create = master.CreateCommand();
                create.CommandText = $"CREATE DATABASE {Quote(database)};";
                await create.ExecuteNonQueryAsync(cancellationToken);
                created = true;
            }
        }

        // مهاجرت و seed روی همان رشته اتصالی که به سرویس تحویل داده می‌شود انجام می‌شود تا
        // نتیجه آزمون اتصال با چیزی که پس از راه‌اندازی مجدد دیدنی است یکی باشد.
        var options = new DbContextOptionsBuilder<NovaFleetDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new NovaFleetDbContext(options);
        await DatabaseInitializer.InitializeAsync(db, cancellationToken);

        return new DatabaseProbeResult(created);
    }

    private static async Task<bool> DatabaseExistsAsync(
        SqlConnection master,
        string database,
        CancellationToken cancellationToken)
    {
        await using var check = master.CreateCommand();
        check.CommandText = "SELECT CASE WHEN DB_ID(@database) IS NULL THEN 0 ELSE 1 END;";
        check.Parameters.Add("@database", SqlDbType.NVarChar, 128).Value = database;
        var found = await check.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(found, CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>نام پایگاه داده از ورودی مدیر می‌آید، پس با کروشه دابل‌شده ایمن‌سازی می‌شود.</summary>
    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
