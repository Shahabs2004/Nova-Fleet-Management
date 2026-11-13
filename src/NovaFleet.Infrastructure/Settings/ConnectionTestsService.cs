using Microsoft.Extensions.Configuration;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Settings;
using NovaFleet.Infrastructure.Setup;

namespace NovaFleet.Infrastructure.Settings;

/// <summary>
/// آزمون‌های زنده تب «اتصال سرویس‌ها». هیچ‌کدام چیزی نمی‌سازد یا تنظیمی را عوض نمی‌کند و
/// هیچ‌کدام راز یا متن خام خطای سرویس‌دهنده را برنمی‌گرداند؛ آن جزئیات فقط در لاگ می‌نشیند.
/// </summary>
public sealed class ConnectionTestsService(
    IRuntimeSettings settings,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IBootstrapConfigurationStore bootstrap,
    IConfiguration configuration,
    IDatabaseHealthProbe database,
    IDirectoryAuthenticator directory,
    ISmsGateway sms) : IConnectionTestsService
{
    public async Task<ConnectionTestResultDto> TestDatabaseAsync(CancellationToken cancellationToken = default)
    {
        Require();
        var (value, source) = ConnectionStringResolver.Resolve(configuration, bootstrap);
        var detail = $"منبع فعال: {source}";
        var result = string.IsNullOrWhiteSpace(value)
            ? ConnectionTestResultDto.Failed(
                "رشته اتصال تنظیم نشده است؛ آن را در همین تب ذخیره کنید.",
                detail)
            : MapDatabase(await database.ProbeAsync(value, cancellationToken), detail);

        await RecordAsync("Database", result, cancellationToken);
        return result;
    }

    public async Task<ConnectionTestResultDto> TestDirectoryAsync(
        string? sampleUsername,
        CancellationToken cancellationToken = default)
    {
        Require();
        var probe = await directory.ProbeAsync(sampleUsername, cancellationToken);
        var result = probe.Success
            ? ConnectionTestResultDto.Passed("حساب Bind پذیرفته شد و جست‌وجوی دامنه پاسخ داد.", DescribeFound(probe))
            : ConnectionTestResultDto.Failed(probe.Error ?? "ارتباط با سرویس دایرکتوری برقرار نشد.");

        await RecordAsync("Directory", result, cancellationToken);
        return result;
    }

    public async Task<ConnectionTestResultDto> TestSmsBalanceAsync(CancellationToken cancellationToken = default)
    {
        Require();
        var balance = await sms.GetBalanceAsync(cancellationToken);
        var result = balance.Success
            ? ConnectionTestResultDto.Passed("سرویس پیامک اعتبار حساب را برگرداند.", await CreditAsync(balance.RawCredit, cancellationToken))
            : ConnectionTestResultDto.Failed("بررسی اعتبار از سرویس پیامک ناموفق بود.", await NoteAsync(balance.Error, cancellationToken));

        await RecordAsync("Sms", result, cancellationToken);
        return result;
    }

    private static ConnectionTestResultDto MapDatabase(DatabaseHealthResult health, string detail) => health.CanConnect
        ? ConnectionTestResultDto.Passed("اتصال پایگاه داده برقرار است.", health.HasPendingMigrations
            ? $"{detail} — مهاجرت در انتظار است و با راه‌اندازی مجدد اعمال می‌شود."
            : $"{detail} — همه مهاجرت‌ها اعمال شده‌اند.")
        : ConnectionTestResultDto.Failed(
            "اتصال پایگاه داده برقرار نشد؛ سرور، نام پایگاه داده و دسترسی را بررسی کنید.",
            detail);

    private static string DescribeFound(DirectoryProbeResult probe) => probe.FoundUsers switch
    {
        0 => "در محدوده BaseDn کاربری یافت نشد؛ BaseDn یا فیلتر جست‌وجو را بررسی کنید.",
        var found => probe.HitSearchLimit
            ? $"حداقل {found} کاربر یافت شد (سقف آزمون)."
            : $"{found} کاربر در محدوده جست‌وجو یافت شد."
    };

    /// <summary>حالت درگاه و خاموشی کل سوییچ پیامک در توضیح می‌آید تا اعتبار شبیه‌سازی با اعتبار واقعی اشتباه نشود.</summary>
    private async Task<string> CreditAsync(string? rawCredit, CancellationToken cancellationToken)
    {
        var note = await ModeNoteAsync(cancellationToken);
        return $"اعتبار گزارش‌شده: {rawCredit}" + (note is null ? "." : $" — {note}");
    }

    private async Task<string?> NoteAsync(string? error, CancellationToken cancellationToken)
    {
        var note = await ModeNoteAsync(cancellationToken);
        return error is null && note is null
            ? null
            : string.Join(" — ", new[] { error, note }.Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    private async Task<string?> ModeNoteAsync(CancellationToken cancellationToken)
    {
        var mode = await settings.GetStringAsync(SettingKeys.SmsMode, cancellationToken);
        var enabled = await settings.GetBoolAsync(SettingKeys.SmsEnabled, false, cancellationToken);
        var parts = new[]
        {
            $"حالت درگاه: {(string.IsNullOrWhiteSpace(mode) ? SettingKeys.DefaultSmsMode : mode.Trim())}",
            enabled ? null : "کل سامانه پیامک خاموش است"
        };
        return string.Join(" — ", parts.Where(item => item is not null));
    }

    private void Require() => currentUser.Require(PermissionCatalog.Administration.ConnectionsManage);

    private Task RecordAsync(string test, ConnectionTestResultDto result, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditRecord(
            "TestConnection",
            "SystemSetting",
            null,
            result.Success ? "Success" : "Failure",
            result.Success ? null : result.Message,
            After: new { Test = test, result.Success, result.Detail }),
            cancellationToken);
}
