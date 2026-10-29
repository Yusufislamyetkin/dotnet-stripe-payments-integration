using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Checkout;
using StripePayments.Domain.Orders;
using StripePayments.Domain.Webhooks;

namespace StripePayments.Application.Webhooks;

public enum WebhookResult { Processed, Duplicate, Ignored }

/// <summary>
/// Applies verified Stripe events to local state. The state change and the processed-event marker are
/// written in one SaveChanges call (one transaction), which makes every handler idempotent.
/// </summary>
public sealed class WebhookProcessor(IAppDbContext db, TimeProvider clock, ILogger<WebhookProcessor> logger)
{
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
            _ => false
        };

        db.ProcessedStripeEvents.Add(ProcessedStripeEvent.Create(evt.Id, evt.Type, handled, clock.GetUtcNow()));
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
        if (e.Mode != "payment")
            return false;

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

        return order.MarkPaid(e.PaymentIntentId, e.AmountTotal ?? order.Amount, e.CustomerEmail, clock.GetUtcNow());
    }

    private async Task<Order?> FindOrder(CheckoutSessionCompleted e, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.CheckoutSessionId == e.SessionId, ct);
        if (order is null && e.Metadata.TryGetValue(CheckoutService.OrderIdMetadataKey, out var raw) && Guid.TryParse(raw, out var id))
            order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct);
        return order;
    }
}
