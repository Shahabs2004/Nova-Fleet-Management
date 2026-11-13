using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Sessions;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Auditing;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.Infrastructure.Sessions;
using NovaFleet.Infrastructure.Settings;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;
using System.Security.Cryptography;
using System.Text;

namespace NovaFleet.UnitTests.Authorization;

/// <summary>
/// نگهبانان مجوز در لایه سرویس؛ همان مسیری که درخواست احراز هویت‌شده اما کم‌اختیار را به پاسخ 403 می‌رساند.
/// </summary>
public class AuthorizationGuardTests : MissionTestBase
{
    private static FakeCurrentUser Operator() => new("demo.operator", "dashboard.view", "missions.view");

    private static FakeCurrentUser SystemManager() => new("demo.sysadmin", PermissionCatalog.Administration.SystemManage);

    private static FakeCurrentUser LocalAdmin() => new("demo.root", PermissionCatalog.All);

    private SettingsService SettingsFor(ICurrentUser user) => new(Db, user, Audit, Runtime);

    private SmsService SmsFor(ICurrentUser user) => new(Db, Settings, user, Audit, Clock);

    private UserService UsersFor(ICurrentUser user) => new(Db, new StubPasswordHasher(), user, Audit);

    private RoleService RolesFor(ICurrentUser user) => new(Db, user, Audit);

    private SessionService SessionsFor(ICurrentUser user) => new(Db, user, Clock);

    private AuditQueryService AuditFor(ICurrentUser user) => new(Db, user);

