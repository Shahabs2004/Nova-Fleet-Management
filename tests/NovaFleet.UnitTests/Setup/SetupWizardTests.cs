using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Application.Setup;
using NovaFleet.Infrastructure.Setup;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Setup;

/// <summary>
/// ویزارد راه‌اندازی: گام دیتابیس، نوشتن همه بلوک‌های تنظیمات در تأیید نهایی،
/// و قفل‌هایی که نصب تکراری یا نوشتن روی دیتابیس اشتباه را رد می‌کنند.
/// پایگاه داده آزمون باید بدون کاربر باشد تا «هنوز نصب نشده» معنا داشته باشد،
/// پس به‌جای MissionTestBase که سه کاربر می‌کارد، همان رکورد در ابتدا پاک می‌شود.
/// </summary>
public class SetupWizardTests : MissionTestBase
{
    private const string AdminPassword = "Setup#2026-Pass";
    private const string SmsSecret = "sms-secret";
    private const string BindSecret = "bind-secret";

    public SetupWizardTests()
    {
        Db.Users.RemoveRange(Db.Users);
        Db.SaveChanges();
    }

    [Fact]
    public async Task Initialize_WritesEveryBlockAndMarksSetupComplete()
    {
        var status = await Wizard().InitializeAsync(FullRequest());

        Assert.True(status.IsCompleted);
        Assert.True(status.HasLocalAdmin);
        Assert.Equal(1, status.UserCount);
        Assert.Equal("شرکت نوین", status.OrganizationName);

        Assert.Equal("شرکت نوین", await Runtime.GetStringAsync(SettingKeys.OrganizationName));
        Assert.Equal("data:image/png;base64,AAA", await Runtime.GetStringAsync(SettingKeys.OrganizationLogo));
        Assert.Equal("dc.example", await Runtime.GetStringAsync(SettingKeys.DirectoryHost));
        Assert.Equal("636", await Runtime.GetStringAsync(SettingKeys.DirectoryPort));
        Assert.Equal("DC=corp,DC=example", await Runtime.GetStringAsync(SettingKeys.DirectoryBaseDn));
        Assert.Equal("(&(objectClass=user)(sAMAccountName={0}))", await Runtime.GetStringAsync(SettingKeys.DirectorySearchFilter));
        Assert.Equal(BindSecret, await Runtime.GetSecretAsync(SettingKeys.DirectoryBindPassword));
        Assert.Equal("Wsdl", await Runtime.GetStringAsync(SettingKeys.SmsMode));
        Assert.Equal("1000999", await Runtime.GetStringAsync(SettingKeys.SmsSenderNumber));
        Assert.Equal(SmsSecret, await Runtime.GetSecretAsync(SettingKeys.SmsPassword));
        Assert.Equal("Warning", await Runtime.GetStringAsync(SettingKeys.OperationsLogLevel));
        Assert.Equal("A5", await Runtime.GetStringAsync(SettingKeys.BrandingReportPaperSize));
        Assert.Equal("C:\\Fonts\\Vazir.ttf", await Runtime.GetStringAsync(SettingKeys.BrandingPdfFontPath));
        Assert.Equal("45", await Runtime.GetStringAsync(SettingKeys.MinimumGapMinutes));
        Assert.Equal("90", await Runtime.GetStringAsync(SettingKeys.SmsReminderMinutes));
        Assert.Equal("360", await Runtime.GetStringAsync(SettingKeys.SessionAbsoluteMinutes));
        Assert.Equal("سرناوگان", await Runtime.GetStringAsync(SettingKeys.MissionOrderHeader));
        Assert.True(await Runtime.GetBoolAsync(SettingKeys.MissionOrderShowLogo, false));
        Assert.Equal(bool.TrueString, await Runtime.GetStringAsync(SettingKeys.SetupCompleted));

        var admin = await Db.Users.SingleAsync();
        Assert.True(admin.IsLocalAdmin);
        Assert.Equal("demo.setup.admin", admin.Username);
        Assert.NotEqual(AdminPassword, admin.PasswordHash);
    }

