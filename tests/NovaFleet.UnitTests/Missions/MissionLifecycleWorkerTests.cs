using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovaFleet.Api.Workers;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Auditing;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.UnitTests.Missions;

public sealed class MissionLifecycleWorkerTests : MissionTestBase
{
    [Fact]
    public async Task Tick_ProcessesAtMostOneHundredDueMissionsInGlobalTimeOrder()
    {
        var resources = SaveReadyResources();
        var baseDue = Hour(0, 0);
        Clock.NowUtc = baseDue.AddHours(301);
        var dueByMission = new Dictionary<Guid, DateTimeOffset>();

        for (var index = 0; index < 101; index++)
        {
            var startDue = baseDue.AddHours(index * 3);
            var start = AddMission($"S{index:D3}", startDue, startDue.AddHours(1));
            start.Approve(resources.Driver.Id, resources.Vehicle.Id, ApproverUsername, startDue.AddHours(-1));
            dueByMission[start.Id] = startDue;

            var endDue = startDue.AddHours(1);
            var running = AddMission($"E{index:D3}", endDue.AddHours(-1), endDue);
            running.Approve(resources.Driver.Id, resources.Vehicle.Id, ApproverUsername, endDue.AddHours(-1));
            running.StartAutomatically(endDue.AddHours(-1));
            dueByMission[running.Id] = endDue;

            var closeDue = startDue.AddHours(2);
            var withdrawal = AddMission($"W{index:D3}", Clock.NowUtc.AddDays(30), Clock.NowUtc.AddDays(31));
            withdrawal.Approve(resources.Driver.Id, resources.Vehicle.Id, ApproverUsername, closeDue.AddHours(-25));
            withdrawal.RequestWithdrawal("آزمون", closeDue.AddHours(-24));
            dueByMission[withdrawal.Id] = closeDue;
        }
        await Db.SaveChangesAsync();
        var initialStatuses = Db.Missions.AsNoTracking().ToDictionary(item => item.Id, item => item.Status);
        Db.ChangeTracker.Clear();

        var logger = new TickLogger();
        await using var provider = CreateProvider(logger);
        var worker = new MissionLifecycleWorker(provider.GetRequiredService<IServiceScopeFactory>(), logger);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForSummaryAsync(logger.Summary.Task);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var expected = dueByMission.OrderBy(item => item.Value).Take(100).Select(item => item.Key).ToHashSet();
        var finalMissions = await Db.Missions.AsNoTracking().ToListAsync();
        var changed = finalMissions
            .Where(item => (initialStatuses[item.Id] == MissionStatus.Confirmed && item.Status == MissionStatus.Running)
                || (initialStatuses[item.Id] == MissionStatus.Running && item.Status == MissionStatus.Completed)
                || (initialStatuses[item.Id] == MissionStatus.WithdrawalPending && item.Status == MissionStatus.Cancelled))
            .Select(item => item.Id)
            .ToList();
        Assert.Empty(logger.Errors);
        var missing = expected.Except(changed).Select(id => dueByMission[id]).OrderBy(value => value).ToArray();
        var extra = changed.Except(expected).Select(id => dueByMission[id]).OrderBy(value => value).ToArray();
        Assert.True(expected.SetEquals(changed), $"Expected 100 earliest missions; actual {changed.Count}. Missing: {string.Join(",", missing)}; extra: {string.Join(",", extra)}.");
    }

    [Fact]
    public async Task Tick_FailedSaveDoesNotPoisonLaterRows()
    {
        var resources = SaveReadyResources();
        var first = AddMission("F001", Hour(0, 10), Hour(0, 11));
        var second = AddMission("F002", Hour(0, 12), Hour(0, 13));
        var third = AddMission("F003", Hour(0, 14), Hour(0, 15));
        first.Approve(resources.Driver.Id, resources.Vehicle.Id, ApproverUsername, Hour(0, 8));
        second.Approve(resources.Driver.Id, resources.Vehicle.Id, ApproverUsername, Hour(0, 8));
        third.Approve(resources.Driver.Id, resources.Vehicle.Id, ApproverUsername, Hour(0, 8));
        Clock.NowUtc = Hour(0, 16);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var interceptor = new FailOneSaveInterceptor(first.Id);
        var logger = new TickLogger();
        await using var provider = CreateProvider(logger, interceptor);
        var worker = new MissionLifecycleWorker(provider.GetRequiredService<IServiceScopeFactory>(), logger);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForSummaryAsync(logger.Summary.Task);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(MissionStatus.Confirmed, (await Db.Missions.AsNoTracking().SingleAsync(item => item.Id == first.Id)).Status);
        Assert.Equal(MissionStatus.Running, (await Db.Missions.AsNoTracking().SingleAsync(item => item.Id == second.Id)).Status);
        Assert.Equal(MissionStatus.Running, (await Db.Missions.AsNoTracking().SingleAsync(item => item.Id == third.Id)).Status);
        Assert.Equal(1, interceptor.Attempts);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationInsteadOfContinuingRows()
    {
        var resources = SaveReadyResources();
        var missions = Enumerable.Range(0, 3)
            .Select(index => AddMission($"C{index:D3}", Hour(0, 10 + index), Hour(0, 11 + index)))
            .ToList();
        foreach (var mission in missions)
            mission.Approve(resources.Driver.Id, resources.Vehicle.Id, ApproverUsername, Hour(0, 8));
        Clock.NowUtc = Hour(0, 16);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var interceptor = new CancelSaveInterceptor(missions[0].Id);
        var logger = new TickLogger();
        await using var provider = CreateProvider(logger, interceptor);
        var worker = new MissionLifecycleWorker(provider.GetRequiredService<IServiceScopeFactory>(), logger);
        var execute = typeof(MissionLifecycleWorker).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var execution = (Task)execute.Invoke(worker, [CancellationToken.None])!;

        await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, interceptor.Attempts);
        Assert.All(missions, mission => Assert.Equal(MissionStatus.Confirmed,
            Db.Missions.AsNoTracking().Single(item => item.Id == mission.Id).Status));
    }

