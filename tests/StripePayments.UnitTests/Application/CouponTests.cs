using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StripePayments.Application.Checkout;
using StripePayments.Application.Common;
using StripePayments.Application.Coupons;
using StripePayments.Domain.Catalog;
using StripePayments.UnitTests.TestSupport;

namespace StripePayments.UnitTests.Application;

public class CouponTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static PromotionCodeInfo Promo(decimal? percent = null, long? amountOff = null, string? currency = null,
        bool active = true, DateTimeOffset? expires = null, long? max = null, long redeemed = 0, long? minimum = null) =>
        new("promo_1", "CODE", active, true, percent, amountOff, currency, expires, max, redeemed, minimum, minimum is null ? null : "usd");

    [Fact]
    public void Percent_off_is_rounded_to_minor_units()
    {
        var r = CouponEvaluator.Evaluate("CODE", Promo(percent: 15), 4999, "usd", Now);

        Assert.True(r.Valid);
        Assert.Equal(750, r.DiscountAmount);
        Assert.Equal(4249, r.FinalAmount);
        Assert.Equal("promo_1", r.PromotionCodeId);
    }

    [Fact]
    public void Amount_off_never_exceeds_price()
    {
        var r = CouponEvaluator.Evaluate("CODE", Promo(amountOff: 10000, currency: "usd"), 4900, "usd", Now);

        Assert.Equal(4900, r.DiscountAmount);
        Assert.Equal(0, r.FinalAmount);
    }

    [Theory]
    [InlineData("not-found")]
    [InlineData("inactive")]
    [InlineData("expired")]
    [InlineData("exhausted")]
    [InlineData("below-minimum")]
    [InlineData("wrong-currency")]
    public void Invalid_codes_are_rejected_with_a_reason(string scenario)
    {
        PromotionCodeInfo? promo = scenario switch
        {
            "not-found" => null,
            "inactive" => Promo(percent: 10, active: false),
            "expired" => Promo(percent: 10, expires: Now.AddSeconds(-1)),
            "exhausted" => Promo(percent: 10, max: 5, redeemed: 5),
            "below-minimum" => Promo(percent: 10, minimum: 10000),
            "wrong-currency" => Promo(amountOff: 500, currency: "eur"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var r = CouponEvaluator.Evaluate("CODE", promo, 4900, "usd", Now);

        Assert.False(r.Valid);
        Assert.NotNull(r.Reason);
        Assert.Equal(4900, r.FinalAmount);
    }

    [Fact]
    public async Task Checkout_applies_valid_code_and_records_it_on_the_order()
    {
        await using var db = TestDb.Create();
        var product = Product.Create("sku-1", "Item", "", 5000, "usd");
        product.AssignStripePrice("price_1");
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var stripe = new FakeStripeGateway();
        stripe.PromotionCodes["WELCOME10"] = Promo(percent: 10);
        var checkout = new CheckoutService(db, stripe, new CouponService(db, stripe, TimeProvider.System),
            Options.Create(new AppOptions()), TimeProvider.System);

        await checkout.CreateOneTimeAsync(new OneTimeCheckoutRequest("sku-1", null, "WELCOME10"), default);

        Assert.Equal("promo_1", Assert.Single(stripe.Sessions).PromotionCodeId);
        Assert.Equal("WELCOME10", (await db.Orders.SingleAsync()).PromotionCode);
    }

    [Fact]
    public async Task Checkout_with_invalid_code_is_a_validation_error_and_creates_nothing()
    {
        await using var db = TestDb.Create();
        var product = Product.Create("sku-1", "Item", "", 5000, "usd");
        product.AssignStripePrice("price_1");
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var stripe = new FakeStripeGateway();
        var checkout = new CheckoutService(db, stripe, new CouponService(db, stripe, TimeProvider.System),
            Options.Create(new AppOptions()), TimeProvider.System);

        await Assert.ThrowsAsync<ValidationException>(() =>
            checkout.CreateOneTimeAsync(new OneTimeCheckoutRequest("sku-1", null, "NOPE"), default));

        Assert.Empty(stripe.Sessions);
        Assert.Empty(db.Orders);
    }

    [Fact]
    public void Request_needs_exactly_one_target()
    {
        var validator = new ValidateCouponRequestValidator();

        Assert.False(validator.Validate(new ValidateCouponRequest("WELCOME10", null, null)).IsValid);
        Assert.False(validator.Validate(new ValidateCouponRequest("WELCOME10", "a", "b")).IsValid);
        Assert.False(validator.Validate(new ValidateCouponRequest("bad code!", "a", null)).IsValid);
        Assert.True(validator.Validate(new ValidateCouponRequest("WELCOME10", "a", null)).IsValid);
    }
}