    [Fact]
    public async Task Initialize_KeepsSecretsOutOfPlainValuesAndAudit()
    {
        await Wizard().InitializeAsync(FullRequest());

        var plain = await Runtime.ReadAllAsync();
        Assert.False(plain.ContainsKey(SettingKeys.SmsPassword));
        Assert.False(plain.ContainsKey(SettingKeys.DirectoryBindPassword));
        Assert.DoesNotContain(SmsSecret, plain.Values);

        var row = await Db.SystemSettings.SingleAsync(item => item.Key == SettingKeys.SmsPassword);
        Assert.True(row.IsSecret);
        Assert.Equal(string.Empty, row.Value);
        Assert.True(row.HasProtectedValue);

        var audit = await Db.AuditEntries.SingleAsync(item => item.Action == "Setup");
        Assert.Equal("demo.setup.admin", audit.ActorUsername);
        Assert.Contains("smsPasswordSet", audit.AfterJson);
        Assert.DoesNotContain(SmsSecret, audit.AfterJson);
        Assert.DoesNotContain(BindSecret, audit.AfterJson);
        Assert.DoesNotContain(AdminPassword, audit.AfterJson);
    }

    [Fact]
    public async Task Initialize_InvalidBlockAbortsBeforeAnyWrite()
    {
        var request = FullRequest() with { Sms = FullSms with { Mode = "Rest" } };

        var error = await Assert.ThrowsAsync<ValidationAppException>(() => Wizard().InitializeAsync(request));

        Assert.Equal("روش ارسال پیامک باید Simulation، Wsdl یا Api باشد.", error.Message);
        Assert.Empty(Db.SystemSettings.Local);
        Assert.False(Db.Users.Local.Any());
        Assert.False(Db.AuditEntries.Local.Any());
    }

    [Fact]
    public async Task Initialize_RejectsWeakAdminPassword()
    {
        var request = FullRequest() with { AdminPassword = "short1" };

        var error = await Assert.ThrowsAsync<ValidationAppException>(() => Wizard().InitializeAsync(request));

        Assert.Equal("رمز مدیر محلی باید حداقل ۸ نویسه باشد.", error.Message);
    }

    [Fact]
    public async Task Initialize_SecondRunIsRefused()
    {
        await Wizard().InitializeAsync(FullRequest());

        var error = await Assert.ThrowsAsync<ConflictAppException>(() => Wizard().InitializeAsync(FullRequest()));

        Assert.Equal("راه‌اندازی اولیه قبلاً انجام شده است.", error.Message);
    }

    [Fact]
    public async Task Initialize_WithoutOptionalBlocksLeavesDefaultsUntouched()
    {
        var minimal = new SetupRequest("شرکت نوین", "demo.setup.admin", AdminPassword, "شهاب", "مدیر", "09121112233");

        await Wizard().InitializeAsync(minimal);

        Assert.Equal("شرکت نوین", await Runtime.GetStringAsync(SettingKeys.OrganizationName));
        Assert.Null(await Runtime.GetStringAsync(SettingKeys.SmsMode));
        Assert.Null(await Runtime.GetStringAsync(SettingKeys.DirectoryHost));
        Assert.Equal(bool.TrueString, await Runtime.GetStringAsync(SettingKeys.SetupCompleted));
    }

    [Fact]
    public async Task Initialize_RefusedWhileBootstrapDatabaseIsNotActiveYet()
    {
        var bootstrap = new FakeBootstrapStore
        {
            Readable = "Server=SHAHABPC;Database=OtherDb;Integrated Security=True;TrustServerCertificate=True"
        };

        var error = await Assert.ThrowsAsync<ConflictAppException>(
            () => Wizard(bootstrap: bootstrap).InitializeAsync(FullRequest()));

        Assert.Contains("راه‌اندازی مجدد", error.Message);
        Assert.False(Db.Users.Local.Any());
    }

    [Fact]
    public async Task ConfigureDatabase_BuildsWindowsAuthAndReportsItActive()
    {
        var bootstrap = new FakeBootstrapStore();
        var probe = new RecordingProbe { CreatedDatabase = true };

        var result = await Wizard(bootstrap, probe).ConfigureDatabaseAsync(
            new SetupDatabaseRequest("SHAHABPC", "NovaFleetDb", true, null, null, true));

        var built = Assert.Single(probe.Candidates);
        Assert.Equal(built, bootstrap.Written);
        Assert.Equal("SHAHABPC", new SqlConnectionStringBuilder(built).DataSource);
        Assert.Equal("NovaFleetDb", new SqlConnectionStringBuilder(built).InitialCatalog);
        Assert.True(new SqlConnectionStringBuilder(built).IntegratedSecurity);
        Assert.True(result.CreatedDatabase);
        Assert.True(result.SameAsActive);
        Assert.False(result.RestartRequired);
        Assert.Contains("همین حالا فعال است", result.Hint);

        var audit = await Db.AuditEntries.SingleAsync(item => item.Action == "SetupDatabase");
        Assert.DoesNotContain("password", audit.AfterJson);
    }

