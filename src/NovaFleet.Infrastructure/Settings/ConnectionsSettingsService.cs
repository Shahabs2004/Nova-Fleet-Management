using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Infrastructure.Setup;

namespace NovaFleet.Infrastructure.Settings;

/// <summary>
/// تنظیمات اتصال سرویس‌ها. رمزها فقط نوشته می‌شوند و هرگز خوانده نمی‌شوند؛
/// به‌جای آن‌ها پرچم HasPassword/HasBindPassword به UI برمی‌گردد.
/// </summary>
public sealed class ConnectionsSettingsService(
    IRuntimeSettings settings,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IBootstrapConfigurationStore bootstrap,
    IConfiguration configuration) : IConnectionsSettingsService
{
    private const string RestartHint =
        "این تغییر پس از راه‌اندازی مجدد سرویس NovaFleet اعمال می‌شود.";

    public async Task<DirectorySettingsDto> GetDirectoryAsync(CancellationToken cancellationToken = default)
    {
        RequireConnections();
        var values = await settings.ReadAllAsync(cancellationToken);
        return new DirectorySettingsDto(
            ReadBool(values, SettingKeys.DirectoryEnabled),
            ReadText(values, SettingKeys.DirectoryHost),
            ReadInt(values, SettingKeys.DirectoryPort, SettingKeys.DefaultDirectoryPort),
            ReadBool(values, SettingKeys.DirectoryUseSsl),
            ReadText(values, SettingKeys.DirectoryDomain),
            ReadText(values, SettingKeys.DirectoryBaseDn),
            ReadText(values, SettingKeys.DirectoryBindDn),
            ReadInt(values, SettingKeys.DirectoryTimeoutSeconds, SettingKeys.DefaultDirectoryTimeoutSeconds),
            ReadText(values, SettingKeys.DirectorySearchFilter, SettingKeys.DefaultDirectorySearchFilter),
            await settings.HasSecretAsync(SettingKeys.DirectoryBindPassword, cancellationToken));
    }

    public async Task<SettingsSaveResultDto> UpdateDirectoryAsync(
        UpdateDirectorySettingsDto request,
        CancellationToken cancellationToken = default)
    {
        RequireConnections();
        var filter = await SettingsWriters.WriteDirectoryAsync(settings, request, cancellationToken);

        await RecordAsync("UpdateDirectorySettings", new
        {
            request.Enabled,
            request.Host,
            request.Port,
            request.UseSsl,
            request.Domain,
            request.BaseDn,
            request.BindDn,
            request.TimeoutSeconds,
            SearchFilter = filter,
            PasswordChanged = !string.IsNullOrWhiteSpace(request.BindPassword)
        }, cancellationToken);

        return new SettingsSaveResultDto(false, null);
    }

    public async Task<SmsConnectionSettingsDto> GetSmsAsync(CancellationToken cancellationToken = default)
    {
        RequireConnections();
        var values = await settings.ReadAllAsync(cancellationToken);
        return new SmsConnectionSettingsDto(
            ReadBool(values, SettingKeys.SmsEnabled),
            ReadText(values, SettingKeys.SmsMode, SettingKeys.DefaultSmsMode),
            ReadText(values, SettingKeys.SmsEndpoint),
            ReadText(values, SettingKeys.SmsOrganization),
            ReadText(values, SettingKeys.SmsUsername),
            ReadText(values, SettingKeys.SmsSenderNumber),
            ReadInt(values, SettingKeys.SmsTimeoutSeconds, SettingKeys.DefaultSmsTimeoutSeconds),
            ReadInt(values, SettingKeys.SmsRetryMinutes, SettingKeys.DefaultSmsRetryMinutes),
            ReadInt(values, SettingKeys.SmsMaxAttempts, SettingKeys.DefaultSmsMaxAttempts),
            await settings.HasSecretAsync(SettingKeys.SmsPassword, cancellationToken));
    }

    public async Task<SettingsSaveResultDto> UpdateSmsAsync(
        UpdateSmsConnectionSettingsDto request,
        CancellationToken cancellationToken = default)
    {
        RequireConnections();
        var mode = await SettingsWriters.WriteSmsAsync(settings, request, cancellationToken);

        await RecordAsync("UpdateSmsSettings", new
        {
            request.Enabled,
            Mode = mode,
            request.Endpoint,
            request.Organization,
            request.Username,
            request.SenderNumber,
            request.TimeoutSeconds,
            request.RetryMinutes,
            request.MaxAttempts,
            PasswordChanged = !string.IsNullOrWhiteSpace(request.Password)
        }, cancellationToken);

        return new SettingsSaveResultDto(false, null);
    }

    public async Task<OperationsSettingsDto> GetOperationsAsync(CancellationToken cancellationToken = default)
    {
        RequireConnections();
        var values = await settings.ReadAllAsync(cancellationToken);
        return new OperationsSettingsDto(
            ReadText(values, SettingKeys.OperationsLogLevel, SettingKeys.DefaultLogLevel),
            ReadText(values, SettingKeys.BrandingPdfFontPath),
            ReadText(values, SettingKeys.BrandingReportPaperSize, SettingKeys.DefaultReportPaperSize));
    }

    public async Task<SettingsSaveResultDto> UpdateOperationsAsync(
        UpdateOperationsSettingsDto request,
        CancellationToken cancellationToken = default)
    {
        RequireConnections();
        var (level, paper, fontPath) = await SettingsWriters.WriteOperationsAsync(settings, request, cancellationToken);

        await RecordAsync("UpdateOperationsSettings", new { LogLevel = level, PdfFontPath = fontPath, PaperSize = paper },
            cancellationToken);

        return new SettingsSaveResultDto(true,
            $"سطح لاگ و مسیر فونت پس از راه‌اندازی مجدد اعمال می‌شوند؛ اندازه کاغذ از {paper} از همین حالا معتبر است.");
    }

    public Task<DatabaseSettingsDto> GetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        RequireConnections();
        var (value, source) = ConnectionStringResolver.Resolve(configuration, bootstrap);
        return Task.FromResult(new DatabaseSettingsDto(Mask(value), value is not null, source.ToString()));
    }

    public async Task<SettingsSaveResultDto> UpdateDatabaseAsync(
        DatabaseSettingsDto request,
        CancellationToken cancellationToken = default)
    {
        RequireConnections();
        var candidate = request.ConnectionString?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 2000)
        {
            throw new ValidationAppException("رشته اتصال معتبر نیست.");
        }

        try
        {
            _ = new SqlConnectionStringBuilder(candidate);
        }
        catch (ArgumentException)
        {
            throw new ValidationAppException("رشته اتصال قابل‌تحلیل نیست؛ نحو آن را بررسی کنید.");
        }

        bootstrap.WriteConnectionString(candidate);
        await RecordAsync("UpdateDatabaseConnection", new { Source = ConnectionSource.BootstrapFile.ToString(), File = bootstrap.FilePath },
            cancellationToken);

        var (_, source) = ConnectionStringResolver.Resolve(configuration, bootstrap);
        var shadowed = source != ConnectionSource.BootstrapFile
            ? $" توجه: در حال حاضر منبع فعال {source} است و تا زمانی که تنظیمش را برندارید این ذخیره اعمال نمی‌شود."
            : string.Empty;

        return new SettingsSaveResultDto(true,
            $"رشته اتصال در {bootstrap.FilePath} ذخیره شد (منبع فعال: {(source is ConnectionSource.None ? "هیچ" : source.ToString())}).{shadowed} "
            + RestartHint);
    }

    private void RequireConnections() => currentUser.Require(PermissionCatalog.Administration.ConnectionsManage);

    /// <summary>رمز و هر کلید حساس دیگر رشته اتصال به UI نشان داده نمی‌شود.</summary>
    private static string? Mask(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString) { Password = string.Empty };
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return "«رشته اتصال ذخیره‌شده قابل‌تحلیل نیست.»";
        }
    }

    private Task RecordAsync(string action, object after, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditRecord(action, "SystemSetting", null, "Success", After: after), cancellationToken);

    private static string ReadText(IReadOnlyDictionary<string, string> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        int.TryParse(ReadText(values, key), out var value) ? value : fallback;

    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key) =>
        bool.TryParse(ReadText(values, key), out var value) && value;
}
