using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StripePayments.Application.Catalog;
using StripePayments.Application.Checkout;
using StripePayments.Application.Common;
using StripePayments.Application.Orders;

namespace StripePayments.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));
        services.Configure<CatalogOptions>(configuration.GetSection(CatalogOptions.Section));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<CatalogService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderQueries>();
        return services;
    }
}