    [Fact]
    public async Task ConfigureDatabase_ReportsRestartUntilServicePointsAtNewFile()
    {
        var bootstrap = new FakeBootstrapStore
        {
            Readable = "Server=SHAHABPC;Database=OldDb;Integrated Security=True",
            AppliesImmediately = false
        };

        var result = await Wizard(bootstrap).ConfigureDatabaseAsync(
            new SetupDatabaseRequest("SHAHABPC", "NovaFleetDb", true, null, null, true));

        Assert.True(result.RestartRequired);
        Assert.False(result.SameAsActive);
        Assert.Contains("راه‌اندازی مجدد", result.Hint);
    }

    [Fact]
    public async Task ConfigureDatabase_RejectsUnsafeDatabaseName()
    {
        var bootstrap = new FakeBootstrapStore();
        var probe = new RecordingProbe();

        var error = await Assert.ThrowsAsync<ValidationAppException>(
            () => Wizard(bootstrap, probe).ConfigureDatabaseAsync(
                new SetupDatabaseRequest("SHAHABPC", "Nova[Fleet];x", true, null, null, true)));

        Assert.Equal("نام پایگاه داده معتبر نیست؛ از ; = [ ] و ' استفاده نکنید.", error.Message);
        Assert.Empty(probe.Candidates);
        Assert.Null(bootstrap.Written);
    }

    [Fact]
    public async Task ConfigureDatabase_AdvancedConnectionStringOverridesFields()
    {
        const string advanced = "Server=REMOTE\\SQLEXPRESS;Database=NovaFleetDb;Integrated Security=True";
        var probe = new RecordingProbe();

        await Wizard(probe: probe).ConfigureDatabaseAsync(
            new SetupDatabaseRequest("نادیده‌گرفته", "نادیده", false, null, null, false, advanced));

        Assert.Equal(advanced, Assert.Single(probe.Candidates));

        var unparsable = await Assert.ThrowsAsync<ValidationAppException>(
            () => Wizard(probe: new RecordingProbe()).ConfigureDatabaseAsync(
                new SetupDatabaseRequest("SHAHABPC", "NovaFleetDb", true, null, null, true, "this is not a connection string")));
        Assert.Equal("رشته اتصال پیشرفته قابل‌تحلیل نیست؛ نحو آن را بررسی کنید.", unparsable.Message);
    }

    [Fact]
    public async Task ConfigureDatabase_RequiresSqlCredentialsAndReusesSavedPassword()
    {
        var missingUser = await Assert.ThrowsAsync<ValidationAppException>(
            () => Wizard().ConfigureDatabaseAsync(
                new SetupDatabaseRequest("SHAHABPC", "NovaFleetDb", false, null, null, true)));
        Assert.Equal("شناسه کاربری SQL الزامی است.", missingUser.Message);

        var missingPassword = await Assert.ThrowsAsync<ValidationAppException>(
            () => Wizard().ConfigureDatabaseAsync(
                new SetupDatabaseRequest("SHAHABPC", "NovaFleetDb", false, "svc.nova", null, true)));
        Assert.Equal("رمز کاربر SQL الزامی است.", missingPassword.Message);

        var bootstrap = new FakeBootstrapStore
        {
            Readable = $"Server=SHAHABPC;Database=OldDb;User ID=svc.nova;Password={SmsSecret};TrustServerCertificate=True",
            AppliesImmediately = false
        };
        var probe = new RecordingProbe();

        await Wizard(bootstrap, probe).ConfigureDatabaseAsync(
            new SetupDatabaseRequest("shahabpc", "NovaFleetDb", false, "SVC.NOVA", "   ", true));

        Assert.Equal(SmsSecret, new SqlConnectionStringBuilder(Assert.Single(probe.Candidates)).Password);
    }

