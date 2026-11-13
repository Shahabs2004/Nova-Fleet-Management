using Microsoft.Extensions.Configuration;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Infrastructure.Settings;
using NovaFleet.Infrastructure.Setup;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Settings;

/// <summary>
/// آزمون‌های زنده تب «اتصال سرویس‌ها»: نگاشت نتیجه پروب‌ها به پیام مدیر و
/// نشت‌نکردن هیچ رمزی به پیام، توضیح یا ممیزی.
/// </summary>
public class ConnectionTestsServiceTests : MissionTestBase
{
    private const string Secret = "Sup3r-S3cret!";

    private ConnectionTestsService ServiceFor(
        ICurrentUser user,
        FakeDatabaseProbe database,
        FakeDirectory directory,
        FakeSms sms,
        string? storedConnectionString) =>
        new(Runtime, user, Audit, new FakeBootstrapStore(storedConnectionString),
            new ConfigurationBuilder().Build(), database, directory, sms);

    private FakeCurrentUser Manager() => new("demo.sysadmin", PermissionCatalog.Administration.ConnectionsManage);

    [Fact]
    public async Task AnonymousCallerIsForbiddenFromEveryTest()
    {
        var service = ServiceFor(AnonymousCurrentUser.Instance,
            new FakeDatabaseProbe(), new FakeDirectory(), new FakeSms(), null);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.TestDatabaseAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.TestDirectoryAsync(null));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.TestSmsBalanceAsync());
    }

    [Fact]
    public async Task Database_WithoutAnyConnectionStringProbesNothing()
    {
        var database = new FakeDatabaseProbe();
        var service = ServiceFor(Manager(), database, new FakeDirectory(), new FakeSms(), null);

        var result = await service.TestDatabaseAsync();

        Assert.False(result.Success);
        Assert.Null(database.UsedConnectionString);
    }

    [Fact]
    public async Task Database_PendingMigrationsAreReportedAsRestartWork()
    {
        var database = new FakeDatabaseProbe { Result = new DatabaseHealthResult(true, true) };
        var service = ServiceFor(Manager(), database, new FakeDirectory(), new FakeSms(), ConnectionString);

        var result = await service.TestDatabaseAsync();

        Assert.True(result.Success);
        Assert.Equal(ConnectionString, database.UsedConnectionString);
        Assert.Contains("مهاجرت", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_FailureKeepsThePasswordOutOfEverythingReturned()
    {
        var database = new FakeDatabaseProbe { Result = new DatabaseHealthResult(false, false) };
        var service = ServiceFor(Manager(), database, new FakeDirectory(), new FakeSms(), ConnectionString);

        var result = await service.TestDatabaseAsync();

        Assert.False(result.Success);
        AssertNoSecret(result.Message);
        AssertNoSecret(result.Detail);
        AssertNoSecret(Audit.Records.Single().After);
    }

    [Fact]
    public async Task Directory_BindFailureSurfacesItsOwnReason()
    {
        var directory = new FakeDirectory
        {
            Result = DirectoryProbeResult.Failed("حساب Bind یا رمز آن در دامنه پذیرفته نشد.")
        };
        var service = ServiceFor(Manager(), new FakeDatabaseProbe(), directory, new FakeSms(), ConnectionString);

        var result = await service.TestDirectoryAsync("demo.user");

        Assert.False(result.Success);
        Assert.Equal("demo.user", directory.UsedSampleUsername);
        Assert.Contains("پذیرفته نشد", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Directory_SearchLimitIsReportedAsALowerBound()
    {
        var directory = new FakeDirectory { Result = DirectoryProbeResult.Bound(25, true) };
        var service = ServiceFor(Manager(), new FakeDatabaseProbe(), directory, new FakeSms(), ConnectionString);

        var result = await service.TestDirectoryAsync(null);

        Assert.True(result.Success);
        Assert.Contains("حداقل 25", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Directory_WithoutAnyUserSaysTheBaseDnIsWrong()
    {
        var directory = new FakeDirectory { Result = DirectoryProbeResult.Bound(0, false) };
        var service = ServiceFor(Manager(), new FakeDatabaseProbe(), directory, new FakeSms(), ConnectionString);

        var result = await service.TestDirectoryAsync(null);

        Assert.True(result.Success);
        Assert.Contains("BaseDn", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sms_BalanceReportsGatewayModeAndTheGlobalSwitch()
    {
        await Runtime.SetAsync(SettingKeys.SmsMode, "Wsdl");
        await Runtime.SetAsync(SettingKeys.SmsEnabled, bool.FalseString);
        var sms = new FakeSms { Result = SmsBalanceResult.Of("12500") };
        var service = ServiceFor(Manager(), new FakeDatabaseProbe(), new FakeDirectory(), sms, ConnectionString);

        var result = await service.TestSmsBalanceAsync();

        Assert.True(result.Success);
        Assert.Contains("12500", result.Detail, StringComparison.Ordinal);
        Assert.Contains("Wsdl", result.Detail, StringComparison.Ordinal);
        Assert.Contains("خاموش", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sms_FailureKeepsItsReasonAndNeverTheStoredPassword()
    {
        await Runtime.SetAsync(SettingKeys.SmsMode, "Wsdl");
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, Secret);
        var sms = new FakeSms { Result = SmsBalanceResult.Failure("نام کاربری یا رمز اشتباه است.") };
        var service = ServiceFor(Manager(), new FakeDatabaseProbe(), new FakeDirectory(), sms, ConnectionString);

        var result = await service.TestSmsBalanceAsync();

        Assert.False(result.Success);
        Assert.Contains("نام کاربری یا رمز اشتباه است", result.Detail, StringComparison.Ordinal);
        AssertNoSecret(result.Message);
        AssertNoSecret(result.Detail);
    }

    [Fact]
    public async Task EveryTestIsAuditedOnce()
    {
        var service = ServiceFor(Manager(), new FakeDatabaseProbe(), new FakeDirectory(), new FakeSms(), ConnectionString);

        await service.TestDatabaseAsync();
        await service.TestDirectoryAsync(null);
        await service.TestSmsBalanceAsync();

        Assert.Equal(3, Audit.Records.Count(record => record.Action == "TestConnection"));
    }

    /// <summary>رشته اتصالی که هم نام کاربر و هم رمز در آن هست تا نشت‌نکردنشان سنجیده شود.</summary>
    private static string ConnectionString =>
        $"Server=SHAHABPC;Database=NovaFleetDb;User ID=novafleet_app;Password={Secret};TrustServerCertificate=True";

    private static void AssertNoSecret(object? value) => Assert.DoesNotContain(
        Secret,
        value?.ToString() ?? string.Empty,
        StringComparison.Ordinal);

    private sealed record FakeDatabaseProbe : IDatabaseHealthProbe
    {
        public DatabaseHealthResult Result { get; init; } = new(false, false);

        public string? UsedConnectionString { get; private set; }

        public Task<DatabaseHealthResult> ProbeAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            UsedConnectionString = connectionString;
            return Task.FromResult(Result);
        }
    }

    private sealed record FakeDirectory : IDirectoryAuthenticator
    {
        public DirectoryProbeResult Result { get; init; } = DirectoryProbeResult.Bound(1, false);

        public string? UsedSampleUsername { get; private set; }

        public Task<DirectoryProbeResult> ProbeAsync(string? sampleUsername, CancellationToken cancellationToken = default)
        {
            UsedSampleUsername = sampleUsername;
            return Task.FromResult(Result);
        }

        public Task<DirectoryAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
            => Task.FromResult(DirectoryAuthResult.Failed("unused"));

        public Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult<DirectoryUserInfo?>(null);
    }

    private sealed record FakeSms : ISmsGateway
    {
        public SmsBalanceResult Result { get; init; } = SmsBalanceResult.Of("0");

        public Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result);

        public Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
            => Task.FromResult(SmsSendResult.Success(null, null));

        public Task<SmsDeliveryResult> GetDeliveryStatusAsync(string providerReference, CancellationToken cancellationToken = default)
            => Task.FromResult(new SmsDeliveryResult(SmsDeliveryState.Unknown, null));
    }

    private sealed record FakeBootstrapStore(string? Value) : IBootstrapConfigurationStore
    {
        public string FilePath => Path.Combine(Path.GetTempPath(), "novafleet-connection-test.json");

        public string? ReadConnectionString() => Value;

        public void WriteConnectionString(string connectionString)
        {
        }
    }
}
