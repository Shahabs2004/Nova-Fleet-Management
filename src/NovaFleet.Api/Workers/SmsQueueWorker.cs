using NovaFleet.Application.Notifications;

namespace NovaFleet.Api.Workers;

/// <summary>پردازش صف پیامک و استعلام وضعیت تحویل؛ خطای درگاه گردش‌کار اصلی را متوقف نمی‌کند.</summary>
public sealed class SmsQueueWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SmsQueueWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var startupScope = scopeFactory.CreateScope();
            var startupDispatcher = startupScope.ServiceProvider.GetRequiredService<ISmsDispatcher>();
            await startupDispatcher.RecoverStaleSendsAsync(100, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMS stale-send recovery at worker startup failed.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<ISmsDispatcher>();
                var sent = await dispatcher.ProcessQueueAsync(20, stoppingToken);
                var polled = await dispatcher.PollDeliveryAsync(20, stoppingToken);
                if (sent > 0 || polled > 0)
                {
                    logger.LogDebug("SMS worker processed {Sent} sends and {Polled} status polls.", sent, polled);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SMS queue worker iteration failed.");
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
}
