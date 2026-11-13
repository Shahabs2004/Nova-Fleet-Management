namespace NovaFleet.Application.Settings;

/// <summary>
/// خروجی ذخیره تنظیمات. تنظیمات runtime بی‌درنگ اعمال می‌شوند؛ مواردی که در زمان
/// راه‌اندازی خوانده می‌شوند RestartRequired دارند و پیام راهنمایشان را در Hint می‌آورند.
/// </summary>
public sealed record SettingsSaveResultDto(bool RestartRequired, string? Hint);

public sealed record DirectorySettingsDto(
    bool Enabled,
    string Host,
    int Port,
    bool UseSsl,
    string Domain,
    string BaseDn,
    string BindDn,
    int TimeoutSeconds,
    string SearchFilter,
    bool HasBindPassword);

/// <summary>BindPassword null یا خالی یعنی رمز ذخیره‌شده دست‌نخورده بماند.</summary>
public sealed record UpdateDirectorySettingsDto(
    bool Enabled,
    string? Host,
    int Port,
    bool UseSsl,
    string? Domain,
    string? BaseDn,
    string? BindDn,
    int TimeoutSeconds,
    string? SearchFilter,
    string? BindPassword);

/// <summary>Enabled کلید سراسری ارسال پیامک است؛ با خاموشی، پیامک‌ها در صف می‌مانند و هیچ درخواستی به سرویس‌دهنده نمی‌رود.</summary>
public sealed record SmsConnectionSettingsDto(
    bool Enabled,
    string Mode,
    string? Endpoint,
    string? Organization,
    string? Username,
    string? SenderNumber,
    int TimeoutSeconds,
    int RetryMinutes,
    int MaxAttempts,
    bool HasPassword);

/// <summary>Password null یا خالی یعنی رمز ذخیره‌شده دست‌نخورده بماند.</summary>
public sealed record UpdateSmsConnectionSettingsDto(
    bool Enabled,
    string Mode,
    string? Endpoint,
    string? Organization,
    string? Username,
    string? SenderNumber,
    int TimeoutSeconds,
    int RetryMinutes,
    int MaxAttempts,
    string? Password);

public sealed record OperationsSettingsDto(string LogLevel, string? PdfFontPath, string ReportPaperSize);

public sealed record UpdateOperationsSettingsDto(string LogLevel, string? PdfFontPath, string ReportPaperSize);

/// <summary>رشته اتصال از فایل بوت‌استرپ خوانده و نوشته می‌شود؛ پیش از اتصال به دیتابیس نمی‌توان آن را خواند.</summary>
public sealed record DatabaseSettingsDto(string? ConnectionString, bool IsConfigured, string Source);

public interface IConnectionsSettingsService
{
    Task<DirectorySettingsDto> GetDirectoryAsync(CancellationToken cancellationToken = default);

    Task<SettingsSaveResultDto> UpdateDirectoryAsync(
        UpdateDirectorySettingsDto request,
        CancellationToken cancellationToken = default);

    Task<SmsConnectionSettingsDto> GetSmsAsync(CancellationToken cancellationToken = default);

    Task<SettingsSaveResultDto> UpdateSmsAsync(
        UpdateSmsConnectionSettingsDto request,
        CancellationToken cancellationToken = default);

    Task<OperationsSettingsDto> GetOperationsAsync(CancellationToken cancellationToken = default);

    Task<SettingsSaveResultDto> UpdateOperationsAsync(
        UpdateOperationsSettingsDto request,
        CancellationToken cancellationToken = default);

    Task<DatabaseSettingsDto> GetDatabaseAsync(CancellationToken cancellationToken = default);

    Task<SettingsSaveResultDto> UpdateDatabaseAsync(
        DatabaseSettingsDto request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// نتیجه آزمون زنده یک اتصال. پیام‌ها عمومی و فارسی‌اند و هرگز راز یا متن خام خطای
/// سرویس‌دهنده را برنمی‌گردانند؛ آن جزئیات فقط در لاگ سرویس نوشته می‌شود.
/// </summary>
public sealed record ConnectionTestResultDto(bool Success, string Message, string? Detail = null)
{
    public static ConnectionTestResultDto Passed(string message, string? detail = null) => new(true, message, detail);

    public static ConnectionTestResultDto Failed(string message, string? detail = null) => new(false, message, detail);
}

/// <summary>
/// آزمون‌های اتصال تب «اتصال سرویس‌ها». فقط‌خواندنی‌اند و هیچ تنظیمی را نمی‌سازند یا مهاجرت نمی‌کنند؛
/// برای ساخت یا تعمیر پایگاه داده از ویزارد راه‌اندازی استفاده می‌شود.
/// </summary>
public interface IConnectionTestsService
{
    Task<ConnectionTestResultDto> TestDatabaseAsync(CancellationToken cancellationToken = default);

    Task<ConnectionTestResultDto> TestDirectoryAsync(
        string? sampleUsername,
        CancellationToken cancellationToken = default);

    Task<ConnectionTestResultDto> TestSmsBalanceAsync(CancellationToken cancellationToken = default);
}
