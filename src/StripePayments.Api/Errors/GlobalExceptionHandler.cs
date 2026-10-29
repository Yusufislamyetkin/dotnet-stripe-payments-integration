using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StripePayments.Application.Common;
using StripePayments.Domain.Common;

namespace StripePayments.Api.Errors;

/// <summary>Maps exceptions to RFC 7807 ProblemDetails responses.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
            InvalidWebhookSignatureException => (StatusCodes.Status400BadRequest, "Invalid webhook signature"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ConflictException or DomainException => (StatusCodes.Status409Conflict, "Conflict"),
            PaymentProviderException => (StatusCodes.Status502BadGateway, "Payment provider error"),
            StripeNotConfiguredException => (StatusCodes.Status503ServiceUnavailable, "Stripe is not configured"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Request failed with {Status}", status);
        else
            logger.LogWarning("Request failed with {Status}: {Message}", status, exception.Message);

        var problem = exception is ValidationException ve
            ? new ValidationProblemDetails(ve.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        problem.Detail = status == StatusCodes.Status500InternalServerError || exception is ValidationException
            ? null
            : exception.Message;

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
