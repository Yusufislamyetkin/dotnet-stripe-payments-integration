namespace StripePayments.Application.Common;

public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>Public URL of this app; used for Checkout success/cancel redirects.</summary>
    public string PublicBaseUrl { get; init; } = "http://localhost:8080";
}
