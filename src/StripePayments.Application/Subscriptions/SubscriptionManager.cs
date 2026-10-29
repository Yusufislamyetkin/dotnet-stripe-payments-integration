using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Checkout;
using StripePayments.Application.Common;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Subscriptions;

namespace StripePayments.Application.Subscriptions;

public sealed record SubscriptionCheckoutRequest(string PlanCode, string? Email);

public sealed record CancelSubscriptionRequest(bool AtPeriodEnd = true);

public sealed record ChangePlanRequest(string PlanCode);

public sealed record PortalLinkResponse(string Url);

public sealed record SubscriptionDto(
    Guid Id,
    string StripeSubscriptionId,
    string? CustomerEmail,
    string? PlanCode,
    string Status,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? LastPaymentAt,
    DateTimeOffset? LastPaymentFailedAt)
{
    public static SubscriptionDto From(Subscription s) => new(s.Id, s.StripeSubscriptionId, s.CustomerEmail, s.PlanCode,
        s.Status.ToString(), s.CurrentPeriodEnd, s.TrialEnd, s.CancelAtPeriodEnd, s.LastPaymentAt, s.LastPaymentFailedAt);
}

public sealed class SubscriptionCheckoutRequestValidator : AbstractValidator<SubscriptionCheckoutRequest>
{
    public SubscriptionCheckoutRequestValidator()
    {
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
    }
}

public sealed class ChangePlanRequestValidator : AbstractValidator<ChangePlanRequest>
{
    public ChangePlanRequestValidator() => RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(64);
}

/// <summary>
/// Subscription commands. Each one asks Stripe to make the change and returns; the local row is
/// updated when the resulting customer.subscription.* webhook arrives.
/// </summary>
public sealed class SubscriptionManager(IAppDbContext db, IStripeGateway stripe, IOptions<AppOptions> app)
{
    public const string PlanCodeMetadataKey = "plan_code";

    public async Task<CheckoutResponse> CreateCheckoutAsync(SubscriptionCheckoutRequest request, CancellationToken ct)
    {
        if (!stripe.IsConfigured) throw new StripeNotConfiguredException();
        var plan = await GetSyncedPlan(request.PlanCode, ct);

        var session = await stripe.CreateCheckoutSessionAsync(new CheckoutSessionSpec(
            CheckoutMode.Subscription,
            plan.StripePriceId!,
            request.Email,
            $"{BaseUrl}/success.html?session_id={{CHECKOUT_SESSION_ID}}",
            $"{BaseUrl}/cancel.html",
            new Dictionary<string, string> { [PlanCodeMetadataKey] = plan.Code })
        {
            TrialDays = plan.TrialDays > 0 ? plan.TrialDays : null
        }, ct);

        return new CheckoutResponse(null, session.SessionId, session.Url);
    }

    public async Task CancelAsync(Guid id, CancelSubscriptionRequest request, CancellationToken ct)
    {
        var subscription = await GetLive(id, ct);
        await stripe.CancelSubscriptionAsync(subscription.StripeSubscriptionId, request.AtPeriodEnd, ct);
    }

    public async Task ChangePlanAsync(Guid id, ChangePlanRequest request, CancellationToken ct)
    {
        var subscription = await GetLive(id, ct);
        var plan = await GetSyncedPlan(request.PlanCode, ct);
        if (plan.StripePriceId == subscription.StripePriceId)
            throw new ConflictException($"Subscription is already on plan '{plan.Code}'.");

        await stripe.ChangeSubscriptionPriceAsync(subscription.StripeSubscriptionId, plan.StripePriceId!, ct);
    }

    public async Task<PortalLinkResponse> CreatePortalLinkAsync(Guid id, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Subscription", id);
        return new PortalLinkResponse(
            await stripe.CreateBillingPortalUrlAsync(subscription.StripeCustomerId, $"{BaseUrl}/orders.html", ct));
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListAsync(CancellationToken ct) =>
        (await db.Subscriptions.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(100).ToListAsync(ct))
        .Select(SubscriptionDto.From).ToList();

    private async Task<Subscription> GetLive(Guid id, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Subscription", id);
        if (!subscription.IsLive)
            throw new ConflictException($"Subscription is {subscription.Status}.");
        return subscription;
    }

    private async Task<Plan> GetSyncedPlan(string code, CancellationToken ct)
    {
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Code == code, ct)
            ?? throw new NotFoundException("Plan", code);
        return plan.StripePriceId is null
            ? throw new ConflictException($"Plan '{code}' is not synced with Stripe yet. Restart the API with a valid Stripe key.")
            : plan;
    }

    private string BaseUrl => app.Value.PublicBaseUrl.TrimEnd('/');
}
