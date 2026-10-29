namespace StripePayments.Domain.Subscriptions;

public enum SubscriptionStatus
{
    Incomplete,
    IncompleteExpired,
    Trialing,
    Active,
    PastDue,
    Unpaid,
    Canceled,
    Paused
}

/// <summary>
/// Local mirror of a Stripe Subscription. Rows are created and updated only by webhooks,
/// in whatever order Stripe delivers them.
/// </summary>
public sealed class Subscription
{
    private Subscription() { }

    public Guid Id { get; private set; }
    public string StripeSubscriptionId { get; private set; } = default!;
    public string StripeCustomerId { get; private set; } = default!;
    public string? CustomerEmail { get; private set; }
    public string? PlanCode { get; private set; }
    public string? StripePriceId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset? CurrentPeriodEnd { get; private set; }
    public DateTimeOffset? TrialEnd { get; private set; }
    public bool CancelAtPeriodEnd { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }
    public DateTimeOffset? LastPaymentAt { get; private set; }
    public DateTimeOffset? LastPaymentFailedAt { get; private set; }

    /// <summary>Creation time of the newest subscription event applied; older snapshots are ignored.</summary>
    public DateTimeOffset? LastSyncedEventAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsLive => Status is not (SubscriptionStatus.Canceled or SubscriptionStatus.IncompleteExpired);

    public static Subscription Create(string stripeSubscriptionId, string stripeCustomerId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        StripeSubscriptionId = stripeSubscriptionId,
        StripeCustomerId = stripeCustomerId,
        Status = SubscriptionStatus.Incomplete,
        CreatedAt = now,
        UpdatedAt = now
    };

    public void AttachCheckout(string? email, string? planCode, DateTimeOffset now)
    {
        CustomerEmail ??= email;
        PlanCode ??= planCode;
        UpdatedAt = now;
    }

    /// <returns><c>false</c> when the snapshot is older than one already applied.</returns>
    public bool Sync(SubscriptionSnapshot s, DateTimeOffset eventCreatedAt, DateTimeOffset now)
    {
        if (LastSyncedEventAt is { } last && eventCreatedAt < last)
            return false;

        Status = s.Status;
        StripePriceId = s.PriceId ?? StripePriceId;
        PlanCode = s.PlanCode ?? PlanCode;
        CurrentPeriodEnd = s.CurrentPeriodEnd;
        TrialEnd = s.TrialEnd;
        CancelAtPeriodEnd = s.CancelAtPeriodEnd;
        CanceledAt = s.CanceledAt;
        LastSyncedEventAt = eventCreatedAt;
        UpdatedAt = now;
        return true;
    }

    public void RecordPayment(DateTimeOffset now)
    {
        LastPaymentAt = now;
        UpdatedAt = now;
    }

    public void RecordPaymentFailure(DateTimeOffset now)
    {
        LastPaymentFailedAt = now;
        UpdatedAt = now;
    }

    public static SubscriptionStatus ParseStatus(string stripeStatus) => stripeStatus switch
    {
        "incomplete" => SubscriptionStatus.Incomplete,
        "incomplete_expired" => SubscriptionStatus.IncompleteExpired,
        "trialing" => SubscriptionStatus.Trialing,
        "active" => SubscriptionStatus.Active,
        "past_due" => SubscriptionStatus.PastDue,
        "unpaid" => SubscriptionStatus.Unpaid,
        "canceled" => SubscriptionStatus.Canceled,
        "paused" => SubscriptionStatus.Paused,
        _ => throw new ArgumentOutOfRangeException(nameof(stripeStatus), stripeStatus, "Unknown Stripe subscription status")
    };
}

public sealed record SubscriptionSnapshot(
    SubscriptionStatus Status,
    string? PriceId,
    string? PlanCode,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt);
