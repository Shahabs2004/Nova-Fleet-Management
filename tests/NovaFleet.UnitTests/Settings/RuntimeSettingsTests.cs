using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Settings;
using NovaFleet.Infrastructure.Setup;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Settings;

/// <summary>
/// لایه تنظیمات runtime: خواندن/نوشتن از پایگاه داده، انبارش رمزنگاری‌شده اسرار
/// و کلید سراسری فعال/غیرفعال کردن ارسال پیامک.
/// </summary>
public class RuntimeSettingsTests : MissionTestBase
{
    private ConnectionsSettingsService ConnectionsFor(ICurrentUser user, FakeBootstrapStore? bootstrap = null) =>
        new(Runtime, user, Audit, bootstrap ?? new FakeBootstrapStore(), new ConfigurationBuilder().Build());

    [Fact]
    public async Task Secret_RoundTripsAndStaysOutOfPlainReads()
    {
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "  top-secret  ");

        Assert.Equal("top-secret", await Runtime.GetSecretAsync(SettingKeys.SmsPassword));
        Assert.True(await Runtime.HasSecretAsync(SettingKeys.SmsPassword));
        Assert.Null(await Runtime.GetStringAsync(SettingKeys.SmsPassword));
        Assert.False((await Runtime.ReadAllAsync()).ContainsKey(SettingKeys.SmsPassword));
        Assert.Null(await Settings.GetValueAsync(SettingKeys.SmsPassword));
    }

    [Fact]
    public async Task Secret_BlankWriteKeepsStoredValue()
    {
        await Runtime.SetSecretAsync(SettingKeys.DirectoryBindPassword, "first");

        await Runtime.SetSecretAsync(SettingKeys.DirectoryBindPassword, "   ");

        Assert.Equal("first", await Runtime.GetSecretAsync(SettingKeys.DirectoryBindPassword));
    }

    [Fact]
    public async Task Secret_ClearRemovesItEntirely()
    {
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "value");

        await Runtime.ClearSecretAsync(SettingKeys.SmsPassword);

        Assert.False(await Runtime.HasSecretAsync(SettingKeys.SmsPassword));
        Assert.Null(await Runtime.GetSecretAsync(SettingKeys.SmsPassword));
    }

    [Fact]
    public async Task PlainWrite_OverwritesASecretRow()
    {
        await Runtime.SetSecretAsync(SettingKeys.SmsUsername, "stored");

        await Runtime.SetAsync(SettingKeys.SmsUsername, "visible");

        Assert.Equal("visible", await Runtime.GetStringAsync(SettingKeys.SmsUsername));
        Assert.False(await Runtime.HasSecretAsync(SettingKeys.SmsUsername));
    }

    [Fact]
    public async Task Connections_AnonymousCallerIsForbidden()
    {
        var service = ConnectionsFor(AnonymousCurrentUser.Instance);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetSmsAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetDirectoryAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetOperationsAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetDatabaseAsync());
    }

    [Fact]
    public async Task Connections_SmsSavePersistsModeAndSecretAndAudits()
    {
        var service = ConnectionsFor(new FakeCurrentUser("demo.sysadmin", PermissionCatalog.Administration.ConnectionsManage));

        var saved = await service.UpdateSmsAsync(new UpdateSmsConnectionSettingsDto(
            true, "Wsdl", "https://sms.example/soap", "سازمان", "svc.user", "1000999", 30, 7, 4, "p@ssword"));

        Assert.False(saved.RestartRequired);
        var read = await service.GetSmsAsync();
        Assert.True(read.Enabled);
        Assert.Equal("Wsdl", read.Mode);
        Assert.Equal(30, read.TimeoutSeconds);
        Assert.True(read.HasPassword);
        Assert.Equal("p@ssword", await Runtime.GetSecretAsync(SettingKeys.SmsPassword));
        Assert.True(Audit.Wrote("UpdateSmsSettings"));
    }

    [Fact]
    public async Task Connections_SmsSaveWithoutPasswordKeepsTheStoredOne()
    {
        var manager = new FakeCurrentUser("demo.sysadmin", PermissionCatalog.Administration.ConnectionsManage);
        var service = ConnectionsFor(manager);
        await service.UpdateSmsAsync(new UpdateSmsConnectionSettingsDto(
            true, "Wsdl", "https://sms.example/soap", null, "svc.user", null, 20, 5, 5, "original"));

        await service.UpdateSmsAsync(new UpdateSmsConnectionSettingsDto(
            true, "Wsdl", "https://sms.example/soap", null, "svc.user", null, 20, 5, 5, null));

        Assert.Equal("original", await Runtime.GetSecretAsync(SettingKeys.SmsPassword));
    }

    [Fact]
    public async Task Connections_EnabledRealGatewayNeedsEndpointAndUsername()
    {
        var service = ConnectionsFor(new FakeCurrentUser("demo.sysadmin", PermissionCatalog.Administration.ConnectionsManage));

        await Assert.ThrowsAsync<ValidationAppException>(() => service.UpdateSmsAsync(
            new UpdateSmsConnectionSettingsDto(true, "Wsdl", null, null, null, null, 20, 5, 5, null)));

        // در حالت شبیه‌سازی یا خاموشی کلید سراسری، نداشتن آدرس مجاز است.
        var saved = await service.UpdateSmsAsync(
            new UpdateSmsConnectionSettingsDto(false, "Wsdl", null, null, null, null, 20, 5, 5, null));
        Assert.False((await service.GetSmsAsync()).Enabled);
        Assert.False(saved.RestartRequired);
    }

    [Fact]
    public async Task Connections_RejectsInvalidDirectoryFilterAndEndpoint()
    {
        var service = ConnectionsFor(new FakeCurrentUser("demo.sysadmin", PermissionCatalog.Administration.ConnectionsManage));

        await Assert.ThrowsAsync<ValidationAppException>(() => service.UpdateDirectoryAsync(
            new UpdateDirectorySettingsDto(true, "dc.example", 636, true, "example", "DC=corp", null, 15,
                "(&(objectClass=user))", null)));

        await Assert.ThrowsAsync<ValidationAppException>(() => service.UpdateSmsAsync(
            new UpdateSmsConnectionSettingsDto(true, "Wsdl", "sms.example/soap", null, "user", null, 20, 5, 5, "x")));
    }

    [Fact]
    public async Task Connections_OperationsSaveReportsRestart()
    {
        var service = ConnectionsFor(new FakeCurrentUser("demo.sysadmin", PermissionCatalog.Administration.ConnectionsManage));

        var saved = await service.UpdateOperationsAsync(new UpdateOperationsSettingsDto("Debug", null, "A5"));

        Assert.True(saved.RestartRequired);
        Assert.NotNull(saved.Hint);
        Assert.Equal("Debug", (await service.GetOperationsAsync()).LogLevel);
        Assert.True(Audit.Wrote("UpdateOperationsSettings"));
    }

    [Fact]
    public async Task Connections_DatabaseSaveWritesBootstrapAndRequiresRestart()
    {
        var bootstrap = new FakeBootstrapStore();
        var service = ConnectionsFor(
            new FakeCurrentUser("demo.sysadmin", PermissionCatalog.Administration.ConnectionsManage), bootstrap);

        var saved = await service.UpdateDatabaseAsync(new DatabaseSettingsDto(
            "Server=localhost;Database=NovaFleetDb;User ID=svc;Password=secret;TrustServerCertificate=True", false, string.Empty));

        Assert.True(saved.RestartRequired);
        Assert.Contains("Password=secret", bootstrap.Written!, StringComparison.Ordinal);

        var read = await service.GetDatabaseAsync();
        Assert.DoesNotContain("secret", read.ConnectionString!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connections_DatabaseSaveRejectsUnparsableConnectionString()
    {
        var service = ConnectionsFor(new FakeCurrentUser("demo.sysadmin", PermissionCatalog.Administration.ConnectionsManage));

        await Assert.ThrowsAsync<ValidationAppException>(() => service.UpdateDatabaseAsync(
            new DatabaseSettingsDto("this is not a connection string", false, string.Empty)));
    }

    [Fact]
    public async Task SmsQueue_StaysUntouchedWhileGlobalSwitchIsOff()
    {
        var gateway = new RecordingSmsGateway();
        var item = await QueueMessageAsync();

        Settings.Values[SettingKeys.SmsEnabled] = bool.FalseString;
        var dispatcher = new SmsDispatcher(Db, gateway, Settings, Clock, NullLogger<SmsDispatcher>.Instance);

        Assert.Equal(0, await dispatcher.ProcessQueueAsync(10));
        Assert.Equal(0, gateway.SendCount);
        Assert.Equal(SmsQueueStatus.Queued, ReloadQueueItem(item.Id).Status);

        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        Assert.Equal(1, await dispatcher.ProcessQueueAsync(10));
        Assert.Equal(1, gateway.SendCount);
        Assert.Equal(SmsQueueStatus.Accepted, ReloadQueueItem(item.Id).Status);
    }

    [Fact]
    public async Task TestSms_IsRefusedWhileGlobalSwitchIsOff()
    {
        var service = new SmsService(Db, Settings,
            new FakeCurrentUser("demo.sysadmin", PermissionCatalog.Administration.ConnectionsManage), Audit, Clock);

        Settings.Values[SettingKeys.SmsEnabled] = bool.FalseString;
        await Assert.ThrowsAsync<ValidationAppException>(
            () => service.QueueTestAsync(new SmsTestRequest("09121112233", "آزمون")));

        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        Assert.NotEqual(Guid.Empty, await service.QueueTestAsync(new SmsTestRequest("09121112233", "آزمون")));
    }

    private async Task<SmsQueueItem> QueueMessageAsync()
    {
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن آزمایشی", null,
            "tester", Clock.UtcNow);
        Db.SmsQueueItems.Add(item);
        await Db.SaveChangesAsync();
        return item;
    }

    private SmsQueueItem ReloadQueueItem(Guid id) =>
        Db.SmsQueueItems.AsNoTracking().First(item => item.Id == id);

    private sealed class RecordingSmsGateway : ISmsGateway
    {
        public int SendCount;

        public Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult(SmsSendResult.Success(message.CorrelationId, "sent"));
        }

        public Task<SmsDeliveryResult> GetDeliveryStatusAsync(
            string providerReference,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SmsDeliveryResult(SmsDeliveryState.Unknown, null));

        public Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(SmsBalanceResult.Of("0"));
    }

    private sealed class FakeBootstrapStore : IBootstrapConfigurationStore
    {
        public string? Written { get; private set; }

        public string FilePath => Path.Combine(Path.GetTempPath(), "novafleet-bootstrap-test.json");

        public string? ReadConnectionString() => Written;

        public void WriteConnectionString(string connectionString) => Written = connectionString;
    }
}
