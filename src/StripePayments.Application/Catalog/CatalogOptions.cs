namespace StripePayments.Application.Catalog;

/// <summary>Catalog seeded into the database (and mirrored to Stripe) on startup.</summary>
public sealed class CatalogOptions
{
    public const string Section = "Catalog";

    public List<ProductOption> Products { get; init; } = [];
}

public sealed class ProductOption
{
    public string Sku { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string Description { get; init; } = "";
    public long Amount { get; init; }
    public string Currency { get; init; } = "usd";
}
