using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StripePayments.Application.Catalog;
using StripePayments.Application.Checkout;
using StripePayments.Application.Common;
using StripePayments.Application.Coupons;
using StripePayments.Application.Orders;
using StripePayments.Application.Subscriptions;
using StripePayments.Application.Webhooks;

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
        services.AddScoped<WebhookProcessor>();
        services.AddScoped<SubscriptionManager>();
        services.AddScoped<CouponService>();
        return services;
    }
}
