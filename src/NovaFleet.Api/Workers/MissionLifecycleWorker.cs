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
                    .ToListAsync(stoppingToken);
                foreach (var mission in toStart)
                {
                    mission.StartAutomatically(now);
                }

                var toComplete = await db.Missions
                    .Where(item => item.Status == MissionStatus.Running && item.PlannedEndAtUtc <= now)
                    .ToListAsync(stoppingToken);
                foreach (var mission in toComplete)
                {
                    mission.CompleteAutomatically(now);
                }

                var autoCloseHours = Parse(await settings.GetValueAsync(SettingKeys.WithdrawalAutoCloseHours, stoppingToken), SettingKeys.DefaultWithdrawalAutoCloseHours);
                var closeBefore = now.AddHours(-autoCloseHours);
                var toClose = await db.Missions
                    .Where(item => item.Status == MissionStatus.WithdrawalPending && item.PlannedEndAtUtc <= closeBefore)
                    .ToListAsync(stoppingToken);
                foreach (var mission in toClose)
                {
                    mission.AutoCloseExpiredWithdrawal(now);
                }

                if (toStart.Count + toComplete.Count + toClose.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    foreach (var mission in toStart)
                    {
                        await audit.WriteAsync(new AuditRecord("AutoStartMission", "Mission", mission.Id.ToString(), "Success", After: new { mission.MissionNumber }), stoppingToken);
                    }

                    foreach (var mission in toComplete)
                    {
                        await audit.WriteAsync(new AuditRecord("AutoCompleteMission", "Mission", mission.Id.ToString(), "Success", After: new { mission.MissionNumber }), stoppingToken);
                    }

                    foreach (var mission in toClose)
                    {
                        await audit.WriteAsync(new AuditRecord("AutoCloseWithdrawal", "Mission", mission.Id.ToString(), "Success", After: new { mission.MissionNumber }), stoppingToken);
                    }

                    logger.LogInformation("Mission lifecycle worker: {Started} started, {Completed} completed, {Closed} withdrawals closed.", toStart.Count, toComplete.Count, toClose.Count);
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
