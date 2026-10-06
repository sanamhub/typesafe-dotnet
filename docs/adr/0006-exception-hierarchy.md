# ADR-0006: Exception hierarchy

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06

## Context

Both official SDKs map HTTP failures to one class per status and separate transport failures
from HTTP failures. Python also has a response validation error for a 2xx body that is missing
required data, with a dotted field path.

Callers need three decisions from an exception: retry later (429, 529, timeout), fix the request
(400, 422), or fix configuration (401, 403). They also need `x-typesafe-request-id` to open a
support ticket.

## Decision

```
TypeSafeException                               base, never thrown directly
  TypeSafeApiException                          any non-2xx after retries
      StatusCode, RequestId, Body (string), Headers, Endpoint ("POST /v1/systemone")
    TypeSafeBadRequestException                 400
    TypeSafeAuthenticationException             401
    TypeSafePermissionDeniedException           403
    TypeSafeNotFoundException                   404
    TypeSafeUnprocessableEntityException        422, adds ValidationErrors
    TypeSafeRateLimitException                  429, adds RetryAfter (TimeSpan?)
    TypeSafeInternalServerException             5xx
      TypeSafeOverloadedException               529
  TypeSafeConnectionException                   no HTTP response (DNS, TLS, reset)
    TypeSafeTimeoutException                    attempt timeout or total budget, adds Timeout
  TypeSafeResponseValidationException           2xx body not JSON, missing or malformed, adds JsonPath
  TypeSafeConfigurationException                bad API key or base URL
```

`Headers` holds the response headers as `IReadOnlyDictionary<string, IReadOnlyList<string>>`,
never `HttpResponseHeaders`, which would put System.Net.Http in a public signature.
`ValidationErrors` is a list of `ValidationError`, a sealed class with `Location`
(`IReadOnlyList<string>`), `Message` and `Type` only.

`TypeSafeResponseValidationException` covers any 2xx body the reader cannot use: a gateway HTML
page, an empty body, `[]`, or a wrong type such as `"input_tokens": 1.5`. `JsonPath` is `$` when
the body is not a JSON object, and the field path otherwise. An empty Score `probabilities` map
is a validation error too.

Rules:

1. **Exception text never carries customer data.** `Message` is built from the body's `error`,
   `message` or `detail` string fields, or from FastAPI `detail[]` entries as `loc` and `msg`
   pairs joined by `"; "`, with every `body` segment dropped from `loc`. The `input` and `ctx`
   fields are never read. Example:
   `"422 questions.urgency.score.criteria: List should have at least 1 item"`.

   A JSON body with none of those fields gives `"<status> status code (no message in body)"`,
   never the raw JSON. A non-JSON body, such as a proxy error page, is included, capped at 200
   characters. Both official SDKs cap only this raw fallback at 200 too. `Body` stays on the
   exception, and its XML doc says it may contain request data and must not be logged.
2. **The API key never appears in a message, `ToString()`, `Data`, or `Headers`.** The
   `Authorization` header is not stored. Python 0.7.1 shipped exactly this fix.
3. **Client-side validation throws `ArgumentException` family**, not `TypeSafeException`. An empty
   question map or a Score with fewer than two levels is a programming error, and .NET callers
   expect `ArgumentException` for that. Bad `RetryPolicy` values and other numeric option ranges
   throw `ArgumentOutOfRangeException` (PLAN.md section 3 has the full list).
4. **Caller cancellation throws `OperationCanceledException`** (ADR-0005). There is no
   `UserAbortException`; `OperationCanceledException` already covers it.
5. **Exceptions are not `[Serializable]`.** BinaryFormatter is gone from modern .NET and the
   pattern is obsolete (SYSLIB0051).
6. **Tests can build any of these.** `TypeSafeModelFactory.ApiException(int statusCode, string?
   message = null, string? requestId = null, TimeSpan? retryAfter = null)` returns the subclass
   that matches the status code.
7. **Batches stop on account-wide failures.** `EvaluateManyAsync` ends the enumeration by
   throwing `TypeSafeAuthenticationException`, `TypeSafePermissionDeniedException`,
   `TypeSafeNotFoundException` or `TypeSafeConfigurationException`. Any other `TypeSafeException`
   or `ArgumentException` is yielded as a failed item (ADR-0007).

## Consequences

`catch (TypeSafeRateLimitException ex) when (ex.RetryAfter is { } wait)` reads naturally, and
`catch (TypeSafeApiException ex)` logs `ex.RequestId` for every HTTP failure.

The names are longer than the JS names (`RateLimitError`). The `TypeSafe` prefix avoids clashing
with `ApiException` types that most apps already have from Refit, NSwag, or their own code.

## Alternatives considered

**One `TypeSafeApiException` with a `StatusCode` switch.** Rejected. Pattern matching on type is
the .NET idiom, and a 529-specific type is useful.

**Mirror the JS names exactly (`APIError`, `RateLimitError`).** Rejected. `Error` suffixes break
.NET naming guidelines and analyzer rule CA1710.
