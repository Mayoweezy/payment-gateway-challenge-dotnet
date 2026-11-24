using System.Globalization;
using System.Net;
using System.Text.Json;

using PaymentGateway.Api.Bank.Requests;
using PaymentGateway.Api.Bank.Responses;
using PaymentGateway.Api.Models.Requests;

using Polly.CircuitBreaker;
using Polly.Timeout;

namespace PaymentGateway.Api.Bank;

/// <summary>
/// Translates between the gateway and the acquiring bank's wire format. Never logs request or response bodies,
/// because the request contains the full card number and CVV.
/// </summary>
public sealed class BankClient : IBankClient
{
    public const string PaymentsPath = "payments";

    private readonly HttpClient _httpClient;
    private readonly ILogger<BankClient> _logger;

    public BankClient(HttpClient httpClient, ILogger<BankClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BankResult> AuthorizeAsync(PostPaymentRequest request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(PaymentsPath, ToBankRequest(request), cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Bank call short-circuited because the circuit is open");
            return new BankResult.Unavailable();
        }
        catch (TimeoutRejectedException)
        {
            _logger.LogWarning("Bank call timed out");
            return new BankResult.Timeout();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout surfaces as a cancellation the caller did not ask for.
            _logger.LogWarning("Bank call timed out");
            return new BankResult.Timeout();
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError is
            HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError)
        {
            // No connection was made, so the bank definitely did not process the payment.
            _logger.LogWarning("Bank could not be reached: {Error}", exception.HttpRequestError);
            return new BankResult.Unavailable();
        }
        catch (HttpRequestException exception)
        {
            // The request may have reached the bank before the connection failed.
            _logger.LogWarning("Bank call failed with a network error: {Error}", exception.HttpRequestError);
            return new BankResult.Failed("network error");
        }

        using (response)
        {
            return await InterpretAsync(response, cancellationToken);
        }
    }

    private async Task<BankResult> InterpretAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogWarning("Bank returned 503 Service Unavailable");
            return new BankResult.Unavailable();
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Bank returned unexpected status {StatusCode}", (int)response.StatusCode);
            return new BankResult.Failed($"unexpected status {(int)response.StatusCode}");
        }

        BankPaymentResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            _logger.LogWarning("Bank returned a response that is not valid JSON");
            return new BankResult.Failed("malformed response");
        }

        switch (body?.Authorized)
        {
            case true when !string.IsNullOrWhiteSpace(body.AuthorizationCode):
                return new BankResult.Authorized(body.AuthorizationCode);
            case false:
                return new BankResult.Declined();
            default:
                _logger.LogWarning("Bank returned a response without a valid authorization result");
                return new BankResult.Failed("malformed response");
        }
    }

    private static BankPaymentRequest ToBankRequest(PostPaymentRequest request) => new()
    {
        CardNumber = request.CardNumber!,
        ExpiryDate = string.Create(CultureInfo.InvariantCulture, $"{request.ExpiryMonth!.Value:00}/{request.ExpiryYear!.Value:0000}"),
        Currency = request.Currency!,
        Amount = request.Amount!.Value,
        Cvv = request.Cvv!
    };
}
