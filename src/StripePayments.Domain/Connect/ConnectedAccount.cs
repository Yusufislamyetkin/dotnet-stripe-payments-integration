namespace StripePayments.Domain.Connect;

/// <summary>A seller onboarded through Stripe Connect Express. Capability flags follow account.updated webhooks.</summary>
public sealed class ConnectedAccount
{
    private ConnectedAccount() { }

    public Guid Id { get; private set; }
    public string StripeAccountId { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public bool ChargesEnabled { get; private set; }
    public bool PayoutsEnabled { get; private set; }
    public bool DetailsSubmitted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ConnectedAccount Create(string stripeAccountId, string email, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        StripeAccountId = stripeAccountId,
        Email = email,
        CreatedAt = now,
        UpdatedAt = now
    };

    public void UpdateCapabilities(bool chargesEnabled, bool payoutsEnabled, bool detailsSubmitted, DateTimeOffset now)
    {
        ChargesEnabled = chargesEnabled;
        PayoutsEnabled = payoutsEnabled;
        DetailsSubmitted = detailsSubmitted;
        UpdatedAt = now;
    }
}
