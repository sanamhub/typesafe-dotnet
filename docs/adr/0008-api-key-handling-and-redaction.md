# ADR-0008: API key handling, validation, and redaction

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06
- **Relates to:** org standard sections 1 and 6 (no secrets in output, OWASP ASVS)

## Context

The API authenticates with `Authorization: Bearer <key>`. Keys come from the constructor, from
options bound to configuration, or from `TYPESAFE_API_KEY`.

Python 0.7.1 (2026-09-21) fixed two things: validate the key early, and keep its value out of
logged exceptions. Its rules: strip leading and trailing whitespace (including the newline a
key file leaves behind), reject empty keys, internal whitespace, control characters and
non-ASCII, and do not fall back to the environment when a key was passed explicitly as empty.

`TypeSafe.AI.Sdk` does none of this. An empty key sends `Authorization: Bearer `, and a key with
an embedded newline is added with `TryAddWithoutValidation`, fails inside `HttpClient`, and is
then retried as a connection error.

## Decision

1. **Resolve once, at construction.** Explicit option, then `TYPESAFE_API_KEY`. An explicit empty
   string is an error, not a fallback, matching Python. The DI package applies the same rules to
   `ApiKey ?? TYPESAFE_API_KEY` at host start, so a host that sets only the environment variable
   is checked too (ADR-0009 rule 2).
2. **Validate with Python's rules** and throw `TypeSafeConfigurationException` with a message
   that names the rule and never echoes the value, for example
   `"The API key contains whitespace inside it. Check for a pasted line break."`.
3. **Hold the key in one private field of an internal settings object.** It is not exposed by
   any public property, not included in `ToString()` of options or client, and not copied into
   exceptions. `TypeSafeClientOptions.ToString()` prints `ApiKey = ***` when set.
4. **Headers and bodies are never logged,** at any level. Headers carry the key, and `state`
   routinely carries customer data (org standard section 7 forbids real PII in test data, and
   the same caution applies to logs). With no headers in the logs, there is no redaction list
   that could miss one.
5. **Telemetry never records `state`, `instructions`, or `criteria`.** It records only the
   attributes ADR-0010 lists: provider and operation name, model names, token counts, server
   address and port, status code, error type, and request id.
6. **Exception text carries no customer data.** `Message` is built from the error fields only,
   as ADR-0006 rule 1 describes. `TypeSafeApiException.Body` stays available, and its XML doc
   says it may contain request data and must not be logged.
7. **HTTPS only by default.** A `BaseUrl` with `http://` throws unless the host is `localhost`,
   `127.0.0.1`, or `::1`, so a typo cannot send the key in clear text. The gateway base URLs in
   PLAN.md section 1.6 are all HTTPS.
8. **TLS.** The SDK does not set `ServicePointManager` or handler protocol flags. On .NET
   Framework, whether TLS 1.2 is on by default depends on the version the app targets and runs
   on: an app targeting 4.7 or later uses the OS defaults. The SDK supports 4.7.2 and later
   (ADR-0001).
9. **Server-side only.** `[assembly: UnsupportedOSPlatform("browser")]` gives Blazor WebAssembly
   consumers a CA1416 warning, and the client constructor throws
   `TypeSafeConfigurationException("TypeSafeClient runs server-side only; a browser app would expose the API key.")`
   when it runs in a browser. This matches the JS SDK's `refuseBrowser`.

## Consequences

A bad key fails at startup with a readable message instead of during the first request with a
confusing one. Nothing the SDK writes (exceptions, logs, spans) can carry the key.

Not logging headers or bodies makes some debugging harder. Callers who need the bytes can attach their own
`DelegatingHandler`, which is their decision about their data.

## Alternatives considered

**Log bodies at `Debug`, as both official SDKs do (unredacted).** Rejected for the data reason
above. A logging sink shipped to a third party is the usual way customer data leaks.

**`SecureString` for the key.** Rejected. Deprecated for new code by Microsoft, and the value has
to become a `string` for the header anyway.
