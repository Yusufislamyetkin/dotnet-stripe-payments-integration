using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Checkout;
using StripePayments.Application.Subscriptions;
using StripePayments.Domain.Customers;
using StripePayments.Domain.Orders;
using StripePayments.Domain.Subscriptions;
using StripePayments.Domain.Webhooks;

namespace StripePayments.Application.Webhooks;

public enum WebhookResult { Processed, Duplicate, Ignored }

/// <summary>
/// Applies verified Stripe events to local state. The state change and the processed-event marker are
/// written in one SaveChanges call (one transaction), which makes every handler idempotent.
/// </summary>
public sealed class WebhookProcessor(IAppDbContext db, TimeProvider clock, ILogger<WebhookProcessor> logger)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<WebhookResult> ProcessAsync(StripeEventEnvelope evt, CancellationToken ct)
    {
        if (await db.ProcessedStripeEvents.AnyAsync(e => e.EventId == evt.Id, ct))
        {
            logger.LogInformation("Skipping duplicate Stripe event {EventId} ({Type})", evt.Id, evt.Type);
            return WebhookResult.Duplicate;
        }

        var handled = evt.Data switch
        {
            CheckoutSessionCompleted e => await OnCheckoutCompleted(e, ct),
            SubscriptionChanged e => await OnSubscriptionChanged(e, evt.Created, ct),
            InvoiceEvent e when evt.Type == StripeEventTypes.InvoicePaid => await OnInvoice(e, paid: true, ct),
            InvoiceEvent e when evt.Type == StripeEventTypes.InvoicePaymentFailed => await OnInvoice(e, paid: false, ct),
            ChargeRefunded e => await OnChargeRefunded(e, ct),
            AccountUpdated e => await OnAccountUpdated(e, ct),
            _ => false
        };

        db.ProcessedStripeEvents.Add(ProcessedStripeEvent.Create(evt.Id, evt.Type, handled, Now));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (!await db.ProcessedStripeEvents.AsNoTracking().AnyAsync(e => e.EventId == evt.Id, ct))
                throw;

            // Another delivery of the same event won the race; its changes are the ones that count.
            logger.LogInformation("Stripe event {EventId} was processed concurrently", evt.Id);
            return WebhookResult.Duplicate;
        }

        logger.LogInformation("Stripe event {EventId} ({Type}) {Result}", evt.Id, evt.Type, handled ? "processed" : "ignored");
        return handled ? WebhookResult.Processed : WebhookResult.Ignored;
    }

    private async Task<bool> OnCheckoutCompleted(CheckoutSessionCompleted e, CancellationToken ct)
    {
        if (e.CustomerId is not null)
            await UpsertCustomer(e.CustomerId, e.CustomerEmail, ct);

        return e.Mode switch
        {
            "payment" => await CompleteOrder(e, ct),
            "subscription" when e.SubscriptionId is not null && e.CustomerId is not null => await AttachSubscriptionCheckout(e, ct),
            _ => false
        };
    }

    private async Task<bool> CompleteOrder(CheckoutSessionCompleted e, CancellationToken ct)
    {
        var order = await FindOrder(e, ct);
        if (order is null)
        {
            logger.LogWarning("No order found for checkout session {SessionId}", e.SessionId);
            return false;
        }

        if (!e.IsPaid)
        {
            logger.LogInformation("Checkout {SessionId} completed but payment is {PaymentStatus}", e.SessionId, e.PaymentStatus);
            return false;
        }

        return order.MarkPaid(e.PaymentIntentId, e.AmountTotal ?? order.Amount, e.CustomerEmail, Now);
    }

    private async Task<bool> AttachSubscriptionCheckout(CheckoutSessionCompleted e, CancellationToken ct)
    {
        var subscription = await GetOrCreateSubscription(e.SubscriptionId!, e.CustomerId!, ct);
        e.Metadata.TryGetValue(SubscriptionManager.PlanCodeMetadataKey, out var planCode);
        subscription.AttachCheckout(e.CustomerEmail, planCode, Now);
        return true;
    }

    private async Task<bool> OnSubscriptionChanged(SubscriptionChanged e, DateTimeOffset eventCreated, CancellationToken ct)
    {
        var subscription = await GetOrCreateSubscription(e.SubscriptionId, e.CustomerId, ct);
        var planCode = e.PriceId is null
            ? null
            : await db.Plans.Where(p => p.StripePriceId == e.PriceId).Select(p => p.Code).FirstOrDefaultAsync(ct);

        var applied = subscription.Sync(new SubscriptionSnapshot(
            Subscription.ParseStatus(e.Status), e.PriceId, planCode, e.CurrentPeriodEnd, e.TrialEnd,
            e.CancelAtPeriodEnd, e.CanceledAt), eventCreated, Now);

        if (!applied)
            logger.LogInformation("Ignoring out-of-order snapshot for subscription {SubscriptionId}", e.SubscriptionId);
        return applied;
    }

    private async Task<bool> OnInvoice(InvoiceEvent e, bool paid, CancellationToken ct)
    {
        if (e.SubscriptionId is null)
            return false;

        var subscription = await db.Subscriptions.SingleOrDefaultAsync(s => s.StripeSubscriptionId == e.SubscriptionId, ct);
        if (subscription is null && e.CustomerId is not null)
            subscription = await GetOrCreateSubscription(e.SubscriptionId, e.CustomerId, ct);
        if (subscription is null)
            return false;

        if (paid)
            subscription.RecordPayment(Now);
        else
            subscription.RecordPaymentFailure(Now);
        return true;
    }

    private async Task<bool> OnChargeRefunded(ChargeRefunded e, CancellationToken ct)
    {
        if (e.PaymentIntentId is null)
            return false;

        var order = await db.Orders.SingleOrDefaultAsync(o => o.PaymentIntentId == e.PaymentIntentId, ct);
        if (order is null)
        {
            logger.LogWarning("No order found for refunded payment intent {PaymentIntentId}", e.PaymentIntentId);
            return false;
        }

        var before = order.AmountRefunded;
        order.ApplyRefundedTotal(e.AmountRefunded, Now);
        return order.AmountRefunded != before;
    }

    private async Task<bool> OnAccountUpdated(AccountUpdated e, CancellationToken ct)
    {
        var account = await db.ConnectedAccounts.SingleOrDefaultAsync(a => a.StripeAccountId == e.AccountId, ct);
        account?.UpdateCapabilities(e.ChargesEnabled, e.PayoutsEnabled, e.DetailsSubmitted, Now);
        return account is not null;
    }

    private async Task<Subscription> GetOrCreateSubscription(string subscriptionId, string customerId, CancellationToken ct)
    {
        var subscription = db.Subscriptions.Local.FirstOrDefault(s => s.StripeSubscriptionId == subscriptionId)
            ?? await db.Subscriptions.SingleOrDefaultAsync(s => s.StripeSubscriptionId == subscriptionId, ct);
        if (subscription is null)
        {
            subscription = Subscription.Create(subscriptionId, customerId, Now);
            db.Subscriptions.Add(subscription);
        }
        return subscription;
    }

    private async Task UpsertCustomer(string stripeCustomerId, string? email, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.StripeCustomerId == stripeCustomerId, ct);
        if (customer is null)
            db.Customers.Add(Customer.Create(stripeCustomerId, email, Now));
        else
            customer.UpdateEmail(email);
    }

    private async Task<Order?> FindOrder(CheckoutSessionCompleted e, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.CheckoutSessionId == e.SessionId, ct);
        if (order is null && e.Metadata.TryGetValue(CheckoutService.OrderIdMetadataKey, out var raw) && Guid.TryParse(raw, out var id))
            order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct);
        return order;
    }
}
