namespace NovaFleet.Application.Setup;

using NovaFleet.Application.Settings;

public sealed record SetupStatusDto(bool IsCompleted, bool HasLocalAdmin, int UserCount, string? OrganizationName);

/// <summary>
/// بلوک‌های اختیاری همراه با تأیید نهایی ویزارد. هر بلوک یا کامل ارسال می‌شود یا null است؛
/// حضور مقدار null یعنی «همان مقادیر پیش‌فرض بماند».
/// </summary>
public sealed record SetupRequest(
    string OrganizationName,
    string AdminUsername,
    string AdminPassword,
    string AdminFirstName,
    string AdminLastName,
    string AdminMobile,
    string? LogoDataUrl = null,
    UpdateDirectorySettingsDto? Directory = null,
    UpdateSmsConnectionSettingsDto? Sms = null,
    UpdateOperationsSettingsDto? Operations = null,
    MissionRulesDto? Rules = null,
    SessionPolicyDto? Session = null,
    MissionOrderTemplateDto? Template = null);

/// <summary>
/// گام دیتابیس ویزارد. منبع و مقدار ماسک‌شده برای پیش‌پُر کردن نمایش داده می‌شود؛
/// رمز رشته اتصال هرگز به مرورگر برنمی‌گردد.
/// </summary>
public sealed record SetupDatabaseStateDto(
    bool IsConfigured,
    string Source,
    string? ServerName,
    string? DatabaseName,
    bool UseWindowsAuthentication,
    string? UserName,
    bool HasPassword);

/// <summary>ConnectionString پرشده یعنی «همین رشته اتصال را به‌کار ببر» و فیلدهای دیگر نادیده گرفته می‌شوند.</summary>
public sealed record SetupDatabaseRequest(
    string ServerName,
    string DatabaseName,
    bool UseWindowsAuthentication,
    string? UserName,
    string? Password,
    bool TrustServerCertificate,
    string? ConnectionString = null);

/// <summary>RestartRequired یعنی تا سرویس بالا نیاید، گام‌های بعدی ویزارد به همان دیتابیس قدیمی نوشته می‌شوند.</summary>
public sealed record SetupDatabaseResultDto(
    bool RestartRequired,
    bool CreatedDatabase,
    bool SameAsActive,
    string Hint);

/// <summary>
/// راه‌اندازی اولیه؛ تنها زمانی مجاز است که هیچ کاربر محلی فعالی وجود نداشته باشد.
/// هیچ رمز پیش‌فرضی در کد یا پایگاه داده قرار نمی‌گیرد.
/// </summary>
public interface ISetupService
{
    Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<SetupDatabaseStateDto> GetDatabaseStateAsync(CancellationToken cancellationToken = default);

    Task<SetupDatabaseResultDto> ConfigureDatabaseAsync(
        SetupDatabaseRequest request,
        CancellationToken cancellationToken = default);

    Task<SetupStatusDto> InitializeAsync(SetupRequest request, CancellationToken cancellationToken = default);
}
