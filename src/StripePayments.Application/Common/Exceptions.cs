namespace StripePayments.Application.Common;

public abstract class AppException(string message) : Exception(message);

public sealed class NotFoundException(string resource, object key)
    : AppException($"{resource} '{key}' was not found.");

public sealed class ConflictException(string message) : AppException(message);

public sealed class StripeNotConfiguredException()
    : AppException("Stripe is not configured. Set Stripe__SecretKey to a TEST key (sk_test_...) via `dotnet user-secrets` or the .env file. See README > Quick start.");

public sealed class InvalidWebhookSignatureException(string message) : AppException(message);

/// <summary>Raised when Stripe rejects a request; wraps Stripe.net exceptions at the infrastructure boundary.</summary>
public sealed class PaymentProviderException(string message, Exception? inner = null) : AppException(message)
{
    public Exception? Provider { get; } = inner;
}
