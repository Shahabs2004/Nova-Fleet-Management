using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Sms;

namespace NovaFleet.IntegrationTests;

/// <summary>
/// آزمون واقعی رقابت دو dispatcher روی SQL Server LocalDB. این آزمون هرگز به پایگاه داده تولیدی
/// وصل نمی‌شود؛ رشته اتصال باید در همین فرایند و فقط برای کاتالوگ اختصاصی Stage 5 باشد.
/// </summary>
public sealed class SmsQueueClaimIntegrationTests : IAsyncLifetime
{
    private const string DataSource = @"(localdb)\MSSQLLocalDB";
    private const string Catalog = "NovaFleet_SmsQueue_Test_20261124";
    private const string EnvironmentVariable = "NOVAFLEET_SMS_TEST_SQL";
    private static readonly SmsQueueStatus IndeterminateStatus = (SmsQueueStatus)9;

    private string connectionString = null!;
    private DbContextOptions<NovaFleetDbContext> options = null!;
    private NovaFleetDbContext db = null!;
    private TestClock clock = null!;
    private TestSettings settings = null!;

    public async Task InitializeAsync()
    {
        connectionString = ReadGuardedConnectionString();
        await RecreateCatalogAsync();
        options = new DbContextOptionsBuilder<NovaFleetDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        db = new NovaFleetDbContext(options);
        await db.Database.MigrateAsync();
        clock = new TestClock(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));
        settings = new TestSettings
        {
            Values =
            {
                [SettingKeys.SmsEnabled] = bool.TrueString,
                [SettingKeys.SmsRetryMinutes] = "5",
                [SettingKeys.SmsMode] = "Simulation",
            }
        };
    }

    public async Task DisposeAsync()
    {
        if (db is not null)
        {
            await db.DisposeAsync();
        }

        SqlConnection.ClearAllPools();
        await DropCatalogAsync();
    }

    [Fact]
    public async Task TwoDispatchers_ClaimOneDueItemExactlyOnce()
    {
        var item = await SeedAsync();
        var calls = 0;
        using var firstDb = CreateContext();
        using var secondDb = CreateContext();
        settings.ClaimBarrier = new Barrier(2);
        var first = new SmsDispatcher(firstDb, new RecordingGateway(() => Interlocked.Increment(ref calls)),
            settings, clock, NullLogger<SmsDispatcher>.Instance);
        var second = new SmsDispatcher(secondDb, new RecordingGateway(() => Interlocked.Increment(ref calls)),
            settings, clock, NullLogger<SmsDispatcher>.Instance);

        await Task.WhenAll(first.ProcessQueueAsync(1), second.ProcessQueueAsync(1));

        Assert.Equal(1, calls);
        var stored = await ReadStoredAsync(item.Id);
        Assert.Equal(SmsQueueStatus.Accepted, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal("700123", stored.ProviderReference);
        Assert.Equal((int)SmsGatewayMode.Simulation, stored.GatewayMode);
        Assert.NotNull(stored.SendStartedAtUtc);
    }

    [Fact]
    public async Task Upgrade_ExistingAcceptedWithoutStoredModeBecomesIndeterminateInsteadOfBeingPolledAsSimulation()
    {
        await db.Database.MigrateAsync("20261123201500_AddLoginLockout");
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [SmsQueueItems]
                ([Id], [EventType], [EventKey], [RecipientMobile], [RecipientName], [MessageBody],
                 [Status], [AttemptCount], [MaxAttempts], [ProviderReference], [AcceptedAtUtc],
                 [LastAttemptAtUtc], [DeliveryPollCount], [UpdatedAtUtc], [CreatedAtUtc])
            VALUES
                ({id}, {(int)SmsEventType.ManualTest}, {SmsEventNames.Of(SmsEventType.ManualTest)},
                 {"09121112233"}, {"tester"}, {"legacy accepted message"},
                 {(int)SmsQueueStatus.Accepted}, {1}, {5}, {"700123"}, {clock.NowUtc},
                 {clock.NowUtc}, {0}, {clock.NowUtc}, {clock.NowUtc});
            """);

        await db.Database.MigrateAsync();

        var stored = await ReadStoredAsync(id);
        Assert.Equal(IndeterminateStatus, stored.Status);
        Assert.Equal("700123", stored.ProviderReference);
        Assert.Equal(clock.NowUtc, stored.AcceptedAtUtc);
        Assert.Null(stored.SendStartedAtUtc);
        Assert.Null(stored.NextDeliveryPollAtUtc);
        Assert.Contains("حالت درگاه", stored.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Downgrade_RemapsIndeterminateToUnderstoodFailureWithSafeAuditExplanation()
    {
        var now = clock.NowUtc;
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن", nowUtc: now);
        item.MarkSending(now);
        item.MarkIndeterminate("raw-provider-secret", "raw-status", now, "700123");
        db.SmsQueueItems.Add(item);
        await db.SaveChangesAsync();

        try
        {
            await db.Database.MigrateAsync("20261123201500_AddLoginLockout");
            await using var connection = new SqlConnection(connectionString);
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var status = await ScalarAsync<int>(connection,
                "SELECT [Status] FROM [SmsQueueItems] WHERE [Id] = @id", new SqlParameter("@id", item.Id));
            var lastError = await ScalarAsync<string>(connection,
                "SELECT [LastError] FROM [SmsQueueItems] WHERE [Id] = @id", new SqlParameter("@id", item.Id));
            var unknownStatuses = await ScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM [SmsQueueItems] WHERE [Status] NOT IN (1, 2, 3, 4, 5, 6, 7, 8)");

            Assert.Equal((int)SmsQueueStatus.Failed, status);
            Assert.Equal(0, unknownStatuses);
            Assert.Contains("نامشخص", lastError, StringComparison.Ordinal);
            Assert.Contains("اپراتور", lastError, StringComparison.Ordinal);
            Assert.DoesNotContain("raw-provider-secret", lastError, StringComparison.Ordinal);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.MigrateAsync();
        }
    }

    [Fact]
    public async Task ReconciliationRetry_StartsFreshPollingBudgetAndCanDeliver()
    {
        var now = clock.NowUtc;
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن", nowUtc: now);
        item.MarkSending(now, (int)SmsGatewayMode.Simulation);
        item.MarkAccepted("700-old", now);
        item.RecordDeliveryPoll(now, now.AddSeconds(10));
        db.SmsQueueItems.Add(item);
        await db.SaveChangesAsync();
        await db.SmsQueueItems
            .Where(row => row.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, IndeterminateStatus)
                .SetProperty(row => row.NextDeliveryPollAtUtc, (DateTimeOffset?)null)
                .SetProperty(row => row.LastError, "نتیجه اولیه نامشخص است."));
        db.ChangeTracker.Clear();
        var retry = await db.SmsQueueItems.SingleAsync(row => row.Id == item.Id);
        retry.ReconcileForRetry("اپراتور ارسال مجدد را تأیید کرد.", now);
        await db.SaveChangesAsync();
        settings.Values[SettingKeys.SmsMaxPollingAttempts] = "1";
        using var dispatcherDb = CreateContext();
        var gateway = new RecordingGateway(() => { },
            deliveryResult: new SmsDeliveryResult(SmsDeliveryState.Delivered, "Delivered"));
        var dispatcher = new SmsDispatcher(dispatcherDb, gateway, settings, clock,
            NullLogger<SmsDispatcher>.Instance);

        Assert.Equal(1, await dispatcher.ProcessQueueAsync(1));
        var accepted = await ReadStoredAsync(item.Id);
        Assert.Equal(SmsQueueStatus.Accepted, accepted.Status);
        Assert.Equal("700123", accepted.ProviderReference);
        Assert.Equal(now, accepted.AcceptedAtUtc);

        clock.NowUtc = now.AddSeconds(10);
        Assert.Equal(1, await dispatcher.PollDeliveryAsync(1));

        var delivered = await ReadStoredAsync(item.Id);
        Assert.Equal(SmsQueueStatus.Delivered, delivered.Status);
        Assert.Equal(1, delivered.DeliveryPollCount);
        Assert.Equal(now.AddSeconds(10), delivered.DeliveredAtUtc);
    }

    [Fact]
    public async Task ProviderTimeoutAfterAcceptance_RecordsIndeterminateAndNeverAutomaticallyResends()
    {
        var item = await SeedAsync();
        var calls = 0;
        var gateway = new RecordingGateway(() => Interlocked.Increment(ref calls),
            () => throw new TaskCanceledException("provider timeout after acceptance"));
        using var dispatcherDb = CreateContext();
        var dispatcher = new SmsDispatcher(dispatcherDb, gateway, settings, clock,
            NullLogger<SmsDispatcher>.Instance);

        await dispatcher.ProcessQueueAsync(1);

        var stored = await ReadStoredAsync(item.Id);
        Assert.Equal(IndeterminateStatus, stored.Status);
        Assert.Null(stored.NextAttemptAtUtc);
        Assert.Equal(1, stored.AttemptCount);

        clock.NowUtc = clock.NowUtc.AddDays(1);
        await dispatcher.ProcessQueueAsync(10);
        await dispatcher.PollDeliveryAsync(10);

        Assert.Equal(1, calls);
        Assert.Equal(IndeterminateStatus, (await ReadStoredAsync(item.Id)).Status);
    }

    [Fact]
    public async Task StaleSending_RecoversToIndeterminateWithoutResendingOrLosingProviderReference()
    {
        var item = await SeedAsync(row => row.MarkSending(clock.NowUtc));
        await db.SmsQueueItems
            .Where(row => row.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.ProviderReference, "700999")
                .SetProperty(row => row.UpdatedAtUtc, clock.NowUtc));
        var lastAttempt = (await ReadStoredAsync(item.Id)).LastAttemptAtUtc;
        var calls = 0;
        using var dispatcherDb = CreateContext();
        var dispatcher = new SmsDispatcher(dispatcherDb,
            new RecordingGateway(() => Interlocked.Increment(ref calls)), settings, clock,
            NullLogger<SmsDispatcher>.Instance);
        clock.NowUtc = clock.NowUtc.AddMinutes(11);

        await dispatcher.ProcessQueueAsync(10);

        var stored = await ReadStoredAsync(item.Id);
        Assert.Equal(IndeterminateStatus, stored.Status);
        Assert.Equal("700999", stored.ProviderReference);
        Assert.Equal(lastAttempt, stored.SendStartedAtUtc);
        Assert.Equal(lastAttempt, stored.LastAttemptAtUtc);
        Assert.Null(stored.NextAttemptAtUtc);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ProviderCall_ObservesAnAlreadyCommittedClaim()
    {
        var item = await SeedAsync();
        SmsQueueStatus? observedStatus = null;
        using var dispatcherDb = CreateContext();
        var gateway = new RecordingGateway(() => { }, async () =>
        {
            using var observer = CreateContext();
            observedStatus = (await observer.SmsQueueItems.AsNoTracking()
                .SingleAsync(row => row.Id == item.Id)).Status;
        });
        var dispatcher = new SmsDispatcher(dispatcherDb, gateway, settings, clock,
            NullLogger<SmsDispatcher>.Instance);

        await dispatcher.ProcessQueueAsync(1);

        Assert.Equal(SmsQueueStatus.Sending, observedStatus);
    }

    [Fact]
    public async Task BlockedFirstSend_DoesNotExposeALaterUnstartedItemToStaleRecovery()
    {
        var firstItem = await SeedAsync();
        var secondItem = await SeedAsync(row => row.ScheduleAt(clock.NowUtc.AddSeconds(1)));
        clock.NowUtc = clock.NowUtc.AddSeconds(1);
        var originalStart = clock.NowUtc;
        var firstProviderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SmsQueueStatus? secondObservedStatus = null;
        DateTimeOffset? secondObservedSendStartedAt = null;
        var recoveredCount = 0;
        using var dispatcherDb = CreateContext();
        var gateway = new RecordingGateway(() => { }, beforeResultWithMessage: async message =>
        {
            if (message.CorrelationId == firstItem.Id.ToString())
            {
                firstProviderEntered.SetResult();
                await releaseFirstProvider.Task;
                return;
            }

            Assert.Equal(secondItem.Id.ToString(), message.CorrelationId);
            using var observer = CreateContext();
            var observed = await observer.SmsQueueItems.AsNoTracking()
                .SingleAsync(row => row.Id == secondItem.Id);
            secondObservedStatus = observed.Status;
            secondObservedSendStartedAt = observed.SendStartedAtUtc;
        });
        var dispatcher = new SmsDispatcher(dispatcherDb, gateway, settings, clock,
            NullLogger<SmsDispatcher>.Instance);
        var processing = dispatcher.ProcessQueueAsync(2);

        try
        {
            await firstProviderEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var recoveryDb = CreateContext();
            var recoveryDispatcher = new SmsDispatcher(recoveryDb, new RecordingGateway(() => { }),
                settings, clock, NullLogger<SmsDispatcher>.Instance);
            clock.NowUtc = originalStart.AddMinutes(11);

            recoveredCount = await recoveryDispatcher.RecoverStaleSendsAsync(10);
        }
        finally
        {
            releaseFirstProvider.TrySetResult();
        }

        var processed = await processing.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(SmsQueueStatus.Sending, secondObservedStatus);
        Assert.Equal(originalStart.AddMinutes(11), secondObservedSendStartedAt);
        Assert.Equal(2, processed);
        Assert.Equal(1, recoveredCount);
        Assert.Equal(IndeterminateStatus, (await ReadStoredAsync(firstItem.Id)).Status);
        var secondStored = await ReadStoredAsync(secondItem.Id);
        Assert.Equal(SmsQueueStatus.Accepted, secondStored.Status);
        Assert.Equal(originalStart.AddMinutes(11), secondStored.SendStartedAtUtc);
    }

    [Fact]
    public async Task SendingItem_CancellationIsRejected()
    {
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن", nowUtc: clock.NowUtc);
        item.MarkSending(clock.NowUtc);

        Assert.False(item.CanCancel);
        Assert.Throws<InvalidOperationException>(() => item.Cancel("لغو", clock.NowUtc));
    }

    private async Task<SmsQueueItem> SeedAsync(Action<SmsQueueItem>? shape = null)
    {
        var item = new SmsQueueItem(SmsEventType.ManualTest, "09121112233", "آزمون", "متن آزمایشی",
            null, "tester", clock.NowUtc);
        shape?.Invoke(item);
        db.SmsQueueItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private NovaFleetDbContext CreateContext()
    {
        // هر اتصال عملیاتی دوباره از همان guard اجباری می‌گذرد.
        _ = ReadGuardedConnectionString(connectionString);
        return new NovaFleetDbContext(options);
    }

    private async Task<SmsQueueItem> ReadStoredAsync(Guid id)
    {
        using var context = CreateContext();
        return await context.SmsQueueItems.AsNoTracking().SingleAsync(row => row.Id == id);
    }

    private static string ReadGuardedConnectionString(string? candidate = null)
    {
        var value = candidate ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Set {EnvironmentVariable} for this test process to the dedicated LocalDB SMS test database.");
        }

        var builder = new SqlConnectionStringBuilder(value);
        if (!string.Equals(builder.DataSource, DataSource, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SMS SQL tests only allow (localdb)\\MSSQLLocalDB.");
        }

        if (!string.Equals(builder.InitialCatalog, Catalog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SMS SQL tests only allow the dedicated catalog {Catalog}.");
        }

        if (!builder.IntegratedSecurity || !string.IsNullOrWhiteSpace(builder.UserID)
            || !string.IsNullOrWhiteSpace(builder.Password) || !string.IsNullOrWhiteSpace(builder.AttachDBFilename))
        {
            throw new InvalidOperationException("SMS SQL tests require integrated Windows authentication without credentials or attached files.");
        }

        return builder.ConnectionString;
    }

    private static async Task RecreateCatalogAsync()
    {
        var builder = new SqlConnectionStringBuilder(ReadGuardedConnectionString())
        {
            InitialCatalog = "master"
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var exists = await ScalarAsync<int?>(connection,
            "SELECT DB_ID(@name)", new SqlParameter("@name", Catalog));
        if (exists is not null)
        {
            await ExecuteAsync(connection,
                $"ALTER DATABASE [{Catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Catalog}];");
        }

        await ExecuteAsync(connection, $"CREATE DATABASE [{Catalog}];");
    }

    private static async Task DropCatalogAsync()
    {
        var builder = new SqlConnectionStringBuilder(ReadGuardedConnectionString())
        {
            InitialCatalog = "master"
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var exists = await ScalarAsync<int?>(connection,
            "SELECT DB_ID(@name)", new SqlParameter("@name", Catalog));
        if (exists is not null)
        {
            await ExecuteAsync(connection,
                $"ALTER DATABASE [{Catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Catalog}];");
        }
    }

    private static async Task<T?> ScalarAsync<T>(DbConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null || value == DBNull.Value)
        {
            return default;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestClock(DateTimeOffset nowUtc) : IClock
    {
        public DateTimeOffset NowUtc { get; set; } = nowUtc;
        public DateTimeOffset UtcNow => NowUtc;
    }

    private sealed class TestSettings : ISettingsService
    {
        public Dictionary<string, string?> Values { get; } = [];
        public Barrier? ClaimBarrier { get; set; }

        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        {
            if (key == SettingKeys.SmsRetryMinutes && ClaimBarrier is not null)
            {
                ClaimBarrier.SignalAndWait(TimeSpan.FromSeconds(15));
            }

            return Task.FromResult(Values.GetValueOrDefault(key));
        }

        public Task<SettingsBundleDto> GetBundleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SettingsBundleDto> GetRuntimeBundleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrganizationProfileDto> UpdateOrganizationAsync(OrganizationProfileDto profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MissionRulesDto> UpdateMissionRulesAsync(MissionRulesDto rules, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionPolicyDto> UpdateSessionPolicyAsync(SessionPolicyDto policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MissionOrderTemplateDto> UpdateMissionOrderTemplateAsync(MissionOrderTemplateDto template, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SplitReasonDto>> ListSplitReasonsAsync(bool includeInactive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SplitReasonDto> CreateSplitReasonAsync(string title, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SplitReasonDto> UpdateSplitReasonAsync(Guid id, UpdateSplitReasonRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingGateway(
        Action onSend,
        Func<Task>? beforeResult = null,
        Func<SmsOutboundMessage, Task>? beforeResultWithMessage = null,
        SmsDeliveryResult? deliveryResult = null) : ISmsGateway
    {
        public async Task<SmsSendResult> SendAsync(SmsOutboundMessage message, CancellationToken cancellationToken = default)
        {
            onSend();
            if (beforeResultWithMessage is not null)
            {
                await beforeResultWithMessage(message);
            }
            else if (beforeResult is not null)
            {
                await beforeResult();
            }

            return SmsSendResult.Success("700123", "TestAccepted");
        }

        public Task<SmsDeliveryResult> GetDeliveryStatusAsync(string providerReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(deliveryResult ?? new SmsDeliveryResult(SmsDeliveryState.Unknown, "TestUnknown"));

        public Task<SmsBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SmsBalanceResult.Of("0"));
    }
}
