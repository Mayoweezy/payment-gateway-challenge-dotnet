using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Http;

/// <summary>RFC 9457 problem responses, each failure with its own "type".</summary>
public static class PaymentProblems
{
    public const string ContentType = "application/problem+json";

    /// <summary>ProblemDetails reserves "status" for the HTTP code.</summary>
    public const string PaymentStatusField = "paymentStatus";

    /// <summary>Not a <see cref="PaymentStatus"/>, because a rejected request is never stored.</summary>
    public const string RejectedPaymentStatus = "Rejected";

    public static class Types
    {
        private const string Prefix = "urn:payment-gateway:problem:";

        public const string Rejected = Prefix + "payment-rejected";
        public const string BankUnavailable = Prefix + "bank-unavailable";
        public const string BankRefused = Prefix + "bank-refused";
        public const string BankTimeout = Prefix + "bank-timeout";
        public const string BankFailed = Prefix + "bank-failed";
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

    /// <summary>Also used for malformed JSON.</summary>
    public static ObjectResult Rejected(HttpContext httpContext, ModelStateDictionary modelState)
    {
        var problem = Factory(httpContext).CreateValidationProblemDetails(
            httpContext,
            modelState,
            StatusCodes.Status400BadRequest,
            title: "The payment was rejected because the request is invalid.",
            type: Types.Rejected,
            detail: "The bank was not contacted. Correct the listed fields and try again.");
        problem.Extensions[PaymentStatusField] = RejectedPaymentStatus;

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
