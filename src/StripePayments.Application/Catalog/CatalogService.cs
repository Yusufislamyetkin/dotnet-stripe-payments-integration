using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StripePayments.Application.Abstractions;
using StripePayments.Domain.Catalog;

namespace StripePayments.Application.Catalog;

public sealed record ProductDto(string Sku, string Name, string Description, long Amount, string Currency, bool Purchasable);

public sealed record PlanDto(string Code, string Name, string Interval, long Amount, string Currency, int TrialDays, bool Purchasable);

public sealed record CatalogDto(IReadOnlyList<ProductDto> Products, IReadOnlyList<PlanDto> Plans);

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

        var plans = (await db.Plans.AsNoTracking().OrderBy(p => p.Interval).ThenBy(p => p.Amount).ToListAsync(ct))
            .Select(p => new PlanDto(p.Code, p.Name, p.StripeInterval, p.Amount, p.Currency, p.TrialDays, p.StripePriceId != null))
            .ToList();

        return new CatalogDto(products, plans);
    }

    /// <summary>Upserts the configured catalog into the database. Idempotent.</summary>
    public async Task SeedAsync(CancellationToken ct)
    {
        var products = await db.Products.ToDictionaryAsync(p => p.Sku, ct);
        foreach (var o in options.Value.Products)
        {
            if (products.TryGetValue(o.Sku, out var product))
                product.Update(o.Name, o.Description, o.Amount, o.Currency);
            else
                db.Products.Add(Product.Create(o.Sku, o.Name, o.Description, o.Amount, o.Currency));
        }

        var plans = await db.Plans.ToDictionaryAsync(p => p.Code, ct);
        foreach (var o in options.Value.Plans)
        {
            if (plans.TryGetValue(o.Code, out var plan))
                plan.Update(o.Name, o.Interval, o.Amount, o.Currency, o.TrialDays);
            else
                db.Plans.Add(Plan.Create(o.Code, o.Name, o.Interval, o.Amount, o.Currency, o.TrialDays));
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

        foreach (var plan in await db.Plans.Where(p => p.StripePriceId == null).ToListAsync(ct))
            plan.AssignStripePrice(await stripe.EnsurePriceAsync(
                new PriceSpec(plan.StripeLookupKey, plan.Name, plan.Amount, plan.Currency, plan.StripeInterval), ct));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Catalog synced with Stripe");
    }
}
