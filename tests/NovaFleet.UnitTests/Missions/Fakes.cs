using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Notifications;

namespace NovaFleet.UnitTests.Missions;

/// <summary>
/// هویت ساختگی با مجموعه مجوز واقعی سامانه؛ Require/RequireAny از خود رابط پیاده‌سازی می‌شوند
/// تا آزمون‌ها رفتار 403 تولیدی را بسنجند، نه یک جای‌نویسی دستی.
/// </summary>
public sealed class FakeCurrentUser(
    string username,
    string displayName,
    EffectivePermissionSet permissions) : ICurrentUser
{
    public FakeCurrentUser(string username, params string[] grantedPermissions)
        : this(username, username, EffectivePermissionSet.Build(
            grantedPermissions.Select(permission => (permission, PermissionEffect.Grant))))
    {
    }

    public bool IsAuthenticated => true;

    public string Username { get; } = username;

    public string DisplayName { get; } = displayName;

    public bool IsLocalAdmin { get; init; }

    public EffectivePermissionSet Permissions { get; } = permissions;
}

public sealed class FakeClock(DateTimeOffset nowUtc) : IClock
{
    public DateTimeOffset NowUtc { get; set; } = nowUtc;

    public DateTimeOffset UtcNow => NowUtc;
}

public sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Records { get; } = [];

    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }

    public bool Wrote(string action) => Records.Any(record => record.Action == action);
}

public sealed record DeliveredNotification(NotificationMessage Message, IReadOnlyList<string> Targets);

public sealed class FakeNotificationPublisher : INotificationPublisher
{
    public List<DeliveredNotification> Delivered { get; } = [];

    public Task PublishAsync(
        NotificationMessage message,
        IEnumerable<string> usernames,
        CancellationToken cancellationToken = default)
    {
        Delivered.Add(new DeliveredNotification(message, usernames.ToList()));
        return Task.CompletedTask;
    }

    public Task PublishToPermissionAsync(
        NotificationMessage message,
        string permission,
        CancellationToken cancellationToken = default)
    {
        Delivered.Add(new DeliveredNotification(message, [$"role:{permission}"]));
        return Task.CompletedTask;
    }

    public Task PublishToRolesAsync(
        NotificationMessage message,
        IReadOnlyList<string> roleNames,
        CancellationToken cancellationToken = default)
    {
        Delivered.Add(new DeliveredNotification(message, roleNames.Select(role => $"role:{role}").ToList()));
        return Task.CompletedTask;
    }

    public string? LastEventKeyFor(string username) => Delivered
        .Where(item => item.Targets.Contains(username))
        .Select(item => item.Message.EventKey)
        .LastOrDefault();
}

public sealed record QueuedSms(Guid MissionId, SmsEventType EventType, string? Reason);

public sealed class FakeSmsDispatcher : ISmsDispatcher
{
    public List<QueuedSms> Queued { get; } = [];

    public List<Guid> CancelledReminders { get; } = [];

    public List<Guid> RescheduledReminders { get; } = [];

    public Task QueueForMissionAsync(
        Guid missionId,
        SmsEventType eventType,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        Queued.Add(new QueuedSms(missionId, eventType, reason));
        return Task.CompletedTask;
    }

    public Task RescheduleStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        RescheduledReminders.Add(missionId);
        return Task.CompletedTask;
    }

    public Task CancelStartReminderAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        CancelledReminders.Add(missionId);
        return Task.CompletedTask;
    }

    public Task<int> ProcessQueueAsync(int batchSize, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<int> PollDeliveryAsync(int batchSize, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public bool QueuedFor(Guid missionId, SmsEventType eventType)
        => Queued.Any(item => item.MissionId == missionId && item.EventType == eventType);
}

/// <summary>
/// تنظیمات کلید/مقدار؛ مقدار null یعنی «تنظیم نشده» تا سرویس به مقدار پیش‌فرض خود برگردد.
/// </summary>
public sealed class FakeSettingsService : ISettingsService
{
    public Dictionary<string, string?> Values { get; } = [];

    public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

    public Task<SettingsBundleDto> GetBundleAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new SettingsBundleDto(
            new OrganizationProfileDto("ناوگان نوین", null),
            new MissionRulesDto(
                Parse(SettingKeys.MinimumGapMinutes, SettingKeys.DefaultMinimumGapMinutes),
                Parse(SettingKeys.FutureRequestDays, SettingKeys.DefaultFutureRequestDays),
                24,
                30),
            new SessionPolicyDto(480, 60),
            new MissionOrderTemplateDto(null, null, false)));

    public Task<OrganizationProfileDto> UpdateOrganizationAsync(
        OrganizationProfileDto profile,
        CancellationToken cancellationToken = default) => Task.FromResult(profile);

    public Task<MissionRulesDto> UpdateMissionRulesAsync(
        MissionRulesDto rules,
        CancellationToken cancellationToken = default) => Task.FromResult(rules);

    public Task<SessionPolicyDto> UpdateSessionPolicyAsync(
        SessionPolicyDto policy,
        CancellationToken cancellationToken = default) => Task.FromResult(policy);

    public Task<MissionOrderTemplateDto> UpdateMissionOrderTemplateAsync(
        MissionOrderTemplateDto template,
        CancellationToken cancellationToken = default) => Task.FromResult(template);

    public Task<IReadOnlyList<SplitReasonDto>> ListSplitReasonsAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SplitReasonDto>>([]);

    public Task<SplitReasonDto> CreateSplitReasonAsync(string title, CancellationToken cancellationToken = default)
        => Task.FromResult(new SplitReasonDto(Guid.NewGuid(), title, 0, true));

    public Task<SplitReasonDto> UpdateSplitReasonAsync(
        Guid id,
        UpdateSplitReasonRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SplitReasonDto(id, request.Title, request.SortOrder, request.IsActive));

    private int Parse(string key, int fallback) =>
        int.TryParse(Values.GetValueOrDefault(key), out var value) ? value : fallback;
}
