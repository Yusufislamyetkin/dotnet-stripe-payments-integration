namespace StripePayments.Application.Coupons;

/// <summary>Stripe promotion code + coupon data needed to evaluate a discount.</summary>
public sealed record PromotionCodeInfo(
    string Id,
    string Code,
    bool Active,
    bool CouponValid,
    decimal? PercentOff,
    long? AmountOff,
    string? Currency,
    DateTimeOffset? ExpiresAt,
    long? MaxRedemptions,
    long TimesRedeemed,
    long? MinimumAmount,
    string? MinimumAmountCurrency);

public sealed record CouponValidationResult(
    string Code,
    bool Valid,
    string? Reason,
    long OriginalAmount,
    long DiscountAmount,
    long FinalAmount,
    string Currency,
    string? Description)
{
    public string? PromotionCodeId { get; init; }
}

/// <summary>
/// Pure discount rules, applied before sending a customer to Checkout so they get a clear answer
/// instead of a generic Stripe error. Stripe re-validates the code when the session is created.
/// </summary>
public static class CouponEvaluator
{
    public static CouponValidationResult Evaluate(string code, PromotionCodeInfo? promo, long amount, string currency, DateTimeOffset now)
    {
        currency = currency.ToLowerInvariant();
        CouponValidationResult Invalid(string reason) => new(code, false, reason, amount, 0, amount, currency, null);

        if (promo is null) return Invalid("Code not found.");
        if (!promo.Active || !promo.CouponValid) return Invalid("Code is no longer active.");
        if (promo.ExpiresAt is { } expires && expires <= now) return Invalid("Code has expired.");
        if (promo.MaxRedemptions is { } max && promo.TimesRedeemed >= max) return Invalid("Code has reached its redemption limit.");

        if (promo.MinimumAmount is { } min)
        {
            if (!string.Equals(promo.MinimumAmountCurrency, currency, StringComparison.OrdinalIgnoreCase))
                return Invalid($"Code requires a {promo.MinimumAmountCurrency?.ToUpperInvariant()} purchase.");
            if (amount < min)
                return Invalid($"Code requires a minimum purchase of {min / 100m:0.00} {currency.ToUpperInvariant()}.");
        }

        long discount;
        string description;
        if (promo.PercentOff is { } percent)
        {
            discount = (long)Math.Round(amount * percent / 100m, MidpointRounding.AwayFromZero);
            description = $"{percent:0.##}% off";
        }
        else if (promo.AmountOff is { } off)
        {
            if (!string.Equals(promo.Currency, currency, StringComparison.OrdinalIgnoreCase))
                return Invalid($"Code only applies to {promo.Currency?.ToUpperInvariant()} purchases.");
            discount = Math.Min(off, amount);
            description = $"{off / 100m:0.00} {currency.ToUpperInvariant()} off";
        }
        else
        {
            return Invalid("Code has no discount configured.");
        }

        return new CouponValidationResult(code, true, null, amount, discount, amount - discount, currency, description)
        {
            PromotionCodeId = promo.Id
        };
    }
}
