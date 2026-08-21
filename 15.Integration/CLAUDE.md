# 15.Integration — Outbound Integration Layer

> **Design status:** Locked (WO-032, Design phase P-200, 2026-06-26). Every contract below has been re-confirmed against the current shape of `SharedKernel.Primitives`, `SharedKernel.Configuration`, `SharedKernel.Contracts`, and `SharedKernel.Messaging.Abstractions` as they exist on disk today — not carried forward unverified from the original 2026-06-25 draft. One correction was made during ratification: `WebhookDeliveryOptions`'s validation mechanism (see Options section below). This is the basis Scaffold (P-201) builds `.csproj` references against.
>
> **WO-064 gold-standard hardening pass (shipped, 2026-08-21, `●` Complete in `SK.15.WO064`/`state-map.md`):** nine additive capabilities built on top of the already-`●`-Published v1.0.0 surface — resilience-handler retry/backoff/timeout wiring correctness (P-421), an SSRF guard (P-422), a per-delivery idempotency header (P-423), an `ActivitySource` (P-424), multi-secret signing rotation (P-425), custom per-subscription headers (P-426), opt-in payload encryption (P-427), an audit-logging retrofit (P-428), and a synthetic ping delivery (P-429). Every contract described below that carries a "(WO-064)" annotation is now implemented and covered by tests — see `15.Integration/state-map.md`'s `SK.15.WO064` phase for the 38-task implementation checklist (all `●`).

## What This Domain Is

The outbound third-party integration surface. Today this means exactly one capability: delivering platform integration events to external HTTP subscribers as signed webhooks. Every downstream microservice that needs to notify an external system (a partner API, a customer-configured callback URL) of something that happened — instead of, or in addition to, publishing onto the internal message bus — derives its dispatch, signing, retry, and verification behavior from the types defined here. This domain is delivery plumbing, not business logic — it does not decide *which* events matter to *which* subscriber; it only delivers a given integration event to a given subscriber reliably and verifiably.

Philosophy: **Thin. Transport-agnostic of the internal bus. Signed by default. Storage-agnostic.**

> **Layering boundary:** Per root `CLAUDE.md`, `15.Integration` may reference `01.Core`, `04.Contracts`, and `SharedKernel.Messaging.Abstractions` (07.Messaging abstractions only) — never `06.Persistence`, `11.Communication`, `07.Messaging.MassTransit`, or any other infrastructure layer. Two consequences fall directly out of that boundary and are documented in detail below: (1) outbound HTTP delivery cannot go through `SharedKernel.Communication.Rest`'s typed-client builder (`AddRestClient<TClient>()`) — it must go directly through `IHttpClientFactory`, the underlying ASP.NET Core abstraction that `11.Communication.Rest` itself wraps; (2) subscription storage and delivery-history persistence cannot live here — this domain defines only the seams (`IWebhookSubscriptionStore`, `IWebhookDeliveryObserver`) and the consuming microservice supplies storage-backed implementations using its own `06.Persistence` stack.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Integration.Webhooks` | Outbound webhook subscription contract, HMAC-SHA256 signing + replay-resistant verification, retrying signed dispatch, delivery-exhausted integration event | `SharedKernel.Primitives`, `SharedKernel.Configuration`, `SharedKernel.Contracts`, `SharedKernel.Messaging.Abstractions`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience`, `SharedKernel.Cryptography` (WO-064/P-427, shipped — new, for opt-in payload encryption) |

Targets `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. The test sub-folder lives inside the package folder (never in a top-level `tests/`). No `.Abstractions` sibling exists — unlike `02.Caching`/`06.Persistence`, this is a single capability with a single delivery mechanism today, not an interchangeable multi-provider surface. If a second outbound channel (e.g. inbound webhook *receipt* verification helpers reused by other services, or a non-HTTP delivery channel) is added later, re-evaluate the `.Abstractions` split at that time — do not pre-split speculatively.

---

## Technology Stack

| Concern | Technology | Owning Package |
| --- | --- | --- |
| Outbound HTTP delivery | `IHttpClientFactory` named client (`Microsoft.Extensions.Http`, in-box with ASP.NET Core) — pinned `10.0.9`, aligning with the platform's existing `10.0.9` floor for `Microsoft.Extensions.*` packages | `SharedKernel.Integration.Webhooks` |
| Delivery resilience (retry, backoff, timeout) | Polly v8 via `Microsoft.Extensions.Http.Resilience`'s standard resilience handler — pinned `10.7.0` (latest stable on the .NET 10 line at Scaffold time, 2026-06-26); AOT status not yet verified, per the existing AOT caveat below | `SharedKernel.Integration.Webhooks` |
| Signing | `System.Security.Cryptography.HMACSHA256` (BCL) — zero new dependency | `SharedKernel.Integration.Webhooks` |
| Replay protection | Timestamp-prefixed signing input + constant-time comparison (`CryptographicOperations.FixedTimeEquals`) | `SharedKernel.Integration.Webhooks` |
| Outbound payload serialization | `System.Text.Json` source-generated `JsonSerializerContext` | `SharedKernel.Integration.Webhooks` |
| Delivery-exhausted notification | `IEventPublisher` (`SharedKernel.Messaging.Abstractions`, 07.Messaging) | `SharedKernel.Integration.Webhooks` |
| Options validation | `SharedKernel.Configuration` (01.Core) Options-pattern validator | `SharedKernel.Integration.Webhooks` |
| Outbound URL/SSRF guard (WO-064) | `System.Net.Dns` (BCL) resolved-`IPAddress` range checks — zero new dependency | `SharedKernel.Integration.Webhooks` |
| Distributed tracing (WO-064) | `System.Diagnostics.ActivitySource` (BCL) — `"SharedKernel.Integration"` | `SharedKernel.Integration.Webhooks` |
| Opt-in payload encryption (WO-064) | `ISymmetricEncryptionService` (AES-GCM, `SharedKernel.Cryptography`, 01.Core) | `SharedKernel.Integration.Webhooks` |

> **Why not `SharedKernel.Communication.Rest`:** That package's `AddRestClient<TClient>()` is the platform's typed-client convention for *inter-service* REST calls, and `11.Communication` sits outside this domain's allowed layering. Webhook delivery targets arbitrary, often third-party, externally-configured URLs — not a typed, service-discovery-resolved client — so the typed-client model doesn't fit even ignoring the layering constraint. Going directly through `IHttpClientFactory` is not a violation of the platform-wide "no raw `HttpClient` in a production constructor" rule (P-159): the factory itself is what's injected; `HttpClient` instances are created per-call via `CreateClient(...)` and never stored as injected state.
>
> **Why no persistence here:** Subscription records (URL, secret, active event types) and any delivery-history ledger are ordinary application data owned by the consuming microservice, modeled with that service's own `06.Persistence` stack. This package only defines the read seam (`IWebhookSubscriptionStore`) and an optional observation seam (`IWebhookDeliveryObserver`) that the consuming service implements against its own storage.

---

## Interface Contracts

### `SharedKernel.Integration.Webhooks` — public surface

#### Subscriptions (`Subscriptions/`)

```text
WebhookSubscription  (sealed record)
    .SubscriptionId  → Guid
    .Secrets         → IReadOnlyList<string>   (WO-064/P-425, shipped — non-empty, newest-first; "sign with
                                                the first, verify against any"; shared HMAC-SHA256 key material —
                                                see Implementation Rules; never logged, never sent on the wire)
    .Secret          → string   [Obsolete]     (WO-064/P-425 — back-compat convenience for the pre-rotation
                                                single-secret shape; maps to a one-element Secrets list; retained,
                                                never removed, since this is a published v1.0.0 package)
    .Url             → Uri
    .EventTypes      → IReadOnlyList<string>   (empty = subscribed to every event type)
    .Headers         → IReadOnlyDictionary<string,string>?   (WO-064/P-426, shipped — optional static
                                                headers applied to every outbound delivery for this subscription,
                                                default null/empty; a header name colliding with any
                                                WebhookSignatureHeaders constant is rejected at dispatch time,
                                                never silently overwritten in either direction)
    .IsActive        → bool
    NOTE: Pure DTO, no behavior. The consuming service owns persistence of the backing data (typically an
          EF Core entity via its own 06.Persistence stack) and projects it into this record when handing
          subscriptions to the dispatcher. This package never serializes or stores this type itself.

