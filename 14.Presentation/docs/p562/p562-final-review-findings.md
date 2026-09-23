# P-562 — final review findings and remediation decisions (2026-09-24)

Three read-only reviews of the merged branch (`9cdfe39b`: waves 1–4, all suites green) with probe projects:
correctness (C1–C22: 9 Medium, 13 Low), security (S1–S16: 4 Medium, 12 Low, none Critical/High) and developer
experience (D1–D25: 4 High, 11 Medium, 10 Low). Full reports stayed in the session scratchpad; this file records every
decision. IDs below prefixed R are the remediation items. "Follow-up" items are outside this pass's approved scope
and need an owner decision.

Confirmed sound by the reviews:
- Authorization fails closed everywhere:
  - An unmapped principal is refused with 403.
  - A pipeline without `UseAuthorization` throws.
  - Policy names cannot be forged, and the attributes' `Policy`/`Roles` cannot be overridden.
- Server-error text is redacted on every protocol.
- Log templates carry no secrets.
- Header inputs are validated.
- Default limits hold, and third-party dependencies are confined to the OpenApi and gRPC packages.
- `ErrorHttpResult.Error` is easy to assert in unit tests.

## Remediation — WebApi core (stream A)

| R | Source | Decision |
| --- | --- | --- |
| R1 | S2, C7 | Pipeline order becomes routing → CORS → authentication → **rate limiter** → authorization, so refused traffic is counted. |
| R2 | C8 | `UseSharedKernelWebApi(Action<WebApiPipeline>? configure)` with ordered hooks: `AtStart` (forwarded headers), `BeforeAuthentication` (certificate forwarding), `BeforeAuthorization` (request localization, so 401/403/429 texts are translated). |
| R3 | S1 | `TrustInboundBaggage` (default `false`): the edge removes every inbound W3C baggage item before the correlation id is written. Services behind a sanitizing gateway may opt in. |
| R4 | C4, S9 | The platform policy provider and result handler **decorate** whatever was registered before them, and a startup check fails fast when a later registration replaced them (a platform policy name must still resolve to the platform requirement). |
| R5 | C5 | The platform exception logic becomes the fallback `ExceptionHandlerOptions.ExceptionHandler`, so a service's own `IExceptionHandler` runs first. Diagnostics are suppressed only for the platform's own handling (it logs by category). |
| R6 | D1, C6 | One middleware enforces idempotency-key and If-Match requirements from endpoint metadata (after authorization). It covers minimal APIs (attribute or convention), MVC and parameter binding. The endpoint and action filters are removed. Codes are unified (`idempotency.key_required`, `idempotency.key_invalid`). |
| R7 | C3, D12 | 412 is decided from the **error**. When the request carries `If-Match`/`If-None-Match` and the failure is a `Conflict` whose code is in `Problems.PreconditionFailedErrorCodes` (default `persistence.concurrency_conflict`, `storage.precondition_failed`, `storage.already_exists`), the answer is 412. Every other conflict stays 409. A 00.Governance test pins the defaults to the owning constants. |
| R8 | C3 | Required If-Match: missing or `*` → 428 `precondition.required` (a specific tag is required); malformed or more than one tag → 400 `precondition.invalid`; a weak tag → 412 `precondition.failed` (strong comparison, RFC 9110 §13.1.1). `GetIfMatchTags()` exposes every listed tag. |
| R9 | D13, C9, C-low | MVC `[ApiController]` model-state 400s and minimal-API binding/JSON failures use the platform shape (`errors`/`errorCodes`, no .NET type names). A thrown single-error `ValidationException` and a returned `Error.Validation` produce identical bodies. |
| R10 | C2, C13 | Any exception while `RequestAborted` is cancelled is a client abort (499, Debug). `TimeoutException` and a non-abort `OperationCanceledException` become 504 `timeout.default`. |
| R11 | C-low | HSTS runs before the exception handler, so error responses carry it. |
| R12 | C-low | `TypeBaseUri` is normalized to end with `/`. |
| R13 | C-low | `MaxJsonDepth` defaults to the framework default (a lower value also limited responses). |
| R14 | C-low, D-low | `OkWithETag` answers 304 only for GET/HEAD, and documents 304 only for those. |
| R15 | C-low | A gRPC request refused with no authentication scheme registered gets 401 (no body), as HTTP does. |
| R16 | D3 | The real reason (no `IUserContextMapper` for the scheme) is logged; a startup warning names authentication schemes without a mapper. |
| R17 | D5 | Startup warning when `AddSharedKernelWebApi()` ran but `UseSharedKernelWebApi()` did not. |
| R18 | D6, D7 | Bindable parameter types `IdempotencyKey` and `IfMatch<TVersion>` (`TVersion : IParsable`), whose presence adds the requirement metadata (so declaring the parameter requires and documents the header). |
| R19 | D8 | The MVC `ToActionResult` family is removed; controllers return the same typed results (verified to work, including OpenAPI). |
| R20 | D9, D10, C20 | Non-generic `Result.ToAccepted(location?)`/`ToCreated(location)`. Consistent argument order: header selector first, then map (`ToOkWithETag(version, map)`). |
| R21 | D11 | Everyday types move to the root namespace (attributes, `ErrorHttpResult`, `OkWithETag`, accessors, conventions); a normal service needs one `using`. |
| R22 | D-low | `WebApiOptions` → `SharedKernelWebApiOptions`, consistent with the other three packages. |
| R23 | S5 | Startup warning when exception details are enabled outside Development. |
| R24 | S6 | CORS validation rejects the `null` origin, and `http://` origins with credentials outside Development. |
| R25 | S7 | When CORS is configured, WebSocket upgrades from an origin outside the allow-list are refused (hubs are covered, as documented). |
| R26 | S8 | The RFC 9470 challenge echoes only `Bearer` or `DPoP` as the scheme. |
| R27 | S12 | Default `Cache-Control: no-store` on responses that set neither `Cache-Control` nor `ETag` (option; null disables). |
| R28 | C22, D-low | XML docs: invalid options throw at host build (Kestrel) / first pipeline build (TestServer); `ErrorHttpResult.StatusCode` is the type-derived status (conditional requests may answer 412); the body limit is enforced by the server (Kestrel), not by TestServer. |

