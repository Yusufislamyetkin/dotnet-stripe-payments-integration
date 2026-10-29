namespace StripePayments.Domain.Catalog;

/// <summary>A one-time purchasable item. Seeded from configuration and mirrored to a Stripe Price.</summary>
public sealed class Product
{
    private Product() { }

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public long UnitAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public string? StripePriceId { get; private set; }

    /// <summary>Stripe prices are immutable, so the lookup key changes whenever the amount or currency changes.</summary>
    public string StripeLookupKey => $"{Sku}_{UnitAmount}_{Currency}";

    public static Product Create(string sku, string name, string description, long unitAmount, string currency)
    {
        var product = new Product { Id = Guid.NewGuid(), Sku = sku };
        product.Update(name, description, unitAmount, currency);
        return product;
    }

    public void Update(string name, string description, long unitAmount, string currency)
    {
        if (unitAmount <= 0) throw new ArgumentOutOfRangeException(nameof(unitAmount));

        currency = currency.ToLowerInvariant();
        if (unitAmount != UnitAmount || currency != Currency)
            StripePriceId = null;

        Name = name;
        Description = description;
        UnitAmount = unitAmount;
        Currency = currency;
    }

    public void AssignStripePrice(string priceId) => StripePriceId = priceId;
}