IWebhookSubscriptionStore  (interface)
    .GetActiveSubscriptionsAsync(string eventType, CancellationToken ct) → Task<IReadOnlyList<WebhookSubscription>>
    NOTE: Implemented by the consuming microservice — there is no default implementation in this package.
          Must return only IsActive == true subscriptions whose EventTypes either contains eventType or is
          empty (empty list means "subscribed to everything"). That filtering is the implementation's
          responsibility, not IWebhookDispatcher's.
```

#### Dispatch (`Dispatch/`)

```text
IWebhookDispatcher  (interface)
    .DispatchAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : IIntegrationEvent                                   → Task<IReadOnlyList<WebhookDeliveryResult>>
    NOTE: Resolves the routing key as typeof(TEvent).Name — a deliberate convention parallel to
          EventEnvelope<TEvent>.EventType's identical typeof(TEvent).Name derivation in 04.Contracts (confirmed
          against current source in Design phase D-02), so one event type routes identically whether it
          travels over 07.Messaging or as a webhook. This is a convention parallel, not a shared generic
          constraint — EventEnvelope<TEvent> constrains on IDomainEvent (03.Domain), this dispatcher on
          IIntegrationEvent (04.Contracts); the two never unify under one type parameter. Looks up active
          subscriptions via IWebhookSubscriptionStore, then delivers to each concurrently (bounded by
          WebhookDeliveryOptions.MaxConcurrentDeliveries). A single subscription's delivery failure never
          faults the others — see DispatchToSubscriptionAsync.

    .DispatchToSubscriptionAsync<TEvent>(WebhookSubscription subscription, TEvent integrationEvent, CancellationToken ct)
        where TEvent : IIntegrationEvent                                   → Task<WebhookDeliveryResult>
    NOTE: Single-subscription delivery path, exposed publicly for callers that already hold a resolved
          subscription (e.g. a manual "redeliver this one" admin action) and don't need the fan-out lookup.
          Never throws for an HTTP-level failure (non-2xx, timeout, transport exception) — those surface as
          a WebhookDeliveryResult with IsSuccess == false. Only invalid input (null arguments) throws.
          On exhausting WebhookDeliveryOptions.MaxAttempts without a 2xx response, publishes exactly one
          WebhookDeliveryExhaustedEvent via IEventPublisher before returning the failed result. Generates one
          Guid delivery id at the start of the call (WO-064/P-423, shipped), stable across every retry of
          this same delivery, sent as WebhookSignatureHeaders.DeliveryIdHeaderName and returned on
          WebhookDeliveryResult.DeliveryId. Immediately before every SendAsync, invokes the registered
          IWebhookUrlValidator (WO-064/P-422, shipped) against the subscription's Url; a rejected target
          short-circuits to a failed WebhookDeliveryResult without any HTTP attempt, following the same
          never-throws contract as an HTTP-level failure.

    .SendTestDeliveryAsync(WebhookSubscription subscription, CancellationToken ct) → Task<WebhookDeliveryResult>
    NOTE (WO-064/P-429, shipped): Synthetic onboarding/connectivity-check delivery. Constructs a
          WebhookPingEvent and calls the existing DispatchToSubscriptionAsync<WebhookPingEvent> verbatim — zero
          parallel signing/retry/observer logic. Lets a subscriber verify their endpoint, signature
          verification, and header handling before any real business event fires.

WebhookDeliveryResult  (sealed record)
    .SubscriptionId  → Guid
    .DeliveryId      → Guid      (WO-064/P-423, shipped — identifies the delivery, stable across every
                                  retry of that delivery; matches the WebhookSignatureHeaders.DeliveryIdHeaderName
                                  value sent on the wire)
    .IsSuccess       → bool
    .StatusCode      → int?      (HTTP status of the final attempt; null if every attempt faulted before a response was received)
    .Attempts        → int       (1-based count of HTTP attempts actually made)
    .Error           → string?   (non-null only when IsSuccess == false)
    NOTE: IsSuccess is true only when some attempt within MaxAttempts received a 2xx response. Every other
          terminal outcome — non-2xx exhausted, timeout exhausted, transport exception exhausted, SSRF-guard
          rejection (WO-064/P-422) — is IsSuccess == false with Error populated and StatusCode reflecting the
          last attempt if one exists (null for an SSRF-guard rejection, since no HTTP attempt was made).
```

#### Outbound URL validation (`Dispatch/`, WO-064/P-422, shipped)

```text
IWebhookUrlValidator  (interface)
    .ValidateAsync(Uri url, CancellationToken ct) → Task<bool>
    NOTE: Invoked by WebhookDispatcher immediately before every SendAsync — re-checked per delivery attempt,
          never cached from subscription-registration time, to close the DNS-rebinding bypass where a hostname
          resolves to a public IP at validation time and a private one at connection time. A false result
          surfaces as a failed, non-throwing WebhookDeliveryResult. Overridable via WithUrlValidator<T>().

PrivateNetworkWebhookUrlValidator  (sealed class, default IWebhookUrlValidator)
    NOTE: Resolves the target host via System.Net.Dns and rejects the request when the resolved IPAddress falls
          in a loopback, link-local (169.254.0.0/16, fd00::/8), private (RFC1918/RFC4193), or multicast/reserved
          range — checked for both IPv4 and IPv6. Checks the resolved IP, never the literal hostname string.
          Fail-closed by default; WebhookDeliveryOptions.AllowPrivateNetworkTargets (bool, default false) is the
          only permitted opt-out, intended for legitimate internal test/staging subscriptions only.
```

#### Signing and verification (`Signing/`)

```text
WebhookSignatureHeaders  (static class)
    .SignatureHeaderName   → "X-Webhook-Signature"
    .TimestampHeaderName   → "X-Webhook-Timestamp"
    .DeliveryIdHeaderName  → "X-Webhook-Delivery-Id"   (WO-064/P-423, shipped)
    NOTE: Single source of truth for all three header names. WebhookDispatcher and WebhookSignatureVerifier must
          both reference these constants — never a literal header-name string — so the two sides cannot
          silently drift. A WebhookSubscription.Headers (WO-064/P-426) entry colliding case-insensitively with
          any of these three names is rejected at dispatch time.

WebhookSignatureProvider  (sealed class)
    .Sign(string payloadJson, string secret, DateTimeOffset timestamp) → string
    NOTE: Computes HMAC-SHA256 (System.Security.Cryptography.HMACSHA256, BCL) over
          UTF8("{timestamp:unix-seconds}.{payloadJson}") keyed by secret; returns the lowercase hex digest.
          The timestamp-prefixed signing input is the GitHub/Stripe-style replay-protection convention —
          the digest alone proves authenticity, not freshness; freshness is enforced separately by
          WebhookSignatureVerifier's tolerance window. Stateless — registered as a singleton.

