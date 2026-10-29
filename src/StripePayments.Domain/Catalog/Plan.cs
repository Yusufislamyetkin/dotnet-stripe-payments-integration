namespace StripePayments.Domain.Catalog;

public enum BillingInterval { Month, Year }

/// <summary>A recurring subscription plan. Seeded from configuration and mirrored to a recurring Stripe Price.</summary>
public sealed class Plan
{
    private Plan() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public BillingInterval Interval { get; private set; }
    public long Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public int TrialDays { get; private set; }
    public string? StripePriceId { get; private set; }

    public string StripeInterval => Interval == BillingInterval.Month ? "month" : "year";
    public string StripeLookupKey => $"{Code}_{Amount}_{Currency}_{StripeInterval}";

    public static Plan Create(string code, string name, BillingInterval interval, long amount, string currency, int trialDays)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Code = code };
        plan.Update(name, interval, amount, currency, trialDays);
        return plan;
    }

    public void Update(string name, BillingInterval interval, long amount, string currency, int trialDays)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (trialDays < 0) throw new ArgumentOutOfRangeException(nameof(trialDays));

        currency = currency.ToLowerInvariant();
        if (amount != Amount || currency != Currency || interval != Interval)
            StripePriceId = null;

        Name = name;
        Interval = interval;
        Amount = amount;
        Currency = currency;
        TrialDays = trialDays;
    }

    public void AssignStripePrice(string priceId) => StripePriceId = priceId;
}
