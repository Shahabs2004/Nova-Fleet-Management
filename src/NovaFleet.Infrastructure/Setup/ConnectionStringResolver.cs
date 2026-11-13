using Microsoft.Extensions.Configuration;

namespace NovaFleet.Infrastructure.Setup;

public enum ConnectionSource
{
    None = 0,
    Environment = 1,
    BootstrapFile = 2,
    ApplicationSettings = 3,
}

/// <summary>
/// تک‌نقطه تصمیم برای رشته اتصال. متغیر محیطی بر فایل بوت‌استرپ و فایل بوت‌استرپ بر appsettings
/// اولویت دارد تا نصب پشتیبان‌گیری‌شده روی ماشین دیگر بی‌سروصدا به تنظیمات توسعه برنگردد.
/// </summary>
public static class ConnectionStringResolver
{
    public const string EnvironmentVariable = "NOVAFLEET_ConnectionStrings__Default";

    public static (string? Value, ConnectionSource Source) Resolve(
        IConfiguration configuration,
        IBootstrapConfigurationStore bootstrap)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return (fromEnvironment.Trim(), ConnectionSource.Environment);
        }

        var fromBootstrap = bootstrap.ReadConnectionString();
        if (fromBootstrap is not null)
        {
            return (fromBootstrap, ConnectionSource.BootstrapFile);
        }

        var fromSettings = configuration.GetConnectionString("Default");
        return string.IsNullOrWhiteSpace(fromSettings)
            ? (null, ConnectionSource.None)
            : (fromSettings.Trim(), ConnectionSource.ApplicationSettings);
    }
}
