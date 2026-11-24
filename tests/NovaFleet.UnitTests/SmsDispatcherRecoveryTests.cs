using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests.Sms;

/// <summary>تمام مسیرهای ناتمام ارسال باید قابل بازیابی و بدون ارسال خودکار دوباره باشند.</summary>
public sealed class SmsDispatcherRecoveryTests : MissionTestBase
{
    private static readonly SmsQueueStatus IndeterminateStatus = (SmsQueueStatus)9;

    [Fact]
    public async Task FinalSaveFailure_KeepsProviderReferenceAndRecoversStaleSendAsIndeterminate()
    {
        var now = Clock.UtcNow;
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن", nowUtc: now);
        Db.SmsQueueItems.Add(item);
        await Db.SaveChangesAsync();
        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        var saveInterceptor = new ThrowingSaveInterceptor();
        var failingOptions = new DbContextOptionsBuilder<NovaFleetDbContext>(Options)
            .AddInterceptors(saveInterceptor)
            .Options;
        await using var failingDb = new NovaFleetDbContext(failingOptions);
        var gateway = new TestGateway(onSend: () => saveInterceptor.FailNext = true);
        var dispatcher = new SmsDispatcher(failingDb, gateway, Settings, Clock,
            NullLogger<SmsDispatcher>.Instance);

        await dispatcher.ProcessQueueAsync(1);

        var stranded = await Db.SmsQueueItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Sending, stranded.Status);
        Assert.Equal("700123", stranded.ProviderReference);

        Clock.NowUtc = now.AddMinutes(11);
        await dispatcher.ProcessQueueAsync(1);

