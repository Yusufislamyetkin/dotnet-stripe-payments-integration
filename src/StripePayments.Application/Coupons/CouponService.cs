using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Common;

namespace StripePayments.Application.Coupons;

public sealed record ValidateCouponRequest(string Code, string? Sku, string? PlanCode);

public sealed class ValidateCouponRequestValidator : AbstractValidator<ValidateCouponRequest>
{
    public ValidateCouponRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches("^[A-Za-z0-9_-]{3,64}$")
            .WithMessage("Code must be 3-64 letters, digits, '-' or '_'.");
        RuleFor(x => x).Must(x => string.IsNullOrEmpty(x.Sku) != string.IsNullOrEmpty(x.PlanCode))
            .WithName("Target").WithMessage("Provide exactly one of Sku or PlanCode.");
    }
}

public sealed class CouponService(IAppDbContext db, IStripeGateway stripe, TimeProvider clock)
{
    public async Task<CouponValidationResult> ValidateAsync(ValidateCouponRequest request, CancellationToken ct)
    {
        var (amount, currency) = await PriceOf(request, ct);
        return await EvaluateAsync(request.Code, amount, currency, ct);
    }

    /// <summary>Returns the valid evaluation (with the promotion code id to apply), or throws a 400 with the reason.</summary>
    public async Task<CouponValidationResult> ResolveForCheckoutAsync(string code, long amount, string currency, CancellationToken ct)
    {
        var result = await EvaluateAsync(code, amount, currency, ct);
        return result is { Valid: true, PromotionCodeId: not null }
            ? result
            : throw new ValidationException([new ValidationFailure("PromotionCode", result.Reason)]);
    }

    private async Task<CouponValidationResult> EvaluateAsync(string code, long amount, string currency, CancellationToken ct)
    {
        if (!stripe.IsConfigured) throw new StripeNotConfiguredException();
        var promo = await stripe.FindPromotionCodeAsync(code, ct);
        return CouponEvaluator.Evaluate(code, promo, amount, currency, clock.GetUtcNow());
    }

    private async Task<(long Amount, string Currency)> PriceOf(ValidateCouponRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.Sku))
        {
            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Sku == request.Sku, ct)
                ?? throw new NotFoundException("Product", request.Sku);
            return (product.UnitAmount, product.Currency);
        }

        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Code == request.PlanCode, ct)
            ?? throw new NotFoundException("Plan", request.PlanCode!);
        return (plan.Amount, plan.Currency);
    }
}
