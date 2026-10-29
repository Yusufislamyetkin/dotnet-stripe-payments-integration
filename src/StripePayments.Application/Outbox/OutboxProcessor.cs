using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StripePayments.Application.Abstractions;
using StripePayments.Domain.Orders;
using StripePayments.Domain.Outbox;

namespace StripePayments.Application.Outbox;

public interface IOutboxPublisher
{
    bool IsConfigured { get; }

    /// <returns><c>null</c> on success, otherwise the failure reason.</returns>
    Task<string?> PublishAsync(OutboxMessage message, CancellationToken ct);
}

public sealed record OutboxMessageDto(Guid Id, string Type, int Attempts, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt, DateTimeOffset? DeliveredAt, string? LastError);

public static class IntegrationEvents
{
    public const string OrderPaid = "order.paid";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static OutboxMessage OrderPaidMessage(Order order, DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            id,
            type = OrderPaid,
            createdAt = now,
            data = new
            {
                orderId = order.Id,
                order.ProductSku,
                order.ProductName,
                order.Amount,
                order.Currency,
                order.CustomerEmail,
                order.PromotionCode,
                order.PaidAt,
                order.ConnectedAccountId,
                order.ApplicationFeeAmount
            }
        }, Json);
        return OutboxMessage.Create(id, OrderPaid, payload, now);
    }
}

/// <summary>Delivers due outbox messages with exponential backoff; called periodically by the dispatcher.</summary>
public sealed class OutboxProcessor(
    IAppDbContext db,
    IOutboxPublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxProcessor> logger)
{
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        if (!publisher.IsConfigured)
            return 0;

        var o = options.Value;
        var now = clock.GetUtcNow();
        var due = await db.OutboxMessages
            .Where(m => m.DeliveredAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(o.BatchSize)
            .ToListAsync(ct);

        var delivered = 0;
        foreach (var message in due)
        {
            string? error;
            try
            {
                error = await publisher.PublishAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                error = ex.Message;
            }

            now = clock.GetUtcNow();
            if (error is null)
            {
                message.MarkDelivered(now);
                delivered++;
            }
            else
            {
                var attempt = message.Attempts + 1;
                DateTimeOffset? retryAt = attempt >= o.MaxAttempts
                    ? null
                    : now + RetryPolicy.Delay(attempt, TimeSpan.FromSeconds(o.BaseDelaySeconds), TimeSpan.FromSeconds(o.MaxDelaySeconds));
                message.MarkFailed(error, now, retryAt);

                if (retryAt is null)
                    logger.LogError("Outbox message {MessageId} dead-lettered after {Attempts} attempts: {Error}", message.Id, attempt, error);
                else
                    logger.LogWarning("Outbox message {MessageId} attempt {Attempt} failed, retrying at {RetryAt}: {Error}", message.Id, attempt, retryAt, error);
            }

            // Saved per message so a crash mid-batch does not re-send already delivered messages.
            await db.SaveChangesAsync(ct);
        }

        return delivered;
    }

    public async Task<IReadOnlyList<OutboxMessageDto>> ListAsync(CancellationToken ct) =>
        (await db.OutboxMessages.AsNoTracking().OrderByDescending(m => m.CreatedAt).Take(100).ToListAsync(ct))
        .Select(m => new OutboxMessageDto(m.Id, m.Type, m.Attempts,
            m.DeliveredAt is not null ? "Delivered" : m.DeadLetteredAt is not null ? "DeadLettered" : "Pending",
            m.CreatedAt, m.NextAttemptAt, m.DeliveredAt, m.LastError))
        .ToList();
}
