using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Application.Setup;
using NovaFleet.Domain.Auditing;
using NovaFleet.Domain.Configuration;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Settings;

namespace NovaFleet.Infrastructure.Setup;

/// <summary>
/// راه‌اندازی اولیه سامانه؛ تا زمانی که هیچ کاربری وجود نداشته باشد در دسترس است.
/// پس از ساخت مدیر محلی، فراخوانی دوباره آن همیشه رد می‌شود.
/// همه بلوک‌های تنظیمات پیش از هر نوشتن اعتبارسنجی می‌شوند، پس یک پاسخ ناقص
/// سامانه را در حالت نیمه‌پیکربندی رها نمی‌کند.
/// </summary>
public sealed class SetupService(
    NovaFleetDbContext db,
    IPasswordHasher passwordHasher,
    IRuntimeSettings runtime,
    IBootstrapConfigurationStore bootstrap,
    IDatabaseSetupProbe probe,
    IConfiguration configuration) : ISetupService
{
    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var userCount = await db.Users.CountAsync(cancellationToken);
        var hasAdmin = await db.Users.AnyAsync(item => item.IsLocalAdmin, cancellationToken);
        var organization = await db.SystemSettings
            .Where(item => item.Key == SettingKeys.OrganizationName)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);
        var completed = await db.SystemSettings
            .AnyAsync(item => item.Key == SettingKeys.SetupCompleted && item.Value == bool.TrueString, cancellationToken);

        return new SetupStatusDto(completed || userCount > 0 && hasAdmin, hasAdmin, userCount, organization);
    }

    /// <summary>
    /// وضعیت اتصال فعلی برای پیش‌پُر کردن گام دیتابیس. رمز هرگز برنمی‌گردد؛
    /// این مسیر پایگاه داده را لمس نمی‌کند تا پیش از وجود دیتابیس هم کار کند.
    /// </summary>
    public Task<SetupDatabaseStateDto> GetDatabaseStateAsync(CancellationToken cancellationToken = default)
    {
        var (value, source) = ConnectionStringResolver.Resolve(configuration, bootstrap);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult(new SetupDatabaseStateDto(false, source.ToString(), null, null, false, null, false));
        }

        try
        {
            var parsed = new SqlConnectionStringBuilder(value);
            var usesWindows = parsed.IntegratedSecurity;
            return Task.FromResult(new SetupDatabaseStateDto(
                true,
                source.ToString(),
                parsed.DataSource,
                parsed.InitialCatalog,
                usesWindows,
                usesWindows ? null : parsed.UserID,
                !usesWindows && !string.IsNullOrEmpty(parsed.Password)));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new SetupDatabaseStateDto(false, source.ToString(), null, null, false, null, false));
        }
    }

    /// <summary>
    /// گام دیتابیس: دیتابیس هدف را می‌سازد و مهاجرت می‌کند و بعد رشته اتصال را در فایل
    /// بوت‌استرپ می‌نویسد. تا سرویس روی همان دیتابیس بالا نیاید گام‌های بعدی مجاز نیست.
    /// </summary>
    public async Task<SetupDatabaseResultDto> ConfigureDatabaseAsync(
        SetupDatabaseRequest request,
        CancellationToken cancellationToken = default)
    {
        // گام دیتابیس ناشناس است؛ پس همان قفل «پس از نصب بسته می‌شود» را دارد، وگرنه
        // یک درخواست بعدی می‌تواند سرویس نصب‌شده را به سرور دیگری بفرستد.
        await RequireNotInstalledAsync(cancellationToken);
        var candidate = BuildConnectionString(request);

        var probeResult = await probe.PrepareAsync(candidate, cancellationToken);
        bootstrap.WriteConnectionString(candidate);

        var (active, source) = ConnectionStringResolver.Resolve(configuration, bootstrap);
        var isActive = source == ConnectionSource.BootstrapFile && Equivalent(active, candidate);

        db.AuditEntries.Add(new AuditEntry(
            "setup",
            "SetupDatabase",
            "SystemSetting",
            null,
            "Success",
            afterJson: JsonSerializer.Serialize(new
            {
                request.ServerName,
                request.DatabaseName,
                request.UseWindowsAuthentication,
                request.UserName,
                request.TrustServerCertificate,
                CreatedDatabase = probeResult.CreatedDatabase,
                File = bootstrap.FilePath
            }, JsonOptions),
            clientIpAddress: null));
        await db.SaveChangesAsync(cancellationToken);

        var hint = isActive
            ? $"پایگاه داده «{request.DatabaseName.Trim()}» روی «{request.ServerName.Trim()}» آماده است"
                + (probeResult.CreatedDatabase ? " و تازه ساخته شد" : " و از قبل وجود داشت")
                + $". رشته اتصال در {bootstrap.FilePath} ذخیره شد و همین حالا فعال است."
            : $"پایگاه داده آماده شد و رشته اتصال در {bootstrap.FilePath} ذخیره گردید. "
                + $"منبع فعال در حال حاضر {source} است؛ سرویس NovaFleet را راه‌اندازی مجدد کنید تا ویزارد از گام دوم ادامه دهد.";

        return new SetupDatabaseResultDto(!isActive, probeResult.CreatedDatabase, isActive, hint);
    }

    private string BuildConnectionString(SetupDatabaseRequest request)
    {
        var advanced = request.ConnectionString?.Trim();
        if (!string.IsNullOrEmpty(advanced))
        {
            try
            {
                _ = new SqlConnectionStringBuilder(advanced);
            }
            catch (ArgumentException)
            {
                throw new ValidationAppException("رشته اتصال پیشرفته قابل‌تحلیل نیست؛ نحو آن را بررسی کنید.");
            }

            return advanced;
        }

        var server = request.ServerName?.Trim() ?? string.Empty;
        var database = request.DatabaseName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new ValidationAppException("نام سرور پایگاه داده الزامی است.");
        }

        if (string.IsNullOrWhiteSpace(database) || database.Length > 128 || database.IndexOfAny([';', '=', '[', ']', '\'']) >= 0)
        {
            throw new ValidationAppException("نام پایگاه داده معتبر نیست؛ از ; = [ ] و ' استفاده نکنید.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = request.UseWindowsAuthentication,
            TrustServerCertificate = request.TrustServerCertificate,
            MultipleActiveResultSets = true
        };

        if (!request.UseWindowsAuthentication)
        {
            var user = request.UserName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(user))
            {
                throw new ValidationAppException("شناسه کاربری SQL الزامی است.");
            }

            // همان قرارداد «خالی یعنی دست‌نخورده» که در نوشتن اسرار رعایت می‌شود.
            var password = request.Password?.Trim();
            if (string.IsNullOrEmpty(password) && ReusablePassword(server, user) is { } saved)
            {
                password = saved;
            }
            else if (string.IsNullOrEmpty(password))
            {
                throw new ValidationAppException("رمز کاربر SQL الزامی است.");
            }

            builder.UserID = user;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// اگر روی همان سرور و همان کاربر قبلاً رمزی در منبع فعال باشد، مدیر نیازی به تایپ دوباره
    /// آن ندارد؛ رمز هیچ‌وقت از این متد به بیرون راه ندارد.
    /// </summary>
    private string? ReusablePassword(string server, string user)
    {
        var (value, _) = ConnectionStringResolver.Resolve(configuration, bootstrap);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var saved = new SqlConnectionStringBuilder(value);
            return !saved.IntegratedSecurity
                && string.Equals(saved.DataSource, server, StringComparison.OrdinalIgnoreCase)
                && string.Equals(saved.UserID, user, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(saved.Password)
                    ? saved.Password
                    : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool Equivalent(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        try
        {
            var a = new SqlConnectionStringBuilder(left);
            var b = new SqlConnectionStringBuilder(right);
            return string.Equals(a.DataSource, b.DataSource, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.InitialCatalog, b.InitialCatalog, StringComparison.OrdinalIgnoreCase)
                && a.IntegratedSecurity == b.IntegratedSecurity
                && string.Equals(a.UserID, b.UserID, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task RequireNotInstalledAsync(CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            throw new ConflictAppException("راه‌اندازی اولیه قبلاً انجام شده است.");
        }
    }

    /// <summary>
    /// اگر فایل بوت‌استرپ چیزی جز همان اتصالی که سرویس با آن بالا آمده بگوید، مدیر
    /// سرویس را restart نکرده و گام‌های بعدی روی دیتابیس اشتباه نوشته می‌شدند.
    /// </summary>
    private void RequireBootstrapDatabaseActive()
    {
        var stored = bootstrap.ReadConnectionString();
        if (string.IsNullOrWhiteSpace(stored))
        {
            return;
        }

        if (Equivalent(db.Database.GetConnectionString(), stored))
        {
            return;
        }

        throw new ConflictAppException(
            $"سرویس هنوز به پایگاه داده دیگری وصل است و فایل {bootstrap.FilePath} اعمال نشده؛ "
            + "اول سرویس NovaFleet را راه‌اندازی مجدد کنید، سپس ویزارد را ادامه دهید.");
    }

    public async Task<SetupStatusDto> InitializeAsync(SetupRequest request, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            throw new ConflictAppException("راه‌اندازی اولیه قبلاً انجام شده است.");
        }

        RequireBootstrapDatabaseActive();

        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrganizationName, nameof(request.OrganizationName));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AdminUsername, nameof(request.AdminUsername));
        if (request.AdminPassword.Length < 8)
        {
            throw new ValidationAppException("رمز مدیر محلی باید حداقل ۸ نویسه باشد.");
        }

        var summary = ValidateBlocks(request);

        var admin = new ApplicationUser(
            request.AdminUsername,
            request.AdminFirstName,
            request.AdminLastName,
            isLocalAdmin: true);
        admin.SetPasswordHash(passwordHasher.Hash(request.AdminPassword));
        admin.UpdateProfile(request.AdminFirstName, request.AdminLastName, request.AdminMobile, null, null);

        await WriteBlocksAsync(request, cancellationToken);

        db.Users.Add(admin);
        db.SystemSettings.Add(new SystemSetting(SettingKeys.SetupCompleted, bool.TrueString));
        db.AuditEntries.Add(new AuditEntry(
            admin.Username, "Setup", "System", null, "Success",
            afterJson: JsonSerializer.Serialize(summary, JsonOptions),
            clientIpAddress: null));
        await db.SaveChangesAsync(cancellationToken);

        return await GetStatusAsync(cancellationToken);
    }

    /// <summary>
    /// بلوک‌های ارسالی را می‌سنجد و خلاصه‌ای بدون رمز برای ممیزی برمی‌گرداند.
    /// کلیدهای Has* عمداً فقط «ست شد / شد» هستند تا هیچ مقدار حساسی نوشته نشود.
    /// </summary>
    private static SortedDictionary<string, object> ValidateBlocks(SetupRequest request)
    {
        var summary = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["organizationName"] = SettingsWriters.ValidateOrganization(
                new OrganizationProfileDto(request.OrganizationName, request.LogoDataUrl))
        };

        if (request.Directory is { } directory)
        {
            summary["directory"] = SettingsWriters.ValidateDirectory(directory);
            summary["directoryEnabled"] = directory.Enabled;
            summary["directoryBindPasswordSet"] = !string.IsNullOrWhiteSpace(directory.BindPassword);
        }

        if (request.Sms is { } sms)
        {
            summary["sms"] = SettingsWriters.ValidateSms(sms);
            summary["smsEnabled"] = sms.Enabled;
            summary["smsPasswordSet"] = !string.IsNullOrWhiteSpace(sms.Password);
        }

        if (request.Operations is { } operations)
        {
            var (level, paper, fontPath) = SettingsWriters.ValidateOperations(operations);
            summary["logLevel"] = level;
            summary["reportPaperSize"] = paper;
            summary["pdfFontPath"] = fontPath ?? string.Empty;
        }

        if (request.Rules is { } rules)
        {
            SettingsWriters.ValidateMissionRules(rules);
            summary["missionRules"] = new
            {
                rules.MinimumGapMinutes,
                rules.FutureRequestDays,
                rules.WithdrawalAutoCloseHours,
                rules.StartReminderMinutes
            };
        }

        if (request.Session is { } session)
        {
            SettingsWriters.ValidateSessionPolicy(session);
            summary["sessionPolicy"] = new { session.AbsoluteMinutes, session.IdleMinutes };
        }

        if (request.Template is { })
        {
            summary["missionOrderTemplate"] = true;
        }

        return summary;
    }

    private async Task WriteBlocksAsync(SetupRequest request, CancellationToken cancellationToken)
    {
        await SettingsWriters.WriteOrganizationAsync(
            runtime, new OrganizationProfileDto(request.OrganizationName, request.LogoDataUrl), cancellationToken);

        if (request.Directory is { } directory)
        {
            await SettingsWriters.WriteDirectoryAsync(runtime, directory, cancellationToken);
        }

        if (request.Sms is { } sms)
        {
            await SettingsWriters.WriteSmsAsync(runtime, sms, cancellationToken);
        }

        if (request.Operations is { } operations)
        {
            await SettingsWriters.WriteOperationsAsync(runtime, operations, cancellationToken);
        }

        if (request.Rules is { } rules)
        {
            await SettingsWriters.WriteMissionRulesAsync(runtime, rules, cancellationToken);
        }

        if (request.Session is { } session)
        {
            await SettingsWriters.WriteSessionPolicyAsync(runtime, session, cancellationToken);
        }

        if (request.Template is { } template)
        {
            await SettingsWriters.WriteOrderTemplateAsync(runtime, template, cancellationToken);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
