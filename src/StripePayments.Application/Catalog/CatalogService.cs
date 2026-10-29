using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StripePayments.Application.Abstractions;
using StripePayments.Domain.Catalog;

namespace StripePayments.Application.Catalog;

public sealed record ProductDto(string Sku, string Name, string Description, long Amount, string Currency, bool Purchasable);

public sealed record CatalogDto(IReadOnlyList<ProductDto> Products);

public sealed class CatalogService(
    IAppDbContext db,
    IStripeGateway stripe,
    IOptions<CatalogOptions> options,
    ILogger<CatalogService> logger)
{
    public async Task<CatalogDto> GetAsync(CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().OrderBy(p => p.UnitAmount)
            .Select(p => new ProductDto(p.Sku, p.Name, p.Description, p.UnitAmount, p.Currency, p.StripePriceId != null))
            .ToListAsync(ct);

        return new CatalogDto(products);
    }

    /// <summary>Upserts the configured catalog into the database. Idempotent.</summary>
    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.Products.ToDictionaryAsync(p => p.Sku, ct);
        foreach (var o in options.Value.Products)
        {
            if (existing.TryGetValue(o.Sku, out var product))
                product.Update(o.Name, o.Description, o.Amount, o.Currency);
            else
                db.Products.Add(Product.Create(o.Sku, o.Name, o.Description, o.Amount, o.Currency));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Creates missing Stripe prices (by lookup key) so the demo works without dashboard setup.</summary>
    public async Task SyncWithStripeAsync(CancellationToken ct)
    {
        if (!stripe.IsConfigured)
        {
            logger.LogWarning("Stripe secret key is not set; catalog is not synced and checkout is disabled");
            return;
        }

        foreach (var product in await db.Products.Where(p => p.StripePriceId == null).ToListAsync(ct))
            product.AssignStripePrice(await stripe.EnsurePriceAsync(
                new PriceSpec(product.StripeLookupKey, product.Name, product.UnitAmount, product.Currency), ct));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Catalog synced with Stripe");
    }
}
