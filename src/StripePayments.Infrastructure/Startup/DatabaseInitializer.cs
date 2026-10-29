using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StripePayments.Application.Catalog;
using StripePayments.Infrastructure.Persistence;

namespace StripePayments.Infrastructure.Startup;

/// <summary>Applies migrations, seeds the catalog and mirrors it to Stripe so `docker compose up` is enough.</summary>
public sealed class DatabaseInitializer(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (configuration.GetValue("Database:MigrateOnStartup", true))
            await db.Database.MigrateAsync(ct);

        var catalog = scope.ServiceProvider.GetRequiredService<CatalogService>();
        await catalog.SeedAsync(ct);

        try
        {
            await catalog.SyncWithStripeAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not fatal: the API, health checks and webhooks still work; checkout reports a clear error.
            logger.LogError(ex, "Could not sync catalog with Stripe");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
