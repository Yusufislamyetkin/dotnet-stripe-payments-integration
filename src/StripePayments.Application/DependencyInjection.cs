using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StripePayments.Application.Catalog;
using StripePayments.Application.Checkout;
using StripePayments.Application.Common;
using StripePayments.Application.Connect;
using StripePayments.Application.Coupons;
using StripePayments.Application.Orders;
using StripePayments.Application.Outbox;
using StripePayments.Application.Refunds;
using StripePayments.Application.Subscriptions;
using StripePayments.Application.Webhooks;

namespace StripePayments.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));
        services.Configure<CatalogOptions>(configuration.GetSection(CatalogOptions.Section));
        services.Configure<ConnectOptions>(configuration.GetSection(ConnectOptions.Section));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Section));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<CatalogService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderQueries>();
        services.AddScoped<WebhookProcessor>();
        services.AddScoped<SubscriptionManager>();
        services.AddScoped<CouponService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ConnectService>();
        services.AddScoped<OutboxProcessor>();
        return services;
    }
}