WebhookSignatureVerifier  (static class)
    .Verify(string payloadJson, string timestampHeaderValue, string signatureHeaderValue, string secret,
            TimeSpan? tolerance = null)                                    → bool
    NOTE: tolerance defaults to 5 minutes. Never throws — a malformed timestamp, a signature outside the
          tolerance window, or a digest mismatch all return false. Digest comparison uses
          CryptographicOperations.FixedTimeEquals (constant-time) — never `==` or `string.Equals` on the
          digest, which would be a timing-attack vulnerability. This is the primitive a downstream
          service's inbound webhook receiver endpoint (typically a 14.Presentation Minimal API route) calls
          to validate a webhook claiming to originate from a SharedKernel.Integration.Webhooks dispatcher
          elsewhere on the platform.

    .Verify(string payloadJson, string timestampHeaderValue, string signatureHeaderValue,
            IReadOnlyList<string> secretCandidates, TimeSpan? tolerance = null)   → bool
    NOTE (WO-064/P-425, shipped): Multi-secret rotation overload. Evaluates
          CryptographicOperations.FixedTimeEquals against every candidate in secretCandidates without
          short-circuiting the iteration — the boolean result is accumulated across the full list, never
          returned early on the first match — so total comparison time cannot itself leak which
          rotation-window secret matched. Returns true if any candidate matches. The single-secret overload
          above is retained unchanged and delegates to this one with a one-element list.
```

#### Distributed tracing (`Dispatch/`, WO-064/P-424, shipped)

```text
WebhookIntegrationActivitySource  (static class, ActivitySource "SharedKernel.Integration")
    NOTE: WebhookDispatcher.DispatchToSubscriptionAsync starts a "WebhookDispatcher.DispatchToSubscription"
          span (tags: SubscriptionId, event type, outcome, attempt count); DispatchAsync starts a parent
          "WebhookDispatcher.Dispatch" span (tags: subscription count, event type) around the fan-out. Spans
          never carry WebhookSubscription.Url or .Secret as a tag under any circumstance. Any tag name that
          overlaps an existing cross-domain concept (e.g. a future tenant-id tag) uses 01.Core's
          WellKnownTagKeys; anything 15.Integration-specific uses a new domain-local WebhookActivityTags
          constants class, following the same single-source-of-truth discipline as WebhookSignatureHeaders.
```

#### Delivery-exhausted notification (`Events/`)

```text
WebhookDeliveryExhaustedEvent  (sealed record, implements IIntegrationEvent)
    .EventId         → Guid
    .OccurredOn      → DateTimeOffset
    .SubscriptionId  → Guid
    .EventType       → string   (the original integration event's type name that failed to deliver)
    .Attempts        → int
    .LastError       → string?
    NOTE: Published via IEventPublisher.PublishAsync exactly once per exhausted subscription, after
          WebhookDeliveryOptions.MaxAttempts is reached without a 2xx response. Lets any consumer elsewhere
          on the platform (an ops/alerting handler, or the owning service itself) react — disable the
          subscription, page someone, surface it in an admin UI. This type lives here rather than in
          04.Contracts because it is specific to this capability's own failure mode, not a general
          cross-service contract; any service that wants to consume it already depends on
          SharedKernel.Integration.Webhooks for the dispatcher itself.
    NOTE (confirmed Design D-02/D-03, 2026-06-26): IIntegrationEvent's current shape in 04.Contracts is
          exactly { Guid EventId; DateTimeOffset OccurredOn; } — nothing more — so this is a clean fit with
          no member drift. IEventPublisher.PublishAsync<TEvent>(TEvent, CancellationToken) constrains only on
          `where TEvent : class` (not IIntegrationEvent or IDomainEvent), so implementing IIntegrationEvent is
          not required for IEventPublisher to accept this type — it is implemented anyway purely for
          cross-domain convention consistency (every integration event on the platform exposes the same
          EventId/OccurredOn shape), not because the publish call site demands it.

WebhookPingEvent  (sealed record, implements IIntegrationEvent)
    .EventId         → Guid
    .OccurredOn      → DateTimeOffset
    NOTE (WO-064/P-429, shipped): No business payload — deliberately minimal. Routes as
          typeof(WebhookPingEvent).Name ("WebhookPingEvent"), consistent with IWebhookDispatcher's existing
          routing convention, giving the subscriber an unambiguous, reserved event-type name to distinguish a
          synthetic onboarding delivery from real business data. Constructed and dispatched only by
          IWebhookDispatcher.SendTestDeliveryAsync — never published onto 07.Messaging or fanned out via
          DispatchAsync's normal subscription lookup.
```

#### Logging (`Dispatch/`)

```text
WebhookDispatcher — Log  (private static partial class nested inside WebhookDispatcher)
    ObserverException(ILogger logger, Exception ex, string observerType)   [LoggerMessage, EventId = LoggingEventIdRanges.Integration + 0 (= 15000), Level = Warning]
    NOTE: Backs the single shared LogObserverException(Exception ex, string observerTypeName) helper called from both
          NotifyAttemptAsync and NotifyCompletedAsync when an IWebhookDeliveryObserver implementation throws. This
          was the only production log statement in SharedKernel.Integration.Webhooks from P-257 (WO-041) until
          WO-064 — see "Logging (EventId allocation)" below.

    DeliverySucceeded(ILogger logger, Guid subscriptionId, string eventType, int attempts, int statusCode)
        [LoggerMessage, EventId = LoggingEventIdRanges.Integration + 1 (= 15001), Level = Information]
    DeliveryFailed(ILogger logger, Guid subscriptionId, string eventType, int attempts, int? statusCode, string? error)
        [LoggerMessage, EventId = LoggingEventIdRanges.Integration + 2 (= 15002), Level = Warning]
    DeliveryExhausted(ILogger logger, Guid subscriptionId, string eventType, int attempts)
        [LoggerMessage, EventId = LoggingEventIdRanges.Integration + 3 (= 15003), Level = Warning]
    NOTE (WO-064/P-428, shipped): Fired from WebhookDispatcher.DispatchToSubscriptionAsync on the
          corresponding terminal outcome. DeliveryExhausted logs alongside — never in place of — the existing
          WebhookDeliveryExhaustedEvent publish. None of the three ever include WebhookSubscription.Secret, the
          signature digest, or the raw payload body as a template placeholder.
```

#### Delivery observation hook (`Observability/`)

```text
IWebhookDeliveryObserver  (interface)
    .OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct)      → Task
    .OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct) → Task
    NOTE: Optional — zero or more observers registered via WithDeliveryObserver<T>(). All registered
          observers are invoked for every attempt and completion; an observer's exception is caught and
          logged at LogLevel.Warning, never allowed to fault the delivery pipeline. This is the seam a
          consuming service uses to persist a delivery-history ledger against its own 06.Persistence stack
          without this package taking a 06.Persistence dependency.
