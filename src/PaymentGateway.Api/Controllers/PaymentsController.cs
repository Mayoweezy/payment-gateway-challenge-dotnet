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
    public const string GetPaymentRouteName = "GetPayment";

    /// <summary>How long a merchant should wait before retrying after the bank was unavailable.</summary>
    public static readonly TimeSpan BankUnavailableRetryAfter = TimeSpan.FromSeconds(5);

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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout, ContentType)]
    public async Task<IActionResult> PostPaymentAsync([FromBody] PostPaymentRequest request)
    {
        // No cancellation token on purpose: a merchant disconnecting must not abort a call the bank may complete.
        var result = await _paymentService.ProcessPaymentAsync(request);

        return result switch
        {
            PaymentResult.Processed processed => CreatedAtRoute(
                GetPaymentRouteName, new { id = processed.Payment.Id }, PaymentResponse.FromPayment(processed.Payment)),
            PaymentResult.Rejected rejected => Rejected(HttpContext, rejected.Errors),
            PaymentResult.BankUnavailable => WithRetryAfter(BankUnavailableRetryAfter, Create(HttpContext,
                StatusCodes.Status503ServiceUnavailable,
                Types.BankUnavailable,
                "The acquiring bank is unavailable.",
                "The payment was not processed. It is safe to retry later.")),
            PaymentResult.BankRefused => Create(HttpContext,
                StatusCodes.Status502BadGateway,
                Types.BankRefused,
                "The acquiring bank refused the request.",
                "The payment was not processed. This is a gateway error, so retrying the same request will not help."),
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

    private ObjectResult WithRetryAfter(TimeSpan delay, ObjectResult result)
    {
        Response.Headers.RetryAfter = ((int)delay.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        return result;
    }
}