        var recovered = await Db.SmsQueueItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        Assert.Equal(IndeterminateStatus, recovered.Status);
        Assert.Equal("700123", recovered.ProviderReference);
        Assert.Equal(now, recovered.LastAttemptAtUtc);
        Assert.Null(recovered.NextAttemptAtUtc);
        Assert.Equal(1, gateway.SendCount);
    }

    [Fact]
    public async Task LateProviderResult_DoesNotOverwriteAConcurrentCancellationDecision()
    {
        var item = await SeedAsync();
        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        await using var dispatcherDb = new NovaFleetDbContext(Options);
        var gateway = new TestGateway(onSendAsync: async () =>
        {
            await Db.SmsQueueItems.Where(row => row.Id == item.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, SmsQueueStatus.Cancelled)
                .SetProperty(row => row.LastError, "design decision won")
                .SetProperty(row => row.NextAttemptAtUtc, (DateTimeOffset?)null)
                .SetProperty(row => row.UpdatedAtUtc, Clock.UtcNow));
        });
        var dispatcher = new SmsDispatcher(dispatcherDb, gateway, Settings, Clock,
            NullLogger<SmsDispatcher>.Instance);

        await dispatcher.ProcessQueueAsync(1);

        var stored = await Db.SmsQueueItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Cancelled, stored.Status);
        Assert.Equal("design decision won", stored.LastError);
        Assert.Equal("700123", stored.ProviderReference);
    }

    [Fact]
    public async Task LateProviderResult_DoesNotCompleteAReconciledRetryAttempt()
    {
        var item = await SeedAsync();
        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        var firstAttemptStartedAt = Clock.UtcNow;
        await using var dispatcherDb = new NovaFleetDbContext(Options);
        var gateway = new TestGateway(onSendAsync: async () =>
        {
            await using var retryDb = new NovaFleetDbContext(Options);
            var current = await retryDb.SmsQueueItems.SingleAsync(row => row.Id == item.Id);
            current.MarkIndeterminate("ارسال اولیه رها شد.", "stale", Clock.UtcNow);
            await retryDb.SaveChangesAsync();
            current.ReconcileForRetry("بررسی اپراتور و تأیید ارسال مجدد", Clock.UtcNow);
            await retryDb.SaveChangesAsync();
            Clock.NowUtc = Clock.UtcNow.AddMinutes(1);
            current.MarkSending(Clock.NowUtc);
            await retryDb.SaveChangesAsync();
        });
        var dispatcher = new SmsDispatcher(dispatcherDb, gateway, Settings, Clock,
            NullLogger<SmsDispatcher>.Instance);

        await dispatcher.ProcessQueueAsync(1);

        var stored = await Db.SmsQueueItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Sending, stored.Status);
        Assert.Equal(firstAttemptStartedAt.AddMinutes(1), stored.SendStartedAtUtc);
        Assert.Null(stored.ProviderReference);
        Assert.Equal(1, gateway.SendCount);
    }

    [Fact]
    public async Task AcceptedWithoutUsableProviderReference_BecomesIndeterminateWithoutRetry()
    {
        var item = await SeedAsync();
        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        var gateway = new TestGateway(result: SmsSendResult.Success(" ", null));
        var dispatcher = new SmsDispatcher(Db, gateway, Settings, Clock,
            NullLogger<SmsDispatcher>.Instance);

        await dispatcher.ProcessQueueAsync(1);

        var stored = await Db.SmsQueueItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        Assert.Equal(IndeterminateStatus, stored.Status);
        Assert.Null(stored.NextAttemptAtUtc);
        Assert.Contains("شناسه", stored.LastError, StringComparison.Ordinal);
        Assert.Equal(1, gateway.SendCount);
    }

    [Fact]
    public async Task PollDelivery_UsesStoredGatewayModeInsteadOfCurrentSettings()
    {
        var item = await SeedAcceptedAsync((int)SmsGatewayMode.Api);
        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        Settings.Values[SettingKeys.SmsMode] = "Simulation";
        Settings.Values[SettingKeys.SmsMaxPollingAttempts] = "5";
        var gateway = new ModeRecordingGateway();
        var dispatcher = new SmsDispatcher(Db, gateway, Settings, Clock,
            NullLogger<SmsDispatcher>.Instance);
        Clock.NowUtc = Clock.UtcNow.AddSeconds(10);

        Assert.Equal(1, await dispatcher.PollDeliveryAsync(1));

        Assert.Equal(SmsGatewayMode.Api, gateway.SelectedMode);
        Assert.Equal(1, gateway.PollCount);
        Assert.Equal(SmsQueueStatus.Accepted, (await Db.SmsQueueItems.AsNoTracking()
            .SingleAsync(row => row.Id == item.Id)).Status);
    }

    [Fact]
    public async Task PollDelivery_WaitsForScheduleAndAppliesBoundedBackoff()
    {
        var item = await SeedAcceptedAsync((int)SmsGatewayMode.Simulation);
        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        Settings.Values[SettingKeys.SmsMaxPollingAttempts] = "10";
        var gateway = new TestGateway();
        var dispatcher = new SmsDispatcher(Db, gateway, Settings, Clock,
            NullLogger<SmsDispatcher>.Instance);
        var start = Clock.UtcNow;

        Assert.Equal(0, await dispatcher.PollDeliveryAsync(1));
        Clock.NowUtc = start.AddSeconds(10);
        Assert.Equal(1, await dispatcher.PollDeliveryAsync(1));
        Assert.Equal(start.AddSeconds(20), (await Db.SmsQueueItems.AsNoTracking()
            .SingleAsync(row => row.Id == item.Id)).NextDeliveryPollAtUtc);
        Assert.Equal(0, await dispatcher.PollDeliveryAsync(1));

        Clock.NowUtc = start.AddSeconds(20);
        Assert.Equal(1, await dispatcher.PollDeliveryAsync(1));
        Assert.Equal(start.AddSeconds(50), (await Db.SmsQueueItems.AsNoTracking()
            .SingleAsync(row => row.Id == item.Id)).NextDeliveryPollAtUtc);

        Clock.NowUtc = start.AddSeconds(50);
        Assert.Equal(1, await dispatcher.PollDeliveryAsync(1));
        Assert.Equal(start.AddSeconds(110), (await Db.SmsQueueItems.AsNoTracking()
            .SingleAsync(row => row.Id == item.Id)).NextDeliveryPollAtUtc);

        Clock.NowUtc = start.AddSeconds(110);
        Assert.Equal(1, await dispatcher.PollDeliveryAsync(1));
        Assert.Equal(start.AddSeconds(410), (await Db.SmsQueueItems.AsNoTracking()
            .SingleAsync(row => row.Id == item.Id)).NextDeliveryPollAtUtc);

        Clock.NowUtc = start.AddSeconds(410);
        Assert.Equal(1, await dispatcher.PollDeliveryAsync(1));
        Assert.Equal(start.AddSeconds(1010), (await Db.SmsQueueItems.AsNoTracking()
            .SingleAsync(row => row.Id == item.Id)).NextDeliveryPollAtUtc);
    }

    [Fact]
    public async Task PollDelivery_CapsExponentialBackoffAtTwentyFourHours()
    {
        var item = await SeedAcceptedAsync((int)SmsGatewayMode.Simulation);
        Settings.Values[SettingKeys.SmsEnabled] = bool.TrueString;
        Settings.Values[SettingKeys.SmsMaxPollingAttempts] = "100";
        await Db.SmsQueueItems
            .Where(row => row.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.DeliveryPollCount, 20)
                .SetProperty(row => row.NextDeliveryPollAtUtc, Clock.UtcNow));
        var dispatcher = new SmsDispatcher(Db, new TestGateway(), Settings, Clock,
            NullLogger<SmsDispatcher>.Instance);
        var start = Clock.UtcNow;

        Assert.Equal(1, await dispatcher.PollDeliveryAsync(1));

        Assert.Equal(start.AddHours(24), (await Db.SmsQueueItems.AsNoTracking()
            .SingleAsync(row => row.Id == item.Id)).NextDeliveryPollAtUtc);
    }

    [Fact]
    public async Task CancelStartReminderAsync_DoesNotCancelAnInFlightSend()
    {
        var missionId = Guid.NewGuid();
        var item = new SmsQueueItem(SmsEventType.MissionStartReminder, "09121112233", "آزمون", "یادآوری",
            missionId, "system", Clock.UtcNow);
        item.MarkSending(Clock.UtcNow);
        Db.SmsQueueItems.Add(item);
        await Db.SaveChangesAsync();
        var dispatcher = new SmsDispatcher(Db, new TestGateway(), Settings, Clock,
            NullLogger<SmsDispatcher>.Instance);

        await dispatcher.CancelStartReminderAsync(missionId);

        var stored = await Db.SmsQueueItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        Assert.Equal(SmsQueueStatus.Sending, stored.Status);
    }

    private async Task<SmsQueueItem> SeedAsync()
    {
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن", nowUtc: Clock.UtcNow);
        Db.SmsQueueItems.Add(item);
        await Db.SaveChangesAsync();
        return item;
    }

    private async Task<SmsQueueItem> SeedAcceptedAsync(int gatewayMode)
    {
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن", nowUtc: Clock.UtcNow);
        item.MarkSending(Clock.UtcNow, gatewayMode);
        item.MarkAccepted("700123", Clock.UtcNow);
        Db.SmsQueueItems.Add(item);
        await Db.SaveChangesAsync();
        return item;
    }

    private sealed class ModeRecordingGateway : ISmsGateway, ISmsGatewaySelector
    {
        public SmsGatewayMode? SelectedMode { get; private set; }
        public int PollCount { get; private set; }

        public ISmsGateway ForMode(SmsGatewayMode mode)
        {
            SelectedMode = mode;
            return this;
        }

        public Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(SmsSendResult.Success("700123", null));

        public Task<SmsDeliveryResult> GetDeliveryStatusAsync(string providerReference, CancellationToken cancellationToken = default)
        {
            PollCount++;
            return Task.FromResult(new SmsDeliveryResult(SmsDeliveryState.Unknown, "Checked"));
        }

        public Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SmsBalanceResult.Of("0"));
    }

    private sealed class TestGateway(
        Action? onSend = null,
        SmsSendResult? result = null,
        Func<Task>? onSendAsync = null) : ISmsGateway
    {
        public int SendCount { get; private set; }

        public async Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
        {
            SendCount++;
            onSend?.Invoke();
            if (onSendAsync is not null)
            {
                await onSendAsync();
            }

            return result ?? SmsSendResult.Success("700123", "TestAccepted");
        }

        public Task<SmsDeliveryResult> GetDeliveryStatusAsync(string providerReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SmsDeliveryResult(SmsDeliveryState.Unknown, "TestUnknown"));

        public Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SmsBalanceResult.Of("0"));
    }

    private sealed class ThrowingSaveInterceptor : SaveChangesInterceptor
    {
        public bool FailNext { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new DbUpdateException("forced final-save failure");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