    [Fact]
    public async Task SettingsService_AnonymousCallerIsForbidden()
    {
        var service = SettingsFor(AnonymousCurrentUser.Instance);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetBundleAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => service.UpdateMissionRulesAsync(new MissionRulesDto(30, 30, 24, 60)));
    }

    [Fact]
    public async Task SettingsService_OperatorWithoutSettingsPermissionIsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => SettingsFor(Operator()).UpdateMissionRulesAsync(new MissionRulesDto(30, 30, 24, 60)));
    }

    [Fact]
    public async Task SettingsService_RuntimeBundleReadStaysOpenForPrintPath()
    {
        var service = SettingsFor(Operator());

        var bundle = await service.GetRuntimeBundleAsync();

        Assert.False(string.IsNullOrWhiteSpace(bundle.Organization.Name));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetBundleAsync());
    }

    [Fact]
    public async Task SettingsService_SystemManagerPersistsRulesAndAudits()
    {
        var service = SettingsFor(SystemManager());

        var saved = await service.UpdateMissionRulesAsync(new MissionRulesDto(45, 21, 12, 90));

        Assert.Equal(45, saved.MinimumGapMinutes);
        Assert.Equal("45", await service.GetValueAsync(SettingKeys.MinimumGapMinutes));
        Assert.True(Audit.Wrote("UpdateMissionRules"));
    }

    [Fact]
    public async Task SettingsService_SessionPolicyNeedsItsOwnPermission()
    {
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => SettingsFor(SystemManager()).UpdateSessionPolicyAsync(new SessionPolicyDto(600, 40)));

        var sessionsManager = SettingsFor(new FakeCurrentUser(
            "demo.sessions", PermissionCatalog.Administration.SessionsManage));
        await sessionsManager.UpdateSessionPolicyAsync(new SessionPolicyDto(600, 40));

        Assert.Equal("600", await sessionsManager.GetValueAsync(SettingKeys.SessionAbsoluteMinutes));
    }

    [Fact]
    public async Task SettingsService_MissionTemplateNeedsItsOwnPermission()
    {
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => SettingsFor(SystemManager()).UpdateMissionOrderTemplateAsync(
                new MissionOrderTemplateDto("سربرگ", "پاورقی", true)));
    }

    [Fact]
    public async Task SettingsService_SplitReasonWriteNeedsItsOwnPermissionAndAudits()
    {
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => SettingsFor(SystemManager()).CreateSplitReasonAsync("تغییر برنامه"));

        var reasonsManager = SettingsFor(new FakeCurrentUser(
            "demo.reasons", PermissionCatalog.Administration.MissionSplitReasonsManage));
        var created = await reasonsManager.CreateSplitReasonAsync("تغییر برنامه");

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("تغییر برنامه", created.Title);
        Assert.True(Audit.Wrote("CreateSplitReason"));
    }

    [Fact]
    public async Task SmsService_TestSendNeedsConnectionsPermission()
    {
        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => SmsFor(Operator()).QueueTestAsync(new SmsTestRequest("09121112233", "آزمون")));

        var id = await SmsFor(LocalAdmin()).QueueTestAsync(new SmsTestRequest("09121112233", "آزمون"));

        Assert.NotEqual(Guid.Empty, id);
        Assert.True(Audit.Wrote("QueueTestSms"));
    }

    [Fact]
    public async Task SmsService_QueueReadNeedsSmsViewPermission()
    {
        var service = SmsFor(Operator());

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => service.ListAsync(new SmsQuery(null, null, null, null, 1, 20)));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetStatusSummaryAsync());
    }

    [Fact]
    public async Task SmsService_TemplateWriteNeedsTemplatesPermission()
    {
        await SmsFor(LocalAdmin()).UpdateTemplateAsync(
            SmsEventType.MissionApproved,
            new UpdateSmsTemplateRequest("مأموریت شما تأیید شد.", true, ["requester"]));

        Assert.True(Audit.Wrote("UpdateSmsTemplate"));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => SmsFor(Operator()).ListTemplatesAsync());
    }

    [Fact]
    public async Task UserService_UserCreationNeedsUsersManage()
    {
        await Assert.ThrowsAsync<ForbiddenAppException>(() => UsersFor(Operator()).CreateLocalAsync(
            new CreateLocalUserRequest(
                "new.requester", "Passw0rd!", "زهرا", "نوری", null, null, null, false, [])));

        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => UsersFor(AnonymousCurrentUser.Instance).ListAsync(null, false));
    }

    [Fact]
    public async Task UserService_DirectPermissionsNeedAuthorizationManage()
    {
        var usersManager = UsersFor(new FakeCurrentUser(
            "demo.users", PermissionCatalog.Administration.UsersManage));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => usersManager.SetPermissionsAsync(
            Guid.NewGuid(), new SetPermissionsRequest([])));
    }

    [Fact]
    public async Task RoleService_EveryOperationNeedsAuthorizationManage()
    {
        var operatorUser = RolesFor(Operator());

        await Assert.ThrowsAsync<ForbiddenAppException>(() => operatorUser.ListAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => operatorUser.CreateAsync(new SaveRoleRequest("Auditor", "بازرس", null)));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => operatorUser.ListDirectoryMappingsAsync());
    }

    [Fact]
    public async Task SessionService_ForceEndAndListNeedSessionsView()
    {
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => SessionsFor(Operator()).ForceEndAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => SessionsFor(Operator()).ListAsync(new SessionQuery(null, null, null, null, 1, 20)));
    }

    [Fact]
    public async Task AuditQueryService_ListNeedsAuditView()
    {
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => AuditFor(Operator()).ListAsync(new Application.Auditing.AuditQuery(
                null, null, null, null, null, null, 1, 20)));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => AuditFor(Operator()).ListActionsAsync());
    }

    [Fact]
    public async Task SessionService_ValidationPathStaysUnguardedForCookieEvents()
    {
        // رویداد کوکی بدون کاربر جاری اجرا می‌شود؛ این مسیر نباید مجوز بخواهد.
        var service = new SessionService(Db, AnonymousCurrentUser.Instance, Clock);

        Assert.False(await service.ValidateAsync("missing-session-key", 60, 30));
    }

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

        public bool Verify(string passwordHash, string password) => Hash(password) == passwordHash;
    }
}
