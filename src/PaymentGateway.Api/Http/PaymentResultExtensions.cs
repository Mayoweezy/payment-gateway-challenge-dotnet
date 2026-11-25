using System.Globalization;

using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

using static PaymentGateway.Api.Http.PaymentProblems;

namespace PaymentGateway.Api.Http;

/// <summary>Maps each payment result to its HTTP response.</summary>
public static class PaymentResultExtensions
{
    public static readonly TimeSpan BankUnavailableRetryAfter = TimeSpan.FromSeconds(5);

    public static IActionResult ToActionResult(this PaymentResult result, HttpContext httpContext) => result switch
    {
        PaymentResult.Processed processed => new CreatedAtRouteResult(
            PaymentsController.GetPaymentRouteName,
            new { id = processed.Payment.Id },
            PaymentResponse.FromPayment(processed.Payment)),
        PaymentResult.Rejected rejected => Rejected(httpContext, rejected.Errors),
        PaymentResult.BankUnavailable => WithRetryAfter(httpContext, BankUnavailableRetryAfter, Create(httpContext,
            StatusCodes.Status503ServiceUnavailable,
            Types.BankUnavailable,
            "The acquiring bank is unavailable.",
            "The payment was not processed. It is safe to retry later.")),
        PaymentResult.BankRefused => Create(httpContext,
            StatusCodes.Status502BadGateway,
            Types.BankRefused,
            "The acquiring bank refused the request.",
            "The payment was not processed. This is a gateway error, so retrying the same request will not help."),
        PaymentResult.BankTimeout => Create(httpContext,
            StatusCodes.Status504GatewayTimeout,
            Types.BankTimeout,
            "The acquiring bank did not respond in time.",
            "The payment outcome is unknown. Do not retry without reconciling first."),
        PaymentResult.BankFailed => Create(httpContext,
            StatusCodes.Status502BadGateway,
            Types.BankFailed,
            "The call to the acquiring bank failed.",
            "The payment outcome is unknown. Do not retry without reconciling first."),
        _ => throw new InvalidOperationException($"Unhandled payment result {result.GetType().Name}.")
    };

    private static ObjectResult WithRetryAfter(HttpContext httpContext, TimeSpan delay, ObjectResult result)
    {
        httpContext.Response.Headers.RetryAfter = ((int)delay.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        return result;
    }
}
