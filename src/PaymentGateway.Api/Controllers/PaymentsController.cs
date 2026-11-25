using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Http;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

using static PaymentGateway.Api.Http.PaymentProblems;

namespace PaymentGateway.Api.Controllers;

/// <summary>Maps HTTP to the payment service. No business logic.</summary>
[Route("api/[controller]")]
[ApiController]
public class PaymentsController : ControllerBase
{
    public const string GetPaymentRouteName = "GetPayment";

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
        var result = await _paymentService.ProcessPaymentAsync(request);

        return result.ToActionResult(HttpContext);
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
}
