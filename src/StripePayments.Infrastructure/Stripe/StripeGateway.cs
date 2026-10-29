using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Catalog;
using StripePayments.Application.Common;
using StripePayments.Application.Coupons;

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

        if (spec.PromotionCodeId is null)
            create.AllowPromotionCodes = true;
        else
            create.Discounts = [new SessionDiscountOptions { PromotionCode = spec.PromotionCodeId }];

        if (spec.Mode == CheckoutMode.Payment)
            create.PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata };
        else
            create.SubscriptionData = new SessionSubscriptionDataOptions { TrialPeriodDays = spec.TrialDays, Metadata = metadata };

        var session = await new SessionService(Client).CreateAsync(create, cancellationToken: ct);
        return new CheckoutSessionResult(session.Id, session.Url);
    });

    public Task<string> CreateBillingPortalUrlAsync(string customerId, string returnUrl, CancellationToken ct) => Call(async () =>
    {
        var session = await new global::Stripe.BillingPortal.SessionService(Client).CreateAsync(
            new global::Stripe.BillingPortal.SessionCreateOptions { Customer = customerId, ReturnUrl = returnUrl },
            cancellationToken: ct);
        return session.Url;
    });

    public Task CancelSubscriptionAsync(string subscriptionId, bool atPeriodEnd, CancellationToken ct) => Call(async () =>
    {
        var subscriptions = new SubscriptionService(Client);
        return atPeriodEnd
            ? await subscriptions.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, cancellationToken: ct)
            : await subscriptions.CancelAsync(subscriptionId, cancellationToken: ct);
    });

    public Task ChangeSubscriptionPriceAsync(string subscriptionId, string newPriceId, CancellationToken ct) => Call(async () =>
    {
        var subscriptions = new SubscriptionService(Client);
        var current = await subscriptions.GetAsync(subscriptionId, cancellationToken: ct);
        return await subscriptions.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
        {
            Items = [new SubscriptionItemOptions { Id = current.Items.Data[0].Id, Price = newPriceId }],
            ProrationBehavior = "create_prorations",
            CancelAtPeriodEnd = false
        }, cancellationToken: ct);
    });

    public Task<PromotionCodeInfo?> FindPromotionCodeAsync(string code, CancellationToken ct) => Call(async () =>
    {
        var list = await new PromotionCodeService(Client).ListAsync(new PromotionCodeListOptions { Code = code, Limit = 1 }, cancellationToken: ct);
        if (list.Data.FirstOrDefault() is not { } p)
            return null;

        return new PromotionCodeInfo(
            p.Id, p.Code, p.Active, p.Coupon.Valid, p.Coupon.PercentOff, p.Coupon.AmountOff, p.Coupon.Currency,
            p.ExpiresAt is { } e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : null,
            p.MaxRedemptions, p.TimesRedeemed, p.Restrictions?.MinimumAmount, p.Restrictions?.MinimumAmountCurrency);
    });

    public Task EnsurePromotionCodeAsync(CouponOption coupon, CancellationToken ct) => Call(async () =>
    {
        var promotionCodes = new PromotionCodeService(Client);
        var existing = await promotionCodes.ListAsync(new PromotionCodeListOptions { Code = coupon.Code, Limit = 1 }, cancellationToken: ct);
        if (existing.Data.Count > 0)
            return existing.Data[0].Id;

        var created = await new global::Stripe.CouponService(Client).CreateAsync(new CouponCreateOptions
        {
            Name = coupon.Code,
            PercentOff = coupon.PercentOff,
            AmountOff = coupon.AmountOff,
            Currency = coupon.AmountOff is null ? null : coupon.Currency,
            Duration = "once"
        }, cancellationToken: ct);

        var promotionCode = await promotionCodes.CreateAsync(new PromotionCodeCreateOptions
        {
            Coupon = created.Id,
            Code = coupon.Code,
            Restrictions = coupon.MinimumAmount is null ? null : new PromotionCodeRestrictionsOptions
            {
                MinimumAmount = coupon.MinimumAmount,
                MinimumAmountCurrency = coupon.Currency ?? "usd"
            }
        }, cancellationToken: ct);
        return promotionCode.Id;
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
