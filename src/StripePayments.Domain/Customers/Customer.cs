namespace StripePayments.Domain.Customers;

/// <summary>Local mirror of a Stripe Customer, created from checkout webhooks.</summary>
public sealed class Customer
{
    private Customer() { }

    public Guid Id { get; private set; }
    public string StripeCustomerId { get; private set; } = default!;
    public string? Email { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Customer Create(string stripeCustomerId, string? email, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), StripeCustomerId = stripeCustomerId, Email = email, CreatedAt = now };

    public void UpdateEmail(string? email) => Email = email ?? Email;
}
