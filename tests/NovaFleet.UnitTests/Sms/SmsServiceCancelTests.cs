using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Notifications;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Sms;

/// <summary>لغو دستی پیامک ارسال‌نشده یا ناموفق؛ پیام پذیرفته/تحویل‌شده دیگر لغو نمی‌شود.</summary>
public sealed class SmsServiceCancelTests : MissionTestBase
{
    private SmsService SmsServiceFor(ICurrentUser user) => new(Db, Settings, user, Audit, Clock);

    private static ICurrentUser Operator() =>
        User(RequesterUsername, PermissionCatalog.Notifications.SmsCancel, PermissionCatalog.Notifications.SmsView);

    private async Task<SmsQueueItem> SeedAsync(Action<SmsQueueItem>? shape = null)
    {
        var item = new SmsQueueItem(SmsEventType.MissionApproved, "09121112233", "گیرنده", "متن پیامک", null, "system", Clock.UtcNow);
        shape?.Invoke(item);
        Db.SmsQueueItems.Add(item);
        await Db.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task CancelAsync_QueuedItem_MarksCancelled()
    {
        var item = await SeedAsync();

        await SmsServiceFor(Operator()).CancelAsync(item.Id);

        var stored = await Db.SmsQueueItems.AsNoTracking().FirstAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Cancelled, stored.Status);
        Assert.Null(stored.NextAttemptAtUtc);
    }

    [Fact]
    public async Task CancelAsync_RetryScheduledItem_MarksCancelled()
    {
        var now = Clock.UtcNow;
        var item = await SeedAsync(row =>
        {
            row.MarkSending(now);
            row.ScheduleRetry("خطای موقت", now.AddMinutes(5), now);
        });

        await SmsServiceFor(Operator()).CancelAsync(item.Id);

        var stored = await Db.SmsQueueItems.AsNoTracking().FirstAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Cancelled, stored.Status);
    }

    [Fact]
    public async Task CancelAsync_AcceptedItem_IsRejected()
    {
        var now = Clock.UtcNow;
        var item = await SeedAsync(row =>
        {
            row.MarkSending(now);
            row.MarkAccepted("700123", now);
        });

        await Assert.ThrowsAsync<ValidationAppException>(() => SmsServiceFor(Operator()).CancelAsync(item.Id));
    }

    [Fact]
    public async Task CancelAsync_FailedItem_MarksCancelled()
    {
        var now = Clock.UtcNow;
        var item = await SeedAsync(row =>
        {
            row.MarkSending(now);
            row.MarkFailed("خطای موقت", now);
        });

        await SmsServiceFor(Operator()).CancelAsync(item.Id);

        var stored = await Db.SmsQueueItems.AsNoTracking().FirstAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Cancelled, stored.Status);
        Assert.Null(stored.NextAttemptAtUtc);
    }

    [Fact]
    public async Task CancelAsync_PermanentFailureItem_MarksCancelled()
    {
        var now = Clock.UtcNow;
        var item = await SeedAsync(row =>
        {
            row.MarkSending(now);
            row.MarkPermanentFailure("شماره نامعتبر", now);
        });

        await SmsServiceFor(Operator()).CancelAsync(item.Id);

        var stored = await Db.SmsQueueItems.AsNoTracking().FirstAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Cancelled, stored.Status);
    }

    [Fact]
    public async Task CancelAsync_DeliveredItem_IsRejected()
    {
        var now = Clock.UtcNow;
        var item = await SeedAsync(row =>
        {
            row.MarkSending(now);
            row.MarkAccepted("700123", now);
            row.MarkDelivered(now);
        });

        await Assert.ThrowsAsync<ValidationAppException>(() => SmsServiceFor(Operator()).CancelAsync(item.Id));
    }

    [Fact]
    public async Task CancelAsync_WithoutPermission_IsForbidden()
    {
        var item = await SeedAsync();
        var noPermission = User(RequesterUsername, PermissionCatalog.Notifications.SmsView);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => SmsServiceFor(noPermission).CancelAsync(item.Id));
    }

    [Fact]
    public async Task CancelAsync_MissingItem_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundAppException>(() => SmsServiceFor(Operator()).CancelAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_FlagsCanCancelForUnsentAndFailedItems()
    {
        var now = Clock.UtcNow;
        var queued = await SeedAsync();
        var failed = await SeedAsync(row =>
        {
            row.MarkSending(now);
            row.MarkFailed("خطای موقت", now);
        });
        var accepted = await SeedAsync(row =>
        {
            row.MarkSending(now);
            row.MarkAccepted("700123", now);
        });

        var page = await SmsServiceFor(Operator()).ListAsync(new SmsQuery(null, null, null, null, 1, 20));

        Assert.True(page.Items.Single(row => row.Id == queued.Id).CanCancel);
        Assert.True(page.Items.Single(row => row.Id == failed.Id).CanCancel);
        Assert.False(page.Items.Single(row => row.Id == accepted.Id).CanCancel);
    }
}