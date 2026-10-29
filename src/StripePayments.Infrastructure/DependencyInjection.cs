using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StripePayments.Application.Abstractions;
using StripePayments.Infrastructure.Persistence;
using StripePayments.Infrastructure.Startup;
using StripePayments.Infrastructure.Stripe;

namespace StripePayments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing.");

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.Section))
            .Validate(o => string.IsNullOrWhiteSpace(o.SecretKey) || StripeOptions.IsTestKey(o.SecretKey),
                "Stripe:SecretKey must be a TEST key (sk_test_... or rk_test_...). Live keys are refused by design.")
            .ValidateOnStart();
        services.AddSingleton<IStripeGateway, StripeGateway>();

        services.AddHostedService<DatabaseInitializer>();
        return services;
    }
}
