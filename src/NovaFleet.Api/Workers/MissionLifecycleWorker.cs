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

                var toStart = await db.Missions
                    .Where(item => item.Status == MissionStatus.Confirmed && item.PlannedStartAtUtc <= now)
                    .OrderBy(item => item.PlannedStartAtUtc)
                    .ThenBy(item => item.Id)
                    .Take(MaxDuePerTick)
                    .ToListAsync(stoppingToken);

                var toComplete = await db.Missions
                    .Where(item => item.Status == MissionStatus.Running && item.PlannedEndAtUtc <= now)
                    .OrderBy(item => item.PlannedEndAtUtc)
                    .ThenBy(item => item.Id)
                    .Take(MaxDuePerTick)
                    .ToListAsync(stoppingToken);

                var autoCloseHours = Parse(await settings.GetValueAsync(SettingKeys.WithdrawalAutoCloseHours, stoppingToken), SettingKeys.DefaultWithdrawalAutoCloseHours);
                var closeBefore = now.AddHours(-autoCloseHours);
                var toClose = await db.Missions
                    .Where(item => item.Status == MissionStatus.WithdrawalPending && item.WithdrawalRequestedAtUtc != null && item.WithdrawalRequestedAtUtc <= closeBefore)
                    .OrderBy(item => item.WithdrawalRequestedAtUtc)
                    .ThenBy(item => item.Id)
                    .Take(MaxDuePerTick)
                    .ToListAsync(stoppingToken);

                var legacy = await db.Missions
                    .Where(item => item.Status == MissionStatus.WithdrawalPending && item.WithdrawalRequestedAtUtc == null)
                    .ToListAsync(stoppingToken);
                foreach (var mission in legacy)
                {
                    logger.LogWarning("Mission lifecycle worker: legacy withdrawal request requires manual review for mission {MissionId}.", mission.Id);
                }

                var started = 0;
                var completed = 0;
                var closed = 0;

                foreach (var mission in toStart)
                {
                    try
                    {
                        mission.StartAutomatically(now);
                        await db.SaveChangesAsync(stoppingToken);
                        await audit.WriteAsync(new AuditRecord("AutoStartMission", "Mission", mission.Id.ToString(), "Success", After: new { mission.MissionNumber }), stoppingToken);
                        started++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Mission lifecycle worker: failed to auto-start mission {MissionId}.", mission.Id);
                    }
                }

                foreach (var mission in toComplete)
                {
                    try
                    {
                        mission.CompleteAutomatically(now);
                        await db.SaveChangesAsync(stoppingToken);
                        await audit.WriteAsync(new AuditRecord("AutoCompleteMission", "Mission", mission.Id.ToString(), "Success", After: new { mission.MissionNumber }), stoppingToken);
                        completed++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Mission lifecycle worker: failed to auto-complete mission {MissionId}.", mission.Id);
                    }
                }

                foreach (var mission in toClose)
                {
                    try
                    {
                        mission.AutoCloseExpiredWithdrawal(now);
                        await db.SaveChangesAsync(stoppingToken);
                        await audit.WriteAsync(new AuditRecord("AutoCloseWithdrawal", "Mission", mission.Id.ToString(), "Success", After: new { mission.MissionNumber }), stoppingToken);
                        closed++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Mission lifecycle worker: failed to auto-close withdrawal for mission {MissionId}.", mission.Id);
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
