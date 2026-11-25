# Payment Gateway

An ASP.NET Core 8 API that lets a merchant process a card payment through an acquiring bank and retrieve it later. The bank is the Mountebank simulator in `imposters/`.

The assessment brief is on the [cko-recruitment](https://github.com/cko-recruitment/) page.

## Running it

Prerequisites: the .NET 8 SDK (or a later SDK plus the ASP.NET Core 8 runtime), and Docker for the bank simulator.

```bash
docker-compose up -d                          # bank simulator on http://localhost:8080
dotnet run --project src/PaymentGateway.Api   # Swagger UI opens at /swagger
```

The simulator decides the outcome from the last digit of the card number:

| Last digit | Result |
|---|---|
| 1, 3, 5, 7, 9 | Authorized |
| 2, 4, 6, 8 | Declined |
| 0 | Bank returns 503 |

## Testing

```bash
dotnet test test/PaymentGateway.Api.Tests                                                          # unit
dotnet test test/PaymentGateway.Api.Integration.Tests --filter "Category!=RequiresDocker"          # integration, no Docker
dotnet test test/PaymentGateway.Api.Integration.Tests --filter "Category=RequiresDocker"           # against the running simulator
```

| Project | What it covers |
|---|---|
| `PaymentGateway.Api.Tests` | Validation rules and boundaries, `PaymentService` orchestration, the bank's wire contract and error mapping, and the thread safety of the repository |
| `PaymentGateway.Api.Integration.Tests` | The real app through `WebApplicationFactory` with a mocked bank: every HTTP status, header and body. The real Polly pipeline over a stub HTTP handler. Smoke tests against the simulator |

The tests were written first, against `NotImplementedException` stubs, and then made to pass.

## API

### `POST /api/payments`

```json
{
  "cardNumber": "2222405343248877",
  "expiryMonth": 4,
  "expiryYear": 2027,
  "currency": "GBP",
  "amount": 100,
  "cvv": "123"
}
```

| Outcome | Status | Body |
|---|---|---|
| Authorized or declined | `201` + `Location` | Payment |
| Invalid request | `400` | Validation problem with `paymentStatus: "Rejected"` |
| Bank unavailable or circuit open | `503` + `Retry-After: 5` | Problem |
| Bank timed out | `504` | Problem |
| Bank call failed | `502` | Problem |
| Unexpected server error | `500` | Problem |

### `GET /api/payments/{id}`

Returns `200` with the payment, or `404` with a problem body.

A payment looks like this:

```json
{
  "id": "3c3b6b8e-5f1e-4b8e-9b0a-1f2d3c4b5a69",
  "status": "Authorized",
  "cardNumberLastFour": "8877",
  "expiryMonth": 4,
  "expiryYear": 2027,
  "currency": "GBP",
  "amount": 100
}
```

Every error is an RFC 9457 `application/problem+json` body, including unhandled exceptions and route mismatches such as a non-GUID id. Each payment failure has its own `type` (for example `urn:payment-gateway:problem:bank-timeout`), so clients can branch on it without parsing messages.

## Design

```
PaymentsController             HTTP mapping only
  -> IPaymentService           validation, orchestration, logging
       -> IValidator<PostPaymentRequest>   FluentValidation
       -> IBankClient                      typed HttpClient
            -> Polly: circuit breaker -> timeout (no retries)
       -> IPaymentsRepository              in-memory
```

```
src/PaymentGateway.Api/
  Controllers/   PaymentsController, PaymentProblems (problem responses)
  Services/      IPaymentService, PaymentService, PaymentResult
  Bank/          IBankClient, BankClient, BankResult, options, DI registration
  Bank/Requests, Bank/Responses   the bank's wire format
  Data/          payment repository (the brief's test double)
  Validation/    PostPaymentRequestValidator
  Models/        Payment, PaymentStatus, API request and response
```

`PaymentService` returns a closed set of `PaymentResult` cases, and the controller maps each to HTTP. Business logic never deals with status codes, and the controller never deals with business rules.

## Key decisions

**Card data never leaks.** Only the last four digits are stored. The full card number and CVV are never stored, returned or logged. Validation messages never include the submitted value, and malformed JSON is not echoed back (`AllowInputFormatterExceptionMessages = false`). Tests check responses and logs for card data.

**Rejected is a 400, not a stored payment.** The brief lists `Rejected` as an outcome but not as a stored status, and the bank is never contacted. ProblemDetails reserves `status` for the HTTP code, so the payment status is in a `paymentStatus` field. Malformed JSON gets the same response shape as a failed validation.

**A decline is a `201`.** The payment was created and processed. The bank declining it is a business outcome, not a client error.

**No retries, but a circuit breaker.** The bank has no idempotency support, so retrying after a timeout could charge the shopper twice. The circuit breaker (outer) and timeout (inner, so timeouts count as failures) stop a struggling bank from tying up requests. Bank 400s don't count as failures, because they indicate a bad request, not an unhealthy bank. With the defaults in `appsettings.json`, the circuit opens when at least 10 calls in 30 seconds include 50% failures, and stays open for 15 seconds. Simulator caveat: cards ending in `0` always return 503, so enough of them open the circuit for every card.

**Failures say whether a retry is safe.** When the bank definitely didn't process the payment (it returned 503, the connection was refused, or the circuit is open), the response is a `503` with `Retry-After`. When the outcome is unknown (timeout, a dropped connection, an unexpected response), it's a `504` or `502` with no `Retry-After`. The payment may have gone through, so the merchant must reconcile.

**A merchant disconnecting doesn't cancel the bank call.** The bank may authorize a payment after the merchant drops the connection, and the gateway must still record it. So the request's cancellation token is deliberately not passed to the bank; the bank call is bounded by its own timeout instead.

**Interfaces at boundaries, real collaborators in tests.** Every service has an interface for consistency and DI. Service tests use the real validator and stores, and mock only the bank. The clock is injected with `TimeProvider`, so expiry rules are tested against a fixed date.

## Core brief vs. beyond it

The brief asks for processing (Authorized, Declined, Rejected), retrieval, the listed validation, tests and documented assumptions. Everything else is extra and could be removed without breaking the brief:

- **Beyond the brief:** the circuit breaker and timeout, telling "safe to retry" (`503`) apart from "outcome unknown" (`502`/`504`), and the per-case problem `type`s. I kept them because a payment gateway that retries blindly or hangs on a slow bank is wrong in ways that matter more than the extra code.
- **Deliberately left out: idempotency keys.** An early version supported an `Idempotency-Key` header, with replay, mismatch detection and a lock after an unknown outcome. It was the most complex part of the code and the brief doesn't ask for it, so I removed it to keep the solution simple. It's the first thing I'd add for production (see below).

## Assumptions

- **Expiry:** a card is valid until the end of its expiry month, in UTC. Years must have four digits.
- **Currencies:** only `GBP`, `USD` and `EUR` are supported (the brief allows up to three), uppercase only.
- **Amount:** a positive `int` in minor units. Zero is rejected.
- **Card number:** ASCII digits only. `char.IsDigit` would accept other numerals, such as Arabic-Indic digits. No Luhn check, because the brief doesn't ask for one and the simulator's test cards don't need it.
- **Identifiers:** GUIDs, as the brief suggests.
- **Storage:** in memory, as the brief allows. Data is lost on restart.

## Not done, and what production would need

- **Merchant authentication and isolation.** This is the biggest gap: any caller can read any payment.
- **Idempotency keys**, scoped per merchant and stored durably with an expiry (for example 24 hours), so a merchant can retry safely after a network failure without double charging.
- **Durable, shared storage.** The current repository is per process.
- **A reconciliation flow** for unknown outcomes. Today a `502` or `504` gives the merchant no payment id to reconcile with. The fix is to store the payment as `Pending` with an id before calling the bank, return that id on a `502`/`504`, and have a job query the bank to settle it.
- **PCI DSS scope reduction** through tokenization, so the gateway never handles raw card numbers.
- **Observability:** metrics, distributed tracing, and alerting on circuit state.
- **Rate limiting** and request size limits.
- **Several acquiring banks.** Each bank has its own wire format, so these would justify per-bank folders behind `IBankClient`.
