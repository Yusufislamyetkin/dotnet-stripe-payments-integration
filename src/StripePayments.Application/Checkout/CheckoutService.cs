using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Common;
using StripePayments.Application.Coupons;
using StripePayments.Domain.Orders;

namespace StripePayments.Application.Checkout;

public sealed record OneTimeCheckoutRequest(string Sku, string? Email, string? PromotionCode = null);

public sealed record CheckoutResponse(Guid? OrderId, string SessionId, string Url);

public sealed class OneTimeCheckoutRequestValidator : AbstractValidator<OneTimeCheckoutRequest>
{
    public OneTimeCheckoutRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.PromotionCode).Matches("^[A-Za-z0-9_-]{3,64}$").When(x => !string.IsNullOrEmpty(x.PromotionCode));
    }
}

public sealed class CheckoutService(
    IAppDbContext db,
    IStripeGateway stripe,
    CouponService coupons,
    IOptions<AppOptions> app,
    TimeProvider clock)
{
    public const string OrderIdMetadataKey = "order_id";

    public async Task<CheckoutResponse> CreateOneTimeAsync(OneTimeCheckoutRequest request, CancellationToken ct)
    {
        if (!stripe.IsConfigured) throw new StripeNotConfiguredException();

        var product = await db.Products.SingleOrDefaultAsync(p => p.Sku == request.Sku, ct)
            ?? throw new NotFoundException("Product", request.Sku);
        if (product.StripePriceId is null)
            throw new ConflictException($"Product '{product.Sku}' is not synced with Stripe yet. Restart the API with a valid Stripe key.");

        var promotionCodeId = string.IsNullOrEmpty(request.PromotionCode)
            ? null
            : await coupons.ResolveForCheckoutAsync(request.PromotionCode, product.UnitAmount, product.Currency, ct);

        var order = Order.CreatePending(product, request.Email, request.PromotionCode, clock.GetUtcNow());

        var session = await stripe.CreateCheckoutSessionAsync(new CheckoutSessionSpec(
            CheckoutMode.Payment,
            product.StripePriceId,
            request.Email,
            SuccessUrl(),
            CancelUrl(),
            new Dictionary<string, string> { [OrderIdMetadataKey] = order.Id.ToString() })
        {
            PromotionCodeId = promotionCodeId
        }, ct);

        order.AttachCheckoutSession(session.SessionId);
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        return new CheckoutResponse(order.Id, session.SessionId, session.Url);
    }

    private string SuccessUrl() => $"{app.Value.PublicBaseUrl.TrimEnd('/')}/success.html?session_id={{CHECKOUT_SESSION_ID}}";
    private string CancelUrl() => $"{app.Value.PublicBaseUrl.TrimEnd('/')}/cancel.html";
}
