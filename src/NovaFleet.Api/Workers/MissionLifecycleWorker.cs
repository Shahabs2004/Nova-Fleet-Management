using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Auditing;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Api.Workers;

/// <summary>
/// چرخه عمر خودکار مأموریت: شروع و پایان سر زمان، بستن انصراف تعیین‌تکلیف‌نشده
/// پس از مهلت تنظیم‌شده و پایان نشست‌های منقضی.
/// </summary>
public sealed class MissionLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MissionLifecycleWorker> logger) : BackgroundService
{
    private const int MaxDuePerTick = 100;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private enum LifecycleAction { Start, Complete, CloseWithdrawal }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NovaFleetDbContext>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
                var now = clock.UtcNow;

                var due = new List<(Mission Mission, DateTimeOffset DueAtUtc, LifecycleAction Action)>(MaxDuePerTick * 3);
                var toStart = await db.Missions
                    .Where(item => item.Status == MissionStatus.Confirmed && item.PlannedStartAtUtc <= now)
                    .OrderBy(item => item.PlannedStartAtUtc)
                    .ThenBy(item => item.Id)
                    .Take(MaxDuePerTick)
                    .ToListAsync(stoppingToken);
                foreach (var mission in toStart)
                    due.Add((mission, mission.PlannedStartAtUtc, LifecycleAction.Start));

                var toComplete = await db.Missions
                    .Where(item => item.Status == MissionStatus.Running && item.PlannedEndAtUtc <= now)
                    .OrderBy(item => item.PlannedEndAtUtc)
                    .ThenBy(item => item.Id)
                    .Take(MaxDuePerTick)
                    .ToListAsync(stoppingToken);
                foreach (var mission in toComplete)
                    due.Add((mission, mission.PlannedEndAtUtc, LifecycleAction.Complete));

                var autoCloseHours = Parse(await settings.GetValueAsync(SettingKeys.WithdrawalAutoCloseHours, stoppingToken), SettingKeys.DefaultWithdrawalAutoCloseHours);
                var closeBefore = now.AddHours(-autoCloseHours);
                var toClose = await db.Missions
                    .Where(item => item.Status == MissionStatus.WithdrawalPending && item.WithdrawalRequestedAtUtc != null && item.WithdrawalRequestedAtUtc <= closeBefore)
                    .OrderBy(item => item.WithdrawalRequestedAtUtc)
                    .ThenBy(item => item.Id)
                    .Take(MaxDuePerTick)
                    .ToListAsync(stoppingToken);
                foreach (var mission in toClose)
                    due.Add((mission, mission.WithdrawalRequestedAtUtc!.Value.AddHours(autoCloseHours), LifecycleAction.CloseWithdrawal));

                var batch = due
                    .OrderBy(item => item.DueAtUtc)
                    .ThenBy(item => item.Action)
                    .ThenBy(item => item.Mission.Id)
                    .Take(MaxDuePerTick)
                    .ToList();

                var legacy = await db.Missions
                    .Where(item => item.Status == MissionStatus.WithdrawalPending && item.WithdrawalRequestedAtUtc == null)
                    .OrderBy(item => item.Id)
                    .Take(MaxDuePerTick)
                    .ToListAsync(stoppingToken);
                foreach (var mission in legacy)
                {
                    logger.LogWarning("Mission lifecycle worker: legacy withdrawal request requires manual review for mission {MissionId}.", mission.Id);
                }

                var started = 0;
                var completed = 0;
                var closed = 0;

                foreach (var item in batch)
                {
                    var mission = item.Mission;
                    try
                    {
                        (Action Mutate, string Audit) operation = item.Action switch
                        {
                            LifecycleAction.Start => (() => mission.StartAutomatically(now), "AutoStartMission"),
                            LifecycleAction.Complete => (() => mission.CompleteAutomatically(now), "AutoCompleteMission"),
                            LifecycleAction.CloseWithdrawal => (() => mission.AutoCloseExpiredWithdrawal(now), "AutoCloseWithdrawal"),
                            _ => throw new InvalidOperationException("Unknown mission lifecycle action.")
                        };
                        operation.Mutate();
                        await db.SaveChangesAsync(stoppingToken);
                        await audit.WriteAsync(new AuditRecord(operation.Audit, "Mission", mission.Id.ToString(), "Success", After: new { mission.MissionNumber }), stoppingToken);
                        if (item.Action == LifecycleAction.Start) started++;
                        else if (item.Action == LifecycleAction.Complete) completed++;
                        else closed++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // A failed mutation must not be retried by the next row's SaveChanges call.
                        db.Entry(mission).State = EntityState.Detached;
                        logger.LogError(ex, "Mission lifecycle worker: failed to process mission {MissionId}.", mission.Id);
                    }
                }

                if (started + completed + closed > 0)
                {
                    logger.LogInformation("Mission lifecycle worker: {Started} started, {Completed} completed, {Closed} withdrawals closed.", started, completed, closed);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Mission lifecycle worker iteration failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static int Parse(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
