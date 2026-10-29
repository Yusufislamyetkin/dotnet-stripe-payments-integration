using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Common;

namespace StripePayments.Infrastructure.Stripe;

/// <summary>Stripe.net implementation of <see cref="IStripeGateway"/>.</summary>
public sealed class StripeGateway(IOptions<StripeOptions> options) : IStripeGateway
{
    private readonly StripeClient? _client =
        string.IsNullOrWhiteSpace(options.Value.SecretKey) ? null : new StripeClient(options.Value.SecretKey);

    public bool IsConfigured => _client is not null;

    private StripeClient Client => _client ?? throw new StripeNotConfiguredException();

    public Task<string> EnsurePriceAsync(PriceSpec spec, CancellationToken ct) => Call(async () =>
    {
        var prices = new PriceService(Client);
        var existing = await prices.ListAsync(new PriceListOptions
        {
            LookupKeys = [spec.LookupKey],
            Active = true,
            Limit = 1
        }, cancellationToken: ct);
        if (existing.Data.Count > 0)
            return existing.Data[0].Id;

        var created = await prices.CreateAsync(new PriceCreateOptions
        {
            LookupKey = spec.LookupKey,
            UnitAmount = spec.UnitAmount,
            Currency = spec.Currency,
            ProductData = new PriceProductDataOptions { Name = spec.ProductName },
            Recurring = spec.RecurringInterval is null ? null : new PriceRecurringOptions { Interval = spec.RecurringInterval }
        }, cancellationToken: ct);
        return created.Id;
    });

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutSessionSpec spec, CancellationToken ct) => Call(async () =>
    {
        var metadata = new Dictionary<string, string>(spec.Metadata);
        var create = new SessionCreateOptions
        {
            Mode = spec.Mode == CheckoutMode.Payment ? "payment" : "subscription",
            LineItems = [new SessionLineItemOptions { Price = spec.PriceId, Quantity = 1 }],
            CustomerEmail = spec.CustomerEmail,
            SuccessUrl = spec.SuccessUrl,
            CancelUrl = spec.CancelUrl,
            Metadata = metadata
        };

        if (spec.Mode == CheckoutMode.Payment)
            create.PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata };

        var session = await new SessionService(Client).CreateAsync(create, cancellationToken: ct);
        return new CheckoutSessionResult(session.Id, session.Url);
    });

    private static async Task<T> Call<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (StripeException ex)
        {
            throw new PaymentProviderException(ex.StripeError?.Message ?? ex.Message, ex);
        }
    }
}
