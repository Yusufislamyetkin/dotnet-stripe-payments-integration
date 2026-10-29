using StripePayments.Application.Abstractions;
using StripePayments.Application.Catalog;
using StripePayments.Application.Coupons;

namespace StripePayments.UnitTests.TestSupport;

public sealed class FakeStripeGateway : IStripeGateway
{
    public bool IsConfigured { get; set; } = true;
    public List<CheckoutSessionSpec> Sessions { get; } = [];
    public List<string> Calls { get; } = [];

    public Task<string> EnsurePriceAsync(PriceSpec spec, CancellationToken ct) => Task.FromResult($"price_{spec.LookupKey}");

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutSessionSpec spec, CancellationToken ct)
    {
        Sessions.Add(spec);
        var id = $"cs_test_{Sessions.Count}";
        return Task.FromResult(new CheckoutSessionResult(id, $"https://checkout.stripe.test/{id}"));
    }

    public Task<string> CreateBillingPortalUrlAsync(string customerId, string returnUrl, CancellationToken ct)
    {
        Calls.Add($"portal:{customerId}");
        return Task.FromResult($"https://billing.stripe.test/{customerId}");
    }

    public Task CancelSubscriptionAsync(string subscriptionId, bool atPeriodEnd, CancellationToken ct)
    {
        Calls.Add($"cancel:{subscriptionId}:{atPeriodEnd}");
        return Task.CompletedTask;
    }

    public Dictionary<string, PromotionCodeInfo> PromotionCodes { get; } = [];

    public Task<PromotionCodeInfo?> FindPromotionCodeAsync(string code, CancellationToken ct) =>
        Task.FromResult(PromotionCodes.GetValueOrDefault(code));

    public Task EnsurePromotionCodeAsync(CouponOption coupon, CancellationToken ct) => Task.CompletedTask;

    public Task ChangeSubscriptionPriceAsync(string subscriptionId, string newPriceId, CancellationToken ct)
    {
        Calls.Add($"change:{subscriptionId}:{newPriceId}");
        return Task.CompletedTask;
    }
}
