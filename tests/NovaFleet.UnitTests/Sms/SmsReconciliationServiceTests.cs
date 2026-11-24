using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Notifications;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Sms;

/// <summary>تصمیم نتیجه مبهم فقط صریح، مجاز و قابل ممیزی است؛ هرگز بخشی از پردازش خودکار صف نیست.</summary>
public sealed class SmsReconciliationServiceTests : MissionTestBase
{
    private SmsService SmsServiceFor(ICurrentUser user) => new(Db, Settings, user, Audit, Clock);

    private static ICurrentUser Operator() => User(
        RequesterUsername,
        PermissionCatalog.Notifications.SmsView,
        PermissionCatalog.Notifications.SmsManage);

    [Fact]
    public async Task ReconcileAsync_CloseMarksFailedAndPreservesProviderEvidence()
    {
        var item = await SeedIndeterminateAsync();

        await SmsServiceFor(Operator()).ReconcileAsync(item.Id,
            new SmsReconciliationRequest("failed", "بررسی سرویس‌دهنده: پیام تحویل نشده", true));

        var stored = await Db.SmsQueueItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Failed, stored.Status);
        Assert.Equal("700123", stored.ProviderReference);
        Assert.NotNull(stored.SendStartedAtUtc);
        Assert.Null(stored.NextAttemptAtUtc);
        Assert.Contains("بررسی سرویس‌دهنده", stored.LastError, StringComparison.Ordinal);
        var audit = Assert.Single(Audit.Records, record => record.Action == "ReconcileSms");
        Assert.Equal("failed", audit.After is { } value ? value.GetType().GetProperty("Decision")?.GetValue(value) : null);
    }

    [Fact]
    public async Task ReconcileAsync_RetryRequeuesOnlyAfterExplicitConfirmation()
    {
        var item = await SeedIndeterminateAsync();

        await SmsServiceFor(Operator()).ReconcileAsync(item.Id,
            new SmsReconciliationRequest("retry", "تحویل پیام تأیید نشد و ارسال مجدد تایید شد", true));

        var stored = await Db.SmsQueueItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Queued, stored.Status);
        Assert.Equal(0, stored.AttemptCount);
        Assert.Equal(Clock.UtcNow, stored.NextAttemptAtUtc);
        Assert.Equal("700123", stored.ProviderReference);
        Assert.Null(stored.SendStartedAtUtc);
        Assert.True(Audit.Wrote("ReconcileSms"));
    }

    [Fact]
    public async Task ReconcileAsync_WithoutManagePermissionIsForbiddenAndChangesNothing()
    {
        var item = await SeedIndeterminateAsync();
        var viewer = User(RequesterUsername, PermissionCatalog.Notifications.SmsView);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => SmsServiceFor(viewer).ReconcileAsync(item.Id,
            new SmsReconciliationRequest("failed", "بستن", true)));

        Assert.Equal(SmsQueueStatus.Indeterminate, (await Db.SmsQueueItems.AsNoTracking()
            .SingleAsync(row => row.Id == item.Id)).Status);
        Assert.False(Audit.Wrote("ReconcileSms"));
    }

    [Fact]
    public async Task ReconcileAsync_RequiresConfirmationReasonAndIndeterminateStatus()
    {
        var item = await SeedIndeterminateAsync();
        var service = SmsServiceFor(Operator());

        await Assert.ThrowsAsync<ValidationAppException>(() => service.ReconcileAsync(item.Id,
            new SmsReconciliationRequest("retry", "دلیل", false)));
        await Assert.ThrowsAsync<ValidationAppException>(() => service.ReconcileAsync(item.Id,
            new SmsReconciliationRequest("retry", " ", true)));
        await Assert.ThrowsAsync<ValidationAppException>(() => service.ReconcileAsync(item.Id,
            new SmsReconciliationRequest("unknown", "دلیل", true)));

        var accepted = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن", nowUtc: Clock.UtcNow);
        accepted.MarkSending(Clock.UtcNow);
        accepted.MarkAccepted("700123", Clock.UtcNow);
        Db.SmsQueueItems.Add(accepted);
        await Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationAppException>(() => service.ReconcileAsync(accepted.Id,
            new SmsReconciliationRequest("failed", "دلیل", true)));
    }

    [Fact]
    public async Task ListAndSummary_ExposeIndeterminateEvidenceAndCapabilities()
    {
        var item = await SeedIndeterminateAsync();

        var page = await SmsServiceFor(Operator()).ListAsync(new SmsQuery(null, null, null, null, 1, 20));
        var dto = page.Items.Single(row => row.Id == item.Id);
        Assert.True(dto.CanReconcile);
        Assert.False(dto.CanCancel);
        Assert.Equal(SmsGatewayMode.Api, dto.GatewayMode);
        Assert.Equal("700123", dto.ProviderReference);
        Assert.NotNull(dto.SendStartedAtUtc);
        Assert.Null(dto.NextDeliveryPollAtUtc);

        var summary = await SmsServiceFor(Operator()).GetStatusSummaryAsync();
        Assert.Equal(1, summary.Indeterminate);
    }

    private async Task<SmsQueueItem> SeedIndeterminateAsync()
    {
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن", nowUtc: Clock.UtcNow);
        item.MarkSending(Clock.UtcNow, (int)SmsGatewayMode.Api);
        item.MarkIndeterminate("نتیجه نامشخص", "timeout", Clock.UtcNow, "700123");
        Db.SmsQueueItems.Add(item);
        await Db.SaveChangesAsync();
        return item;
    }
}
