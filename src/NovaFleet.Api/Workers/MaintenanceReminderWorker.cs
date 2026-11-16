using NovaFleet.Infrastructure.Maintenance;

namespace NovaFleet.Api.Workers;

public sealed class MaintenanceReminderWorker(IServiceScopeFactory scopes, ILogger<MaintenanceReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MaintenanceReminders>().SendAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Maintenance reminder processing failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
