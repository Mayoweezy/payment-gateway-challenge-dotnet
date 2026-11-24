using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Controllers;

/// <summary>
/// Builds the RFC 9457 problem responses for every payment failure, so clients can tell them apart by "type".
/// </summary>
public static class PaymentProblems
{
    public const string ContentType = "application/problem+json";

    /// <summary>
    /// ProblemDetails reserves "status" for the HTTP status code, so the payment status goes in this field.
    /// </summary>
    public const string PaymentStatusField = "paymentStatus";

    public static class Types
    {
        private const string Prefix = "urn:payment-gateway:problem:";

        public const string Rejected = Prefix + "payment-rejected";
        public const string BankUnavailable = Prefix + "bank-unavailable";
        public const string BankTimeout = Prefix + "bank-timeout";
        public const string BankFailed = Prefix + "bank-failed";
        public const string IdempotencyKeyMismatch = Prefix + "idempotency-key-mismatch";
        public const string IdempotencyKeyInProgress = Prefix + "idempotency-key-in-progress";
        public const string IdempotencyKeyOutcomeUnknown = Prefix + "idempotency-key-outcome-unknown";
    }

    public static ObjectResult Rejected(HttpContext httpContext, IReadOnlyDictionary<string, string[]> errors)
    {
        var modelState = new ModelStateDictionary();
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                modelState.AddModelError(field, message);
            }
        }

        return Rejected(httpContext, modelState);
    }

    /// <summary>Also used for malformed JSON, so every invalid request gets the same response shape.</summary>
    public static ObjectResult Rejected(HttpContext httpContext, ModelStateDictionary modelState)
    {
        var problem = Factory(httpContext).CreateValidationProblemDetails(
            httpContext,
            modelState,
            StatusCodes.Status400BadRequest,
            title: "The payment was rejected because the request is invalid.",
            type: Types.Rejected,
            detail: "The bank was not contacted. Correct the listed fields and try again.");
        problem.Extensions[PaymentStatusField] = nameof(PaymentStatus.Rejected);

        return ToResult(problem);
    }

    public static ObjectResult Create(HttpContext httpContext, int statusCode, string type, string title, string detail) =>
        ToResult(Factory(httpContext).CreateProblemDetails(httpContext, statusCode, title, type, detail));

    private static ProblemDetailsFactory Factory(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();

    private static ObjectResult ToResult(ProblemDetails problem)
    {
        var result = new ObjectResult(problem) { StatusCode = problem.Status };
        result.ContentTypes.Add(ContentType);
        return result;
    }
}