```

#### Options (`Options/`)

```text
WebhookDeliveryOptions  (options POCO, section "SharedKernel:Integration:Webhooks")
    .MaxAttempts              (int, default 5)              — [Range(1, int.MaxValue)]
    .BaseBackoffDelay         (TimeSpan, default 2s)
    .MaxBackoffDelay          (TimeSpan, default 60s)
    .RequestTimeout           (TimeSpan, default 10s)
    .SignatureTolerance       (TimeSpan, default 5m)
    .MaxConcurrentDeliveries  (int, default 8)               — [Range(1, int.MaxValue)]
    .AllowPrivateNetworkTargets  (bool, default false)       — WO-064/P-422, shipped; explicit SSRF-guard opt-out
    .EncryptPayload           (bool, default false)          — WO-064/P-427, shipped; opt-in AES-GCM payload encryption
    : IValidatableObject
        → cross-field checks DataAnnotations attributes cannot express on their own:
          BaseBackoffDelay, MaxBackoffDelay, RequestTimeout, SignatureTolerance all > TimeSpan.Zero;
          MaxBackoffDelay ≥ BaseBackoffDelay.
    NOTE: Registered and validated eagerly at startup via SharedKernel.Configuration's actual API —
          OptionsExtensions.AddValidatedOptions<TOptions>(this IServiceCollection, IConfigurationSection),
          which calls .Bind(section).ValidateDataAnnotations().ValidateOnStart() under the hood. There is
          no separate IValidateOptions<T> contract or standalone validator class in SharedKernel.Configuration
          today — mechanical bounds (MaxAttempts ≥ 1, MaxConcurrentDeliveries ≥ 1) are plain DataAnnotations
          [Range] attributes on the POCO; the one cross-field rule (MaxBackoffDelay ≥ BaseBackoffDelay) and
          the positive-TimeSpan checks are expressed via IValidatableObject.Validate on WebhookDeliveryOptions
          itself. (Ratified in Design phase D-04, 2026-06-26 — corrects the original "Options-pattern
          validator" phrasing, which implied a free-standing validator type that does not exist upstream.)
    NOTE (WO-064/P-421, shipped): MaxAttempts/BaseBackoffDelay/MaxBackoffDelay/RequestTimeout drive the
          named HttpClient's Microsoft.Extensions.Http.Resilience pipeline via the exact formula —
          Retry.MaxRetryAttempts = Math.Max(0, MaxAttempts - 1); Retry.Delay = BaseBackoffDelay;
          Retry.BackoffType = DelayBackoffType.Exponential; Retry.MaxDelay = MaxBackoffDelay;
          AttemptTimeout.Timeout = RequestTimeout;
          TotalRequestTimeout.Timeout = (RequestTimeout + MaxBackoffDelay) * MaxAttempts (a documented
          worst-case bound, not a magic constant) — resolved from ResilienceHandlerContext.ServiceProvider
          inside AddStandardResilienceHandler's configuration callback, never left to the library's own
          built-in defaults. See docs/configuration-reference.md for the fully worked mapping.
```

#### DI extensions (`Extensions/`)

```text
AddSharedKernelWebhooks(this IServiceCollection services, Action<WebhookDeliveryOptions>? configure = null)
    → IServiceCollection
    NOTE: Registers WebhookDeliveryOptions (+ eager validator), WebhookSignatureProvider (singleton —
          stateless), IWebhookDispatcher → WebhookDispatcher (scoped), IWebhookUrlValidator →
          PrivateNetworkWebhookUrlValidator (WO-064/P-422, shipped — singleton, overridable via
          WithUrlValidator<T>()), and a named HttpClient ("SharedKernel.Integration.Webhooks") via
          AddHttpClient(...).AddStandardResilienceHandler(...) (Microsoft.Extensions.Http.Resilience)
          configured from the resolved WebhookDeliveryOptions per the P-421 field-mapping formula documented
          in the Options section above. Does NOT register IWebhookSubscriptionStore (required — the consuming
          service must register its own implementation or DI resolution fails at first use) or any
          IWebhookDeliveryObserver (optional).

WithDeliveryObserver<TObserver>(this IServiceCollection services) → IServiceCollection
    where TObserver : class, IWebhookDeliveryObserver
    NOTE: Registers TObserver as scoped. Additive — multiple calls accumulate; every registered observer
          fires for every delivery attempt and completion, in registration order.

WithUrlValidator<TValidator>(this IServiceCollection services) → IServiceCollection
    where TValidator : class, IWebhookUrlValidator
    NOTE (WO-064/P-422, shipped): Overrides the default PrivateNetworkWebhookUrlValidator registration —
          the last call wins (single active validator, unlike WithDeliveryObserver<T>()'s additive registration).
          Exists for consuming services with a non-default target-network policy; the SSRF-guard default posture
          must not be silently disabled — prefer WebhookDeliveryOptions.AllowPrivateNetworkTargets for the common
          "allow internal staging targets" case instead of a full custom validator.
```

---

## Implementation Rules

### Hard violations (never do these)

- Constructing `new HttpClient()` anywhere in this package, or injecting a raw `HttpClient` into any constructor — `IHttpClientFactory` is the only permitted source, and only via the named client registered by `AddSharedKernelWebhooks`.
- `WebhookSubscription.Secret`/`.Secrets` appearing in a log statement, an exception message, an outbound request body, or any header other than as the *input* to `WebhookSignatureProvider.Sign` — only the derived HMAC digest is ever transmitted or surfaced. (WO-064/P-425, shipped: this extends unchanged to the new `Secrets` list — every candidate is signing/verification input only, never surfaced.)
- Comparing a computed signature digest to a received one with `==`, `string.Equals`, or any non-constant-time comparison — `CryptographicOperations.FixedTimeEquals` is mandatory inside `WebhookSignatureVerifier`, including per-candidate in the WO-064/P-425 multi-secret overload (never short-circuited on the first match).
- `WebhookSignatureVerifier.Verify` throwing for any malformed input — malformed timestamp, malformed signature, or a missing header must all produce `false`, never an exception.
- Adding a `DbContext`, `IRepository<T,TId>`, or any other `06.Persistence` type to this package — subscription storage and delivery-history persistence are the consuming service's responsibility, expressed only through `IWebhookSubscriptionStore` and `IWebhookDeliveryObserver`.
- Adding a `ProjectReference` to any `11.Communication.*` package — outbound HTTP goes directly through `IHttpClientFactory`; see "Why not `SharedKernel.Communication.Rest`" above.
- Adding a `ProjectReference` to `SharedKernel.Messaging.MassTransit` — only `SharedKernel.Messaging.Abstractions` (`IEventPublisher`) is within this domain's layering allowance.
- Letting an `IWebhookDeliveryObserver` implementation's exception propagate out of `IWebhookDispatcher` — observer calls are wrapped in try/catch with `LogLevel.Warning` logging; a faulty observer must never affect delivery outcome.
- Publishing `WebhookDeliveryExhaustedEvent` more than once per exhausted delivery, or publishing it for an attempt that has not yet exhausted `WebhookDeliveryOptions.MaxAttempts`.
- `IWebhookDispatcher.DispatchAsync`/`DispatchToSubscriptionAsync` throwing because of an individual subscription's HTTP failure, or because `IWebhookUrlValidator` rejects the target — per-subscription outcomes surface as a `WebhookDeliveryResult`, never an exception, so one unreachable or SSRF-guard-rejected endpoint cannot fail an entire fan-out (WO-064/P-422, shipped).
- Checking `WebhookSubscription.Url`'s literal hostname string instead of the DNS-resolved `IPAddress` in `IWebhookUrlValidator` — a hostname-only check is trivially bypassed by DNS rebinding (WO-064/P-422, shipped).
- Caching an `IWebhookUrlValidator` result from subscription-registration time instead of re-validating immediately before every `SendAsync` — the resolved IP can change between registration and delivery (WO-064/P-422, shipped).
- A `WebhookSubscription.Headers` entry silently overwriting (or being silently overwritten by) `WebhookSignatureHeaders.SignatureHeaderName`/`.TimestampHeaderName`/`.DeliveryIdHeaderName` — a name collision must fail the dispatch loudly, in either direction (WO-064/P-426, shipped).
- Any static mutable state.

### Signing convention rules

- The signing input is always `"{unixSeconds}.{payloadJson}"`, UTF-8 encoded — never the payload alone. Signing the payload alone provides authenticity but not freshness, allowing a captured request to be replayed indefinitely. (WO-064/P-427, shipped: when opt-in payload encryption is enabled, `payloadJson` in this formula is the post-encryption ciphertext representation, not the plaintext — see "Payload encryption rules" below.)
- `WebhookSignatureHeaders.SignatureHeaderName`, `.TimestampHeaderName`, and `.DeliveryIdHeaderName` (WO-064/P-423) are the only permitted header name literals — both the dispatcher (writing headers) and the verifier (reading them) reference these constants.
- `WebhookDeliveryOptions.SignatureTolerance` (default 5 minutes) is the only permitted clock-skew allowance for `WebhookSignatureVerifier.Verify` — do not hardcode a different window at a call site.
- `WebhookSubscription.Secrets` is always signed with `Secrets[0]` (the newest, first in the newest-first ordering) — never a randomly-selected or last-in-list candidate (WO-064/P-425, shipped). Verification, in contrast, must accept a match against *any* candidate in the list.

### Delivery rules

- `IWebhookDispatcher` resolves the event-type routing key as `typeof(TEvent).Name` — matching `EventEnvelope<TEvent>.EventType`'s convention in `04.Contracts` so the same event type routes identically over `07.Messaging` and over webhooks. `WebhookPingEvent` (WO-064/P-429) follows the identical convention, routing as `"WebhookPingEvent"`.
- Retry/backoff is configured once, on the named `HttpClient`, via `Microsoft.Extensions.Http.Resilience`'s standard resilience handler — never a hand-rolled retry loop inside `WebhookDispatcher`. The four `WebhookDeliveryOptions` retry/backoff/timeout knobs must actually drive that handler's configuration (WO-064/P-421, shipped) — a validated-but-unconsulted options value is itself a hard violation of this rule's intent, even though nothing here previously said so explicitly.
- `WebhookDeliveryOptions.MaxConcurrentDeliveries` bounds the fan-out in `DispatchAsync` — unbounded `Task.WhenAll` over an arbitrarily large subscription list is a hard violation.
- `IWebhookDispatcher.SendTestDeliveryAsync` (WO-064/P-429, shipped) must call the real `DispatchToSubscriptionAsync` — a parallel/duplicated signing-and-send code path for test deliveries is prohibited, since the entire point of a test delivery is proving the *actual* production code path works.

### SSRF guard rules (WO-064/P-422, shipped)

- Outbound delivery to a `WebhookSubscription.Url` that resolves to a loopback, link-local (`169.254.0.0/16`/`fd00::/8`), private (RFC1918/RFC4193), or multicast/reserved IP address is rejected by default, for both IPv4 and IPv6 — fail-closed is the only acceptable default posture for a package whose purpose is issuing outbound HTTP requests to externally-supplied URLs.
- The only permitted opt-out is `WebhookDeliveryOptions.AllowPrivateNetworkTargets` (or a caller-supplied `IWebhookUrlValidator` via `WithUrlValidator<T>()`) — never a silent bypass, a hardcoded allowlist bypassing the validator entirely, or a `TODO`-commented-out check.
- A rejected target is a `WebhookDeliveryResult` with `IsSuccess == false`, never a thrown exception — consistent with every other HTTP-level failure this package already treats this way.
- Validation runs immediately before every `SendAsync`, not once at subscription-registration time — this is the specific defense against DNS-rebinding TOCTOU bypass.

### Payload encryption rules (WO-064/P-427, shipped)

- Opt-in only, via `WebhookDeliveryOptions.EncryptPayload` — disabled by default; TLS already provides transport confidentiality, this is defense-in-depth for subscribers who want payload-level confidentiality independent of their own TLS termination boundary.
- Order is always encrypt-then-sign — the HMAC signature is computed over the post-encryption ciphertext bytes, never the plaintext, so `WebhookSignatureVerifier` continues to detect tampering on exactly what was transmitted.
- Uses `01.Core/SharedKernel.Cryptography`'s `ISymmetricEncryptionService` (AES-GCM/AEAD) exclusively — introducing a new cryptographic primitive inside `SharedKernel.Integration.Webhooks` itself is a hard violation, mirroring the platform-wide "no hand-rolled crypto" rule in the root `CLAUDE.md`.

### Logging rules

- Every production log statement in this package is authored via the `[LoggerMessage]` source-generated partial-method pattern (root `CLAUDE.md` Logging Conventions) — a direct `ILogger.LogInformation/LogWarning/LogError/LogCritical/LogTrace/LogDebug(...)` extension-method call or a hand-written `LoggerMessage.Define<>()` static delegate is a hard violation, mechanically enforced by `00.Governance`'s SK0020 (`DirectILoggerExtensionMethodUsage`) / SK0021 (`HandWrittenLoggerMessageDefineDelegate`) analyzers and `LoggingEventIdIntegrityAssertion` (P-250) once shipped.
- Every `[LoggerMessage]` method's `EventId` is written as `LoggingEventIdRanges.Integration + {offset}` (`LoggingEventIdRanges.Integration` is `const int` = 15000, so the sum is itself a valid compile-time constant `[LoggerMessage(EventId = ...)]` argument) — never a bare literal integer.
- Message template placeholders are PascalCase named properties matching the call's named arguments (e.g. `{ObserverType}`) — never positional placeholders, never string-interpolated into the template.
- CorrelationId, distributed-trace context, and TenantId are never passed as explicit message-template placeholders on any log statement in this package — they flow ambiently through the OpenTelemetry logging pipeline (`13.ServiceDefaults`), consistent with the root convention.

---

## AOT Notes

- `HMACSHA256` and `CryptographicOperations.FixedTimeEquals` are BCL, fully AOT-compatible.
- The outbound payload is serialized via a `System.Text.Json` source-generated `JsonSerializerContext` — no runtime reflection-based serialization.
- `Microsoft.Extensions.Http.Resilience` (Polly v8) AOT status must be re-verified on every major version bump — third-party, not BCL.
- No reflection anywhere in this package's hot path; `IWebhookSubscriptionStore` and `IWebhookDeliveryObserver` are plain interfaces resolved through ordinary DI.
- (WO-064/P-422, shipped) `System.Net.Dns.GetHostAddressesAsync`/IP-range checks in `PrivateNetworkWebhookUrlValidator` are BCL, fully AOT-compatible.
- (WO-064/P-424, shipped) `System.Diagnostics.ActivitySource`/`Activity` are BCL, fully AOT-compatible — no new AOT risk from the tracing addition.
- (WO-064/P-427, shipped) `ISymmetricEncryptionService` (AES-GCM) from `SharedKernel.Cryptography` is already documented as pure-BCL/AOT-safe in `01.Core`'s own brain (P-205–P-209, WO-033) — this package inherits that guarantee by composition, introducing no new AOT-risk surface of its own.

---

## Logging (EventId allocation — P-257, WO-041)

`15.Integration` reserves `LoggingEventIdRanges.Integration` (15000-15999, from `SharedKernel.Primitives` — `01.Core` P-249) as its platform-wide `EventId` block. Unlike every other domain retrofitted under WO-041, this is a **single-package domain** (`SharedKernel.Integration.Webhooks` — no `.Abstractions` sibling, per the package-split discipline in the Packages section above), so the root registry's 100-wide-per-package sub-block subdivision rule does not apply here: subdivision is required only "when a domain has multiple packages." The entire 15000-15999 block belongs to `SharedKernel.Integration.Webhooks` today.

| Type | EventId | Level | Trigger |
| --- | --- | --- | --- |
| `WebhookDispatcher.Log.ObserverException` | `LoggingEventIdRanges.Integration + 0` (15000) | Warning | An `IWebhookDeliveryObserver` implementation's `OnAttemptAsync`/`OnCompletedAsync` throws; the exception is caught and logged, never propagated (see the observer-isolation hard violation above) |
| `WebhookDispatcher.Log.DeliverySucceeded` (WO-064/P-428, shipped) | `LoggingEventIdRanges.Integration + 1` (15001) | Information | A delivery's final outcome is a 2xx response, within `MaxAttempts` |
| `WebhookDispatcher.Log.DeliveryFailed` (WO-064/P-428, shipped) | `LoggingEventIdRanges.Integration + 2` (15002) | Warning | A delivery's final outcome is a non-2xx/timeout/transport failure, within `MaxAttempts` (i.e. not yet exhausted) |
| `WebhookDispatcher.Log.DeliveryExhausted` (WO-064/P-428, shipped) | `LoggingEventIdRanges.Integration + 3` (15003) | Warning | `MaxAttempts` reached without a 2xx response — logged alongside, never in place of, the `WebhookDeliveryExhaustedEvent` publish |

Offsets `+4` through `+999` (15004-15999) stay reserved for future logging additions to this package, or — should a genuinely new second outbound delivery channel ever warrant the `.Abstractions` + `.{Provider}` split described in the Packages section — for a second package's own 100-wide sub-block, at which point this table must be re-partitioned following the same declaration-order convention `02.Caching` established.

Every `[LoggerMessage(EventId = ...)]` value in this package must be written as `LoggingEventIdRanges.Integration + {offset}` — never a bare literal integer. `SharedKernel.Integration.Webhooks.csproj` already carries a `<ProjectReference>` to `SharedKernel.Primitives` (added under S-01/D-01 for future-proofing before this package had any logging call site) — P-257 is the first phase to actually consume it.

---

## Tracing (`"SharedKernel.Integration"` ActivitySource — P-424, WO-064)

This domain's distributed-tracing surface, mirroring the shape of the Logging section above.

- **Source name:** `"SharedKernel.Integration"` (`WebhookIntegrationActivitySource.Name`), following the owning-namespace naming convention `06.Persistence`/`07.Messaging` already established — not the package name.
- **Spans:**
  - `WebhookDispatcher.Dispatch` — the parent span wrapping `DispatchAsync`'s fan-out. Tags: `webhook.subscription_count`, `webhook.event_type` (`WebhookActivityTags.SubscriptionCount`/`.EventType`).
  - `WebhookDispatcher.DispatchToSubscription` — the per-subscription span wrapping `DispatchToSubscriptionAsync`. Tags: `webhook.subscription_id`, `webhook.event_type`, `webhook.outcome` (`"success"`/`"failure"`), `webhook.attempt_count` (`WebhookActivityTags.SubscriptionId`/`.EventType`/`.Outcome`/`.AttemptCount`).
- **Hard rule:** no span emitted by this package ever carries `WebhookSubscription.Url` or any signing secret as a tag, under any circumstance — verified by a dedicated `ActivityListener`-based test (`WebhookTracingTests`).
- **Tag-name source of truth:** `WebhookActivityTags` (`Dispatch/`), a domain-local static-constants class mirroring `WebhookSignatureHeaders`' single-source-of-truth discipline. A future span needing a cross-domain concept (e.g. a tenant-id tag) reuses `01.Core`'s `WellKnownTagKeys` instead of adding a duplicate literal here — none of today's tags overlap an existing cross-domain concept.

---

## DI Registration (shipped shape)

```csharp
// Minimal setup — caller must also register IWebhookSubscriptionStore
builder.Services.AddSharedKernelWebhooks();
builder.Services.AddScoped<IWebhookSubscriptionStore, EfWebhookSubscriptionStore>();

// With custom delivery options
builder.Services.AddSharedKernelWebhooks(options =>
{
    options.MaxAttempts = 8;
    options.RequestTimeout = TimeSpan.FromSeconds(15);
});

// Optional delivery-history observer, backed by the consuming service's own persistence
builder.Services.WithDeliveryObserver<EfWebhookDeliveryLedger>();

// WO-064/P-422, shipped — overriding the default SSRF-guard validator (rarely needed;
// prefer WebhookDeliveryOptions.AllowPrivateNetworkTargets for the common internal-staging case)
builder.Services.WithUrlValidator<CustomAllowlistWebhookUrlValidator>();

// Dispatching an integration event as a webhook (application-layer call site)
public sealed record OrderShippedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId)
    : IIntegrationEvent;

var results = await webhookDispatcher.DispatchAsync(
    new OrderShippedIntegrationEvent(Guid.NewGuid(), clock.UtcNow, order.Id), ct);

// WO-064/P-429, shipped — onboarding a new subscription with a synthetic ping delivery
var pingResult = await webhookDispatcher.SendTestDeliveryAsync(subscription, ct);

// Verifying an inbound webhook claiming to come from this dispatcher (14.Presentation receiver endpoint)
var isValid = WebhookSignatureVerifier.Verify(
    payloadJson: rawBody,
    timestampHeaderValue: request.Headers[WebhookSignatureHeaders.TimestampHeaderName],
    signatureHeaderValue: request.Headers[WebhookSignatureHeaders.SignatureHeaderName],
    secretCandidates: subscription.Secrets); // WO-064/P-425 — accepts a match against any active secret
```

---

## Test Rules

- Test project is nested inside the package folder: `SharedKernel.Integration.Webhooks/SharedKernel.Integration.Webhooks.Tests/`.
- HTTP delivery tests stub the named `HttpClient` via a fake `DelegatingHandler` registered through `IHttpClientFactory` test wiring — no real network calls, no Testcontainers needed for this package.
- `WebhookSignatureProvider`/`WebhookSignatureVerifier`: round-trip tests (sign then verify succeeds), tamper tests (mutated payload or header fails verification), expired-timestamp tests (outside tolerance fails), malformed-input tests (never throws, always returns `false`).
- `IWebhookDispatcher.DispatchAsync`: fan-out to N active subscriptions, inactive/non-matching subscriptions excluded, one subscription's failure does not affect others' results.
- Retry/backoff: transient failures (e.g. 503 responses) retried up to `MaxAttempts`, success on a later attempt reflected correctly in `WebhookDeliveryResult.Attempts`, exhaustion publishes exactly one `WebhookDeliveryExhaustedEvent` — assert via `16.Testing`'s `InMemoryEventPublisher` (`ShouldHavePublishedOnce<WebhookDeliveryExhaustedEvent>()`) rather than a hand-rolled `IEventPublisher` stub.
- `IWebhookDeliveryObserver`: registered observers invoked once per attempt and once per completion; an observer that throws does not affect the delivery outcome and is logged (via `WebhookDispatcher.Log.ObserverException`, `EventId = LoggingEventIdRanges.Integration + 0`), not rethrown.
- `WebhookDeliveryOptions` validator: each invalid combination (zero `MaxAttempts`, `MaxBackoffDelay < BaseBackoffDelay`, non-positive `TimeSpan` values) fails startup validation with an actionable message.
- (WO-064/P-421, shipped) Resilience-handler field mapping: a GATING regression test configures non-default `MaxAttempts`/`BaseBackoffDelay`/`MaxBackoffDelay`/`RequestTimeout` and proves — via a `StubHttpMessageHandler` failure/retry sequence — that the dispatcher's real attempt count, inter-attempt delay bound, and per-attempt timeout match the configured values, not the resilience library's own built-in defaults.
- (WO-064/P-422, shipped) `IWebhookUrlValidator`: a rejection test (via a spy/fake validator, never a real DNS lookup) proves a private/loopback/metadata-resolving target is rejected before any HTTP attempt, surfaces as a non-throwing `WebhookDeliveryResult`, and still notifies registered observers; a separate test proves `AllowPrivateNetworkTargets = true` permits the target through.
- (WO-064/P-423, shipped) Delivery id: a multi-retry test proves the `X-Webhook-Delivery-Id` header value is identical across every attempt of one delivery and equals the returned `WebhookDeliveryResult.DeliveryId`.
- (WO-064/P-424, shipped) `ActivitySource`: a test using an `ActivityListener` proves a span is emitted for both a successful and a failed delivery, with the documented tags present and `Url`/`Secret` never present as a tag key or value.
- (WO-064/P-425, shipped) Multi-secret rotation: a test proves a signature produced with a non-newest active secret still verifies against the full `Secrets` candidate list, and that `FixedTimeEquals` is evaluated once per candidate without short-circuiting.
- (WO-064/P-426, shipped) Custom headers: a test proves `WebhookSubscription.Headers` entries are present on the outbound request; a second test proves a reserved-header-name collision is rejected before any HTTP call.
- (WO-064/P-427, shipped) Payload encryption: a round-trip test (encrypt → sign over ciphertext → verify → decrypt → original payload recovered) when `EncryptPayload = true`; a test proving zero wire-format change when left at its `false` default.
- (WO-064/P-428, shipped) Delivery-outcome logging: assertions via `16.Testing`'s in-memory `ILogger`/`ILoggerFactory` double proving `DeliverySucceeded`/`DeliveryFailed`/`DeliveryExhausted` fire on the corresponding outcome, and that no log statement in the package ever includes `Secret`/digest/payload content.
- (WO-064/P-429, shipped) `SendTestDeliveryAsync`: a test proves the ping delivery is signed, delivered, and retried identically to a real event dispatch, and that the delivered event-type is unambiguously `"WebhookPingEvent"`.
- Standard test package set: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`, `FluentAssertions`, plus `Microsoft.Extensions.Http` test doubles as needed. `GlobalUsings.cs` includes `global using Xunit;`.

---

## Changelog

> Maintained by the integration domain agent. One line per significant change.

- [2026-06-25] Domain brain initialized — packages, technology stack, layering-boundary rationale (no `11.Communication`, no `06.Persistence`), interface contracts (`WebhookSubscription`/`IWebhookSubscriptionStore`, `IWebhookDispatcher`/`WebhookDeliveryResult`, `WebhookSignatureProvider`/`WebhookSignatureVerifier`, `WebhookDeliveryExhaustedEvent`, `IWebhookDeliveryObserver`, `WebhookDeliveryOptions`, DI extensions), implementation rules, AOT notes, DI registration shape, test rules (claude)
- [2026-06-26] SK.15.Scaffold (P-201) complete — `SharedKernel.Integration.Webhooks.csproj` wired to the four locked ProjectReferences plus `Microsoft.Extensions.Http` `10.0.9` / `Microsoft.Extensions.Http.Resilience` `10.7.0` (first platform pin for both); 7 stub folders created; Tests project wired to `SharedKernel.Testing` + standard xUnit/FluentAssertions set; both already present in `.slnx`; `dotnet build` clean on both target projects (integration-phase-implementer)
- [2026-06-26] WO-032 Design phase (P-200) locked — every interface contract re-confirmed against current upstream source (`SharedKernel.Primitives`, `SharedKernel.Contracts`'s `IIntegrationEvent`/`EventEnvelope<TEvent>`, `SharedKernel.Messaging.Abstractions`'s `IEventPublisher`, `SharedKernel.Configuration`'s `OptionsExtensions`); one correction made — `WebhookDeliveryOptions` validates via DataAnnotations `[Range]` attributes + `IValidatableObject` through `AddValidatedOptions<TOptions>(IConfigurationSection)`, not a free-standing validator type as originally phrased, since `SharedKernel.Configuration` exposes no separate `IValidateOptions<T>` contract; `IWebhookDispatcher`'s `typeof(TEvent).Name` routing convention reconfirmed as a deliberate parallel to (not a shared constraint with) `EventEnvelope<TEvent>.EventType`; `WebhookDeliveryExhaustedEvent` implementing `IIntegrationEvent` reconfirmed as a convention choice, not an `IEventPublisher` requirement (`PublishAsync<TEvent>` constrains only `where TEvent : class`); zero reference to `06.Persistence`/`11.Communication.*`/`07.Messaging.MassTransit` re-verified across all four confirmed dependency surfaces (integration-arch-planner)
- [2026-06-26] SK.15.Docs (DO-01–DO-03) complete — `GenerateDocumentationFile` enabled in `SharedKernel.Integration.Webhooks.csproj` (zero missing-doc warnings; one unresolved `<see cref="WebhookDispatcher"/>` in `WebhookSignatureVerifier.cs` fixed by switching to a `<c>` literal since the type lives in a different namespace than the doc comment's compilation context expects); `README.md` added covering minimal setup, custom `WebhookDeliveryOptions`, `WithDeliveryObserver<T>()`, `DispatchAsync`/`DispatchToSubscriptionAsync`, and inbound `WebhookSignatureVerifier.Verify` usage from a `14.Presentation` receiver; `docs/configuration-reference.md` added covering every `WebhookDeliveryOptions` property/default/bound and validation-failure examples; 48/48 tests still passing (integration-phase-implementer)
- [2026-06-26] SK.15.Published (P-01–P-05) complete — verification-only, no interface/rule changes. `SharedKernel.Integration.Webhooks.csproj` gained full NuGet packaging metadata mirroring the `12.Security`/`13.ServiceDefaults`/`14.Presentation` convention (`PackageId`, MIT license, README packed via `PackagePath="\"`, symbol package); packs cleanly to `.nupkg`+`.snupkg` with zero warnings, output to root `artifacts/nupkg/` per the established repo convention (both extensions already `.gitignore`d). New `15.Integration/consumer-verify` harness (mirrors the `13.ServiceDefaults`/`14.Presentation` consumer-verify pattern, registered in `Platform.SharedKernel.slnx`) proves `AddSharedKernelWebhooks()` + a registered `IWebhookSubscriptionStore` + a stand-in `IEventPublisher` resolves `IWebhookDispatcher` and completes a real `DispatchAsync` call with zero DI exceptions, and proves omitting `IWebhookSubscriptionStore` causes `GetRequiredService<IWebhookDispatcher>()` itself to throw `InvalidOperationException` naming the missing type — failure surfaces immediately at first resolution (a constructor dependency of `WebhookDispatcher`), not deferred into a silently-resolved dispatcher that no-ops inside `DispatchAsync`. Harness discovery: `AddSharedKernelWebhooks()`'s `BindConfiguration` call requires `IConfiguration` registered in the container even with no bound section — any real host's builder already provides this; the harness registers an empty `ConfigurationBuilder().Build()` instance to satisfy it in isolation. 48/48 tests still passing. `SharedKernel.Integration.Webhooks` now `●` Published — **15.Integration domain (WO-032) complete end to end** (integration-phase-implementer)
- [2026-07-09] LoggingRetrofit phase (P-257, WO-041) planned — audited the domain's entire production log surface: exactly one call site, `WebhookDispatcher.LogObserverException` (a shared private helper invoked from both `NotifyAttemptAsync` and `NotifyCompletedAsync`), currently a direct `_logger.LogWarning(...)` call, no pre-existing `EventId` and no hand-written `LoggerMessage.Define` delegate. Added a "Logging" entry to the Interface Contracts section (`WebhookDispatcher.Log.ObserverException`, `[LoggerMessage]`, `EventId = LoggingEventIdRanges.Integration + 0` = 15000), a new "Logging rules" implementation-rules subsection, and a new "Logging (EventId allocation)" section documenting that this single-package domain needs no 100-wide sub-block subdivision — the full 15000-15999 block belongs to `SharedKernel.Integration.Webhooks`, with `+1..+999` reserved for future growth or a genuine second delivery-channel package. Test Rules updated to reference the new `Log.ObserverException` method. 5 tasks (LR-01→LR-05) added to `15.Integration/state-map.md` under `SK.15.LoggingRetrofit` — execution-blocked until `01.Core` ships `LoggingEventIdRanges` (P-249, `0/4` done as of this planning pass) (integration-arch-planner, WO-041)
- [2026-07-14] LoggingRetrofit (LR-01→LR-05) shipped — `WebhookDispatcher.LogObserverException` now backed by a `[LoggerMessage]`-attributed `Log.ObserverException(ILogger, Exception, string)` on a nested `Log` class; Logging interface contract corrected to match the shipped signature (added the `Exception ex` parameter) (integration-phase-implementer)
- [2026-08-21] WO-064 gold-standard/big-fintech hardening pass design-locked on top of the `●`-Published v1.0.0 surface (nine root phases, P-421–P-429, dispatched by `arch-lead`; 38-task implementation checklist added to `state-map.md`'s `SK.15.WO064`, all `○` Pending — none of this has shipped yet). Design banner at the top of this file annotates every affected contract "(WO-064)". Summary of what changed in this pass: `WebhookSubscription` gains `Secrets: IReadOnlyList<string>` (newest-first, sign-with-newest/verify-against-any — P-425) with the singular `Secret` retained `[Obsolete]` for back-compat, and an optional `Headers` dictionary with a fail-loud reserved-name-collision rule against the three `WebhookSignatureHeaders` constants (P-426); `WebhookSignatureHeaders` gains `.DeliveryIdHeaderName` (P-423); `WebhookSignatureVerifier.Verify` gains a multi-secret-candidate overload evaluating `FixedTimeEquals` against every candidate without short-circuiting, to avoid leaking which rotation-window secret matched via timing (P-425); `WebhookDeliveryResult` gains `.DeliveryId: Guid`, stable across a delivery's retries (P-423); a new `IWebhookUrlValidator`/`PrivateNetworkWebhookUrlValidator` seam closes a previously-undocumented, genuinely unmitigated SSRF gap — fail-closed by default, checks the DNS-resolved `IPAddress` (not the hostname, closing the DNS-rebinding bypass) immediately before every `SendAsync`, with `WebhookDeliveryOptions.AllowPrivateNetworkTargets` as the sole opt-out (P-422); a new `"SharedKernel.Integration"` `ActivitySource` closes this domain's status as the only outbound-HTTP-issuing infrastructure domain with zero tracing — spans deliberately never tag `Url`/`Secret` (P-424); `IWebhookDispatcher` gains `SendTestDeliveryAsync`, dispatching a new `WebhookPingEvent : IIntegrationEvent` through the exact same `DispatchToSubscriptionAsync` pipeline, zero parallel delivery logic, for subscriber onboarding (P-429); `WebhookDeliveryOptions` gains `EncryptPayload` (opt-in AES-GCM payload encryption via a **new** `SharedKernel.Cryptography` `ProjectReference` — the package's first reference to a `01.Core` package other than `.Primitives`/`.Configuration` — encrypt-then-sign ordering so the HMAC continues to cover the transmitted bytes, mirroring `07.Messaging`'s P-346 payload transform, P-427); the resilience-handler retry/backoff/timeout wiring gap is corrected — `WebhookDeliveryOptions`' four already-validated knobs now actually drive `HttpStandardResilienceOptions` via a documented, non-magic formula, resolved from `ResilienceHandlerContext.ServiceProvider` inside `AddStandardResilienceHandler`'s configuration callback (P-421, the highest-severity item — a previously silent configuration-trust violation); and three new `[LoggerMessage]` entries (`DeliverySucceeded`/`DeliveryFailed`/`DeliveryExhausted` at `+1`/`+2`/`+3`, 15001-15003) extend this domain's logging coverage past the single `ObserverException` statement it has carried since WO-041 (P-428). New Implementation Rules subsections added: "SSRF guard rules" and "Payload encryption rules". AOT Notes, the Logging table, Test Rules, and the DI Registration example all updated to match. No Packages/Layering/package-split change — still exactly one package, `SharedKernel.Integration.Webhooks`, no `.Abstractions` sibling warranted by any of these nine items (integration-arch-planner, WO-064, P-421–P-429)
- [2026-08-21] WO064Hardening (SK.15.WO064, H-01–H-38) implemented and shipped end to end, closing all nine root phases (P-421–P-429) dispatched by `integration-arch-planner` the same day. All nine capabilities described by the design pass above are now real, tested code: `WebhookDeliveryOptions`' four retry/backoff/timeout knobs genuinely drive `HttpStandardResilienceOptions` via `AddStandardResilienceHandler().Configure((options, serviceProvider) => ...)`, with the `MaxAttempts == 1` edge case handled by flooring `Retry.MaxRetryAttempts` at Polly's own `[Range(1, ...)]` minimum and short-circuiting `Retry.ShouldHandle` instead (P-421); `IWebhookUrlValidator`/`PrivateNetworkWebhookUrlValidator` reject loopback/link-local/private/multicast targets by resolved IP before every send, with `AllowPrivateNetworkTargets`/`WithUrlValidator<T>()` as the two opt-outs (P-422); every delivery carries a stable `X-Webhook-Delivery-Id` header and `WebhookDeliveryResult.DeliveryId` (P-423); the `"SharedKernel.Integration"` `ActivitySource` emits `WebhookDispatcher.Dispatch`/`.DispatchToSubscription` spans, never tagging `Url`/`Secret` (P-424); `WebhookSubscription.Secrets` (newest-first, sign-with-newest) and the non-short-circuiting multi-candidate `WebhookSignatureVerifier.Verify` overload ship, with the old single-`Secret` constructor/property retained `[Obsolete]` (P-425); `WebhookSubscription.Headers` applies custom per-subscription headers with a fail-loud reserved-name-collision guard (P-426); `WebhookDeliveryOptions.EncryptPayload` opts into AES-GCM encrypt-then-sign via `SharedKernel.Cryptography`'s `ISymmetricEncryptionService`, resolved from the request-scoped `IServiceProvider` and failing loudly (not silently) when unregistered (P-427); `WebhookDispatcher.Log.DeliverySucceeded`/`.DeliveryFailed`/`.DeliveryExhausted` (EventIds 15001–15003) extend the domain's logging coverage past the single pre-existing `ObserverException` statement (P-428); `IWebhookDispatcher.SendTestDeliveryAsync` dispatches a `WebhookPingEvent` through the real `DispatchToSubscriptionAsync` pipeline verbatim, zero parallel logic (P-429). A new top-level "Tracing" section (mirroring the Logging section's shape) documents the `ActivitySource`/spans/no-`Url`-no-`Secret` tagging rule; the "Logging (EventId allocation)" table's stale "Until WO-064 ships" sentence was removed. `README.md` gained six new sections (SSRF guard opt-out, secret-rotation recipe, custom-headers example, payload-encryption recipe with a verify-then-decrypt subscriber snippet, subscriber-side delivery deduplication, "Testing a new subscription") and its inbound-verification/store-projection samples were updated from the obsolete single-`Secret` shape to `Secrets`; `docs/configuration-reference.md` gained `AllowPrivateNetworkTargets`/`EncryptPayload` rows and a full resilience-handler field-mapping table. Three defects were found and fixed during the GATING regression-test pass (H-03/H-08/H-13/H-17/H-22/H-26/H-30/H-33/H-37), none design errors — all three were either an implementation gap the design already anticipated or a pre-existing shared-infrastructure bug this domain's new tests were the first to exercise: (1) `WebhookDispatcher.SendAsync`'s catch clause only matched `HttpRequestException`/`TaskCanceledException`/`TimeoutException`, letting Polly's own `Timeout.TimeoutRejectedException` (and any other `Polly.ExecutionRejectedException`, e.g. a circuit-breaker rejection) propagate out of `DispatchToSubscriptionAsync` instead of surfacing as a non-throwing `WebhookDeliveryResult` — fixed by broadening the catch filter; (2) `WebhookPingDeliveryTests`' own test code read `HttpRequestMessage.Content` from `StubHttpMessageHandler.Requests` *after* the dispatch call returned, by which point `WebhookDispatcher`'s `using var request = ...` had already disposed it (`ObjectDisposedException`) — fixed by capturing the body/headers synchronously inside the responder callback, mirroring the already-correct pattern in `WebhookPayloadEncryptionTests`; (3) a genuine, previously-latent defect in `16.Testing/SharedKernel.Testing`'s `Logging/InMemoryLogger.cs` — this project's `Microsoft.Extensions.Http.Resilience` dependency transitively pulls in `Microsoft.Extensions.Telemetry`/`.Abstractions`, whose `Microsoft.Gen.Logging` source generator (confirmed via a throwaway `EmitCompilerGeneratedFiles=true` build) replaces the BCL's own `[LoggerMessage]` generator for every method in this compilation and passes a *pooled, thread-local* state object that the generated code clears for reuse immediately after `ILogger.Log(...)` returns; `InMemoryLogger.Log` was storing a live reference to that object into `LogRecord.State` instead of copying it, so `LogRecord.TryGetProperty(...)` read back empty by the time a test inspected it — fixed by taking a `.ToArray()` defensive copy synchronously inside `Log()`, before the generator's own `state.Clear()` runs; verified with zero regressions across `16.Testing/SharedKernel.Testing.SelfTests`' full non-Docker-gated suite (970/970). All 38 `SK.15.WO064` tasks (H-01–H-38) now `●`; 109/109 tests passing in `SharedKernel.Integration.Webhooks.Tests` (integration-phase-implementer)
