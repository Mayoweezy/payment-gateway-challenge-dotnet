using System.Globalization;

using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

using static PaymentGateway.Api.Controllers.PaymentProblems;

namespace PaymentGateway.Api.Controllers;

/// <summary>Maps HTTP to the payment service and payment results back to HTTP. Holds no business logic.</summary>
[Route("api/[controller]")]
[ApiController]
public class PaymentsController : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string IdempotentReplayedHeader = "Idempotent-Replayed";
    public const string GetPaymentRouteName = "GetPayment";

    /// <summary>How long a merchant should wait before retrying after the bank was unavailable.</summary>
    public static readonly TimeSpan BankUnavailableRetryAfter = TimeSpan.FromSeconds(5);

    /// <summary>How long a merchant should wait before retrying a duplicate of an in-flight request.</summary>
    public static readonly TimeSpan InProgressRetryAfter = TimeSpan.FromSeconds(1);

    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    /// <summary>Processes a card payment through the acquiring bank.</summary>
    /// <remarks>A declined payment is still a created payment, so both outcomes return 201.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout, ContentType)]
    public async Task<IActionResult> PostPaymentAsync(
        [FromBody] PostPaymentRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Model binding turns an empty header into null. Keep it as "" so it is rejected rather than ignored.
        idempotencyKey ??= Request.Headers.ContainsKey(IdempotencyKeyHeader) ? string.Empty : null;

        var result = await _paymentService.ProcessPaymentAsync(request, idempotencyKey, cancellationToken);

        return result switch
        {
            PaymentResult.Processed processed => Created(processed),
            PaymentResult.Rejected rejected => Rejected(HttpContext, rejected.Errors),
            PaymentResult.BankUnavailable => WithRetryAfter(BankUnavailableRetryAfter, Create(HttpContext,
                StatusCodes.Status503ServiceUnavailable,
                Types.BankUnavailable,
                "The acquiring bank is unavailable.",
                "The payment was not processed. It is safe to retry later.")),
            PaymentResult.BankTimeout => Create(HttpContext,
                StatusCodes.Status504GatewayTimeout,
                Types.BankTimeout,
                "The acquiring bank did not respond in time.",
                "The payment outcome is unknown. Do not retry without reconciling first."),
            PaymentResult.BankFailed => Create(HttpContext,
                StatusCodes.Status502BadGateway,
                Types.BankFailed,
                "The call to the acquiring bank failed.",
                "The payment outcome is unknown. Do not retry without reconciling first."),
            PaymentResult.IdempotencyKeyMismatch => Create(HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                Types.IdempotencyKeyMismatch,
                "The idempotency key was already used with different payment details.",
                "Use a new idempotency key for a different payment."),
            PaymentResult.IdempotencyKeyInProgress => WithRetryAfter(InProgressRetryAfter, Create(HttpContext,
                StatusCodes.Status409Conflict,
                Types.IdempotencyKeyInProgress,
                "A request with this idempotency key is still being processed.",
                "Retry shortly with the same idempotency key to get the result.")),
            PaymentResult.IdempotencyKeyOutcomeUnknown => Create(HttpContext,
                StatusCodes.Status409Conflict,
                Types.IdempotencyKeyOutcomeUnknown,
                "A previous request with this idempotency key has an unknown outcome.",
                "Retrying will not help. Reconcile the payment before trying again."),
            _ => throw new InvalidOperationException($"Unhandled payment result {result.GetType().Name}.")
        };
    }

    /// <summary>Retrieves a previously processed payment.</summary>
    [HttpGet("{id:guid}", Name = GetPaymentRouteName)]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, ContentType)]
    public ActionResult<PaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentService.GetPayment(id);

        return payment is null ? NotFound() : Ok(PaymentResponse.FromPayment(payment));
    }

    private CreatedAtRouteResult Created(PaymentResult.Processed processed)
    {
        if (processed.IsReplay)
        {
            Response.Headers[IdempotentReplayedHeader] = "true";
        }

        return CreatedAtRoute(GetPaymentRouteName, new { id = processed.Payment.Id }, PaymentResponse.FromPayment(processed.Payment));
    }

    private ObjectResult WithRetryAfter(TimeSpan delay, ObjectResult result)
    {
        Response.Headers.RetryAfter = ((int)delay.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        return result;
    }
}