    [Fact]
    public async Task ConfigureDatabase_IsClosedOnceInstalled()
    {
        await Wizard().InitializeAsync(FullRequest());

        await Assert.ThrowsAsync<ConflictAppException>(
            () => Wizard().ConfigureDatabaseAsync(
                new SetupDatabaseRequest("SHAHABPC", "NovaFleetDb", true, null, null, true)));
    }

    [Fact]
    public async Task GetDatabaseState_ReturnsSourceButNeverThePassword()
    {
        var bootstrap = new FakeBootstrapStore
        {
            Readable = $"Server=SHAHABPC;Database=NovaFleetDb;User ID=svc.nova;Password={SmsSecret};TrustServerCertificate=True"
        };

        var state = await Wizard(bootstrap).GetDatabaseStateAsync();

        Assert.True(state.IsConfigured);
        Assert.Equal(nameof(ConnectionSource.BootstrapFile), state.Source);
        Assert.Equal("SHAHABPC", state.ServerName);
        Assert.Equal("NovaFleetDb", state.DatabaseName);
        Assert.Equal("svc.nova", state.UserName);
        Assert.False(state.UseWindowsAuthentication);
        Assert.True(state.HasPassword);
    }

    [Fact]
    public async Task GetDatabaseState_ReportsUnconfiguredWhenNothingIsResolved()
    {
        var state = await Wizard().GetDatabaseStateAsync();

        Assert.False(state.IsConfigured);
        Assert.Equal(nameof(ConnectionSource.None), state.Source);
        Assert.Null(state.ServerName);
    }

    private SetupService Wizard(
        FakeBootstrapStore? bootstrap = null,
        RecordingProbe? probe = null) =>
        new(Db, new FakePasswordHasher(), Runtime, bootstrap ?? new FakeBootstrapStore(),
            probe ?? new RecordingProbe(), new ConfigurationBuilder().Build());

    private static UpdateSmsConnectionSettingsDto FullSms => new(
        Enabled: true,
        Mode: "Wsdl",
        Endpoint: "https://sms.example/soap",
        Organization: "سازمان",
        Username: "svc.user",
        SenderNumber: "1000999",
        TimeoutSeconds: 30,
        RetryMinutes: 7,
        MaxAttempts: 4,
        Password: SmsSecret);

    private static SetupRequest FullRequest() => new(
        OrganizationName: "شرکت نوین",
        AdminUsername: "demo.setup.admin",
        AdminPassword: AdminPassword,
        AdminFirstName: "شهاب",
        AdminLastName: "مدیر",
        AdminMobile: "09121112233",
        LogoDataUrl: "data:image/png;base64,AAA",
        Directory: new UpdateDirectorySettingsDto(
            Enabled: true,
            Host: "dc.example",
            Port: 636,
            UseSsl: true,
            Domain: "example",
            BaseDn: "DC=corp,DC=example",
            BindDn: "CN=svc-bind,DC=corp,DC=example",
            TimeoutSeconds: 15,
            SearchFilter: "(&(objectClass=user)(sAMAccountName={0}))",
            BindPassword: BindSecret),
        Sms: FullSms,
        Operations: new UpdateOperationsSettingsDto("Warning", "C:\\Fonts\\Vazir.ttf", "A5"),
        Rules: new MissionRulesDto(45, 20, 12, 90),
        Session: new SessionPolicyDto(360, 60),
        Template: new MissionOrderTemplateDto("سرناوگان", "پاورقی آزمون", true));

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string passwordHash, string password) => passwordHash == $"hashed:{password}";
    }

    private sealed class FakeBootstrapStore : IBootstrapConfigurationStore
    {
        public string? Readable { get; set; }

        public string? Written { get; private set; }

        /// <summary>false یعنی فایل نوشته شد اما سرویس هنوز روی اتصال قبلی بالا است.</summary>
        public bool AppliesImmediately { get; init; } = true;

        public string FilePath => @"C:\novafleet\bootstrap.json";

        public string? ReadConnectionString() => AppliesImmediately ? Written ?? Readable : Readable;

        public void WriteConnectionString(string connectionString) => Written = connectionString;
    }

    private sealed class RecordingProbe : IDatabaseSetupProbe
    {
        public List<string> Candidates { get; } = [];

        public bool CreatedDatabase { get; init; }

        public Task<DatabaseProbeResult> PrepareAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            Candidates.Add(connectionString);
            return Task.FromResult(new DatabaseProbeResult(CreatedDatabase));
        }
    }
}