## Remediation — other packages (streams B–D)

| R | Source | Decision |
| --- | --- | --- |
| R30 | S4 | gRPC: an `RpcException` not built by the platform is rebuilt without its original trailers. Server-category text is redacted outside Development, and the platform `ErrorInfo` (this service's domain) is added. |
| R31 | C1, S11 | gRPC: field violations are capped (count and size), with a final "and N more" entry, so the status stays under the client's header limit. |
| R32 | D4 | gRPC: the package's `ThrowIfFailure`/`GetValueOrThrow` (identical signatures to `SharedKernel.Core`'s, CS0121) and the `ResultFailures` handoff are removed; Core's throw `Error.ToException()`, which the interceptor maps to the same rich status. |
| R33 | C2 | gRPC: any exception while the call is cancelled is a cancellation (`Cancelled`, Debug). |
| R34 | D2 | SignalR: the documented client-side format is SignalR's real one (`… HubException: {code}: {message}`); a public `HubErrorMessage.TryParse` extracts code and message. |
| R35 | C14, D-low | SignalR: a hub method returning `Result<IAsyncEnumerable<T>>` fails with the coded HubException instead of closing the connection; streaming errors after the stream starts are documented as uncoded. |
| R36 | S16 | OpenApi: a startup warning when documents are exposed in Production without an authorization convention. |
| R37 | — | OpenApi, samples, consumer-verify, 00.Governance, 13 tests: adopt R6–R22 (metadata, parameter types, namespaces, renames). The OpenApi add-on documents 428/400/412 for If-Match. |
| R38 | C18, S | 11.Communication.Rest: the error code falls back to `http.{status}` (never `title`); an `errors` map means Validation only for 400/422. |

## Not changed, documented

- `[AllowAnonymous]` switches off every requirement (S10, ASP.NET Core semantics).
- The automatic 412 carries no current ETag (C19): clients re-read, which is the safer choice.
- HSTS behind a TLS-terminating proxy needs forwarded headers (S15): use R2's `AtStart`.
- The unknown OpenAPI document 404 is plain text (C21).
- gRPC authorization refusals carry no rich status.

## Follow-ups outside this pass (owner decision)

| Item | Source | Owner |
| --- | --- | --- |
| Step-up on long-lived SignalR connections and gRPC streams: the TOTP step-up claim (`amr=otp`) carries no verification time, so a connection keeps it after the window. Needs 12.Security to stamp the step-up time, plus an optional max age on `[RequireAuthenticationMethod]`. | S3 | 12.Security + 14 |
| `BaggageLogRecordProcessor` copies every baggage item onto every log record (allow-list needed); the default feature-management accessor takes the tenant from baggage. R3 closes the HTTP edge only. | S1 | 13.ServiceDefaults, 01.Core |
| Idempotency keys are reserved per tenant, not per caller: a user who knows another user's key can replay that user's stored response. | S (adjacent) | 05.Application, 18.Idempotency |
| ETags are the raw PostgreSQL `xmin`, which reveals transaction volume across tenants. | S13 | 06.Persistence |
| The refusal log (14002) records no caller identity. | S14 | 14 (needs a privacy decision) |
| A `ValidationResult<T>` → `Result<T>` bridge, and naming the field of a hand-made validation error. | D14 | 01.Core |
| `SecureDefaultsAssertion.AssertMethodBodyInvokesMethod` never matches methods on nested types. | wave 4 | 00.Governance |
| Remaining outage errors that are still `Unexpected`: 10.Intelligence, 17's `workflow.service_unavailable`/`timed_out`, 06's statement timeouts, ElasticSearch 504 → `search.unreachable`. | wave 1 | 06, 09, 10, 17 |
