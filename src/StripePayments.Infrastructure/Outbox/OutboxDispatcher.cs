using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StripePayments.Application.Outbox;

namespace StripePayments.Infrastructure.Outbox;

/// <summary>
/// Polls the outbox table and delivers due messages. Designed for a single API instance;
/// see README > Roadmap for the multi-instance variant.
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds)));
        if (string.IsNullOrWhiteSpace(options.Value.WebhookUrl))
            logger.LogWarning("Outbox:WebhookUrl is not set; integration events are stored but not delivered");

        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var delivered = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(stoppingToken);
                if (delivered > 0)
                    logger.LogInformation("Delivered {Count} outbox message(s)", delivered);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