    [Fact]
    public async Task Tick_LegacyWithdrawalWarningsAreBounded()
    {
        var resources = SaveReadyResources();
        var ids = new List<Guid>();
        for (var index = 0; index < 105; index++)
        {
            var mission = AddMission($"L{index:D3}", Hour(1, 10), Hour(1, 11));
            mission.Approve(resources.Driver.Id, resources.Vehicle.Id, ApproverUsername, Hour(0, 8));
            ids.Add(mission.Id);
        }
        var due = AddMission("L500", Hour(0, 10), Hour(0, 11));
        due.Approve(resources.Driver.Id, resources.Vehicle.Id, ApproverUsername, Hour(0, 8));
        Clock.NowUtc = Hour(0, 16);
        await Db.SaveChangesAsync();
        foreach (var id in ids)
            await Db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Missions] SET [Status] = {6}, [StatusBeforeWithdrawal] = {3}, [WithdrawalRequestedAtUtc] = NULL WHERE [Id] = {id}");
        Db.ChangeTracker.Clear();

        var logger = new TickLogger();
        await using var provider = CreateProvider(logger);
        var worker = new MissionLifecycleWorker(provider.GetRequiredService<IServiceScopeFactory>(), logger);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForSummaryAsync(logger.Summary.Task);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(100, logger.LegacyWarnings);
        Assert.Equal(MissionStatus.WithdrawalPending, (await Db.Missions.AsNoTracking().SingleAsync(item => item.Id == ids[0])).Status);
    }

    private Mission AddMission(string number, DateTimeOffset start, DateTimeOffset end)
    {
        var mission = new Mission(number, number, RequesterUsername, "آزمون", "09120000000", "آزمون",
            MissionType.Freight, null, "مبدأ", "مقصد", null, start, end);
        Db.Missions.Add(mission);
        return mission;
    }

    private ServiceProvider CreateProvider(TickLogger logger, SaveChangesInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ISettingsService>(Settings);
        services.AddSingleton<IAuditTrail>(new FakeAuditTrail());
        services.AddScoped(provider =>
        {
            var builder = new DbContextOptionsBuilder<NovaFleetDbContext>(Options);
            if (interceptor is not null)
                builder.AddInterceptors(interceptor);
            return new NovaFleetDbContext(builder.Options);
        });
        return services.BuildServiceProvider();
    }

    private static async Task WaitForSummaryAsync(Task summary)
    {
        var completed = await Task.WhenAny(summary, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(summary, completed);
        await summary;
    }

    private sealed class TickLogger : ILogger<MissionLifecycleWorker>
    {
        private int legacyWarnings;
        public TaskCompletionSource Summary { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Errors { get; } = [];
        public int LegacyWarnings => legacyWarnings;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (message.Contains("legacy withdrawal request", StringComparison.Ordinal))
                Interlocked.Increment(ref legacyWarnings);
            if (logLevel == LogLevel.Error)
                Errors.Add(message);
            if (logLevel == LogLevel.Information && message.Contains(" started, ", StringComparison.Ordinal))
                Summary.TrySetResult();
        }
    }

    private sealed class FailOneSaveInterceptor(Guid missionId) : SaveChangesInterceptor
    {
        private bool failed;
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var hasTarget = eventData.Context!.ChangeTracker.Entries<Mission>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.Id == missionId);
            if (hasTarget)
            {
                Attempts++;
                if (!failed)
                {
                    failed = true;
                    throw new InvalidOperationException("synthetic save failure");
                }
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class CancelSaveInterceptor(Guid missionId) : SaveChangesInterceptor
    {
        private readonly CancellationTokenSource cancellation = new();
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var hasTarget = eventData.Context!.ChangeTracker.Entries<Mission>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.Id == missionId);
            if (hasTarget)
            {
                Attempts++;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
