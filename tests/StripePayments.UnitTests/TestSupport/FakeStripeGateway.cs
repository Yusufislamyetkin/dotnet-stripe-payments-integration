using StripePayments.Application.Abstractions;

namespace StripePayments.UnitTests.TestSupport;

public sealed class FakeStripeGateway : IStripeGateway
{
    public bool IsConfigured { get; set; } = true;
    public List<CheckoutSessionSpec> Sessions { get; } = [];

    public Task<string> EnsurePriceAsync(PriceSpec spec, CancellationToken ct) => Task.FromResult($"price_{spec.LookupKey}");

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutSessionSpec spec, CancellationToken ct)
    {
        Sessions.Add(spec);
        var id = $"cs_test_{Sessions.Count}";
        return Task.FromResult(new CheckoutSessionResult(id, $"https://checkout.stripe.test/{id}"));
    }
}
