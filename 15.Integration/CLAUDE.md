# 15.Integration — Outbound Integration Layer

> **Design status:** Locked (WO-032, Design phase P-200, 2026-06-26). Every contract below has been re-confirmed against the current shape of `SharedKernel.Primitives`, `SharedKernel.Configuration`, `SharedKernel.Contracts`, and `SharedKernel.Messaging.Abstractions` as they exist on disk today — not carried forward unverified from the original 2026-06-25 draft. One correction was made during ratification: `WebhookDeliveryOptions`'s validation mechanism (see Options section below). This is the basis Scaffold (P-201) builds `.csproj` references against.

## What This Domain Is

The outbound third-party integration surface. Today this means exactly one capability: delivering platform integration events to external HTTP subscribers as signed webhooks. Every downstream microservice that needs to notify an external system (a partner API, a customer-configured callback URL) of something that happened — instead of, or in addition to, publishing onto the internal message bus — derives its dispatch, signing, retry, and verification behavior from the types defined here. This domain is delivery plumbing, not business logic — it does not decide *which* events matter to *which* subscriber; it only delivers a given integration event to a given subscriber reliably and verifiably.

Philosophy: **Thin. Transport-agnostic of the internal bus. Signed by default. Storage-agnostic.**

> **Layering boundary:** Per root `CLAUDE.md`, `15.Integration` may reference `01.Core`, `04.Contracts`, and `SharedKernel.Messaging.Abstractions` (07.Messaging abstractions only) — never `06.Persistence`, `11.Communication`, `07.Messaging.MassTransit`, or any other infrastructure layer. Two consequences fall directly out of that boundary and are documented in detail below: (1) outbound HTTP delivery cannot go through `SharedKernel.Communication.Rest`'s typed-client builder (`AddRestClient<TClient>()`) — it must go directly through `IHttpClientFactory`, the underlying ASP.NET Core abstraction that `11.Communication.Rest` itself wraps; (2) subscription storage and delivery-history persistence cannot live here — this domain defines only the seams (`IWebhookSubscriptionStore`, `IWebhookDeliveryObserver`) and the consuming microservice supplies storage-backed implementations using its own `06.Persistence` stack.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Integration.Webhooks` | Outbound webhook subscription contract, HMAC-SHA256 signing + replay-resistant verification, retrying signed dispatch, delivery-exhausted integration event | `SharedKernel.Primitives`, `SharedKernel.Configuration`, `SharedKernel.Contracts`, `SharedKernel.Messaging.Abstractions`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience` |

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
    .Url             → Uri
    .Secret          → string   (shared HMAC-SHA256 key — see Implementation Rules; never logged, never sent on the wire)
    .EventTypes      → IReadOnlyList<string>   (empty = subscribed to every event type)
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
          WebhookDeliveryExhaustedEvent via IEventPublisher before returning the failed result.

WebhookDeliveryResult  (sealed record)
    .SubscriptionId  → Guid
    .IsSuccess       → bool
    .StatusCode      → int?      (HTTP status of the final attempt; null if every attempt faulted before a response was received)
    .Attempts        → int       (1-based count of HTTP attempts actually made)
    .Error           → string?   (non-null only when IsSuccess == false)
    NOTE: IsSuccess is true only when some attempt within MaxAttempts received a 2xx response. Every other
          terminal outcome — non-2xx exhausted, timeout exhausted, transport exception exhausted — is
          IsSuccess == false with Error populated and StatusCode reflecting the last attempt if one exists.
```

#### Signing and verification (`Signing/`)

```text
WebhookSignatureHeaders  (static class)
    .SignatureHeaderName  → "X-Webhook-Signature"
    .TimestampHeaderName  → "X-Webhook-Timestamp"
    NOTE: Single source of truth for both header names. WebhookDispatcher and WebhookSignatureVerifier must
          both reference these constants — never a literal header-name string — so the two sides cannot
          silently drift.

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
```

#### Logging (`Dispatch/`)

```text
WebhookDispatcher — Log  (private static partial class nested inside WebhookDispatcher)
    ObserverException(ILogger logger, string observerType)   [LoggerMessage, EventId = LoggingEventIdRanges.Integration + 0 (= 15000), Level = Warning]
    NOTE: Backs the single shared LogObserverException(Exception ex, string observerTypeName) helper called from both
          NotifyAttemptAsync and NotifyCompletedAsync when an IWebhookDeliveryObserver implementation throws. This is
          the only production log statement in SharedKernel.Integration.Webhooks today (P-257, WO-041 logging
          retrofit) — see "Logging (EventId allocation)" below.
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
```

#### DI extensions (`Extensions/`)

```text
AddSharedKernelWebhooks(this IServiceCollection services, Action<WebhookDeliveryOptions>? configure = null)
    → IServiceCollection
    NOTE: Registers WebhookDeliveryOptions (+ eager validator), WebhookSignatureProvider (singleton —
          stateless), IWebhookDispatcher → WebhookDispatcher (scoped), and a named HttpClient
          ("SharedKernel.Integration.Webhooks") via AddHttpClient(...).AddStandardResilienceHandler(...)
          (Microsoft.Extensions.Http.Resilience) configured from the resolved WebhookDeliveryOptions.
          Does NOT register IWebhookSubscriptionStore (required — the consuming service must register its
          own implementation or DI resolution fails at first use) or any IWebhookDeliveryObserver (optional).

WithDeliveryObserver<TObserver>(this IServiceCollection services) → IServiceCollection
    where TObserver : class, IWebhookDeliveryObserver
    NOTE: Registers TObserver as scoped. Additive — multiple calls accumulate; every registered observer
          fires for every delivery attempt and completion, in registration order.
```

---

## Implementation Rules

### Hard violations (never do these)

- Constructing `new HttpClient()` anywhere in this package, or injecting a raw `HttpClient` into any constructor — `IHttpClientFactory` is the only permitted source, and only via the named client registered by `AddSharedKernelWebhooks`.
- `WebhookSubscription.Secret` appearing in a log statement, an exception message, an outbound request body, or any header other than as the *input* to `WebhookSignatureProvider.Sign` — only the derived HMAC digest is ever transmitted or surfaced.
- Comparing a computed signature digest to a received one with `==`, `string.Equals`, or any non-constant-time comparison — `CryptographicOperations.FixedTimeEquals` is mandatory inside `WebhookSignatureVerifier`.
- `WebhookSignatureVerifier.Verify` throwing for any malformed input — malformed timestamp, malformed signature, or a missing header must all produce `false`, never an exception.
- Adding a `DbContext`, `IRepository<T,TId>`, or any other `06.Persistence` type to this package — subscription storage and delivery-history persistence are the consuming service's responsibility, expressed only through `IWebhookSubscriptionStore` and `IWebhookDeliveryObserver`.
- Adding a `ProjectReference` to any `11.Communication.*` package — outbound HTTP goes directly through `IHttpClientFactory`; see "Why not `SharedKernel.Communication.Rest`" above.
- Adding a `ProjectReference` to `SharedKernel.Messaging.MassTransit` — only `SharedKernel.Messaging.Abstractions` (`IEventPublisher`) is within this domain's layering allowance.
- Letting an `IWebhookDeliveryObserver` implementation's exception propagate out of `IWebhookDispatcher` — observer calls are wrapped in try/catch with `LogLevel.Warning` logging; a faulty observer must never affect delivery outcome.
- Publishing `WebhookDeliveryExhaustedEvent` more than once per exhausted delivery, or publishing it for an attempt that has not yet exhausted `WebhookDeliveryOptions.MaxAttempts`.
- `IWebhookDispatcher.DispatchAsync`/`DispatchToSubscriptionAsync` throwing because of an individual subscription's HTTP failure — per-subscription outcomes surface as a `WebhookDeliveryResult`, never an exception, so one unreachable endpoint cannot fail an entire fan-out.
- Any static mutable state.

### Signing convention rules

- The signing input is always `"{unixSeconds}.{payloadJson}"`, UTF-8 encoded — never the payload alone. Signing the payload alone provides authenticity but not freshness, allowing a captured request to be replayed indefinitely.
- `WebhookSignatureHeaders.SignatureHeaderName` and `.TimestampHeaderName` are the only permitted header name literals — both the dispatcher (writing headers) and the verifier (reading them) reference these constants.
- `WebhookDeliveryOptions.SignatureTolerance` (default 5 minutes) is the only permitted clock-skew allowance for `WebhookSignatureVerifier.Verify` — do not hardcode a different window at a call site.

### Delivery rules

- `IWebhookDispatcher` resolves the event-type routing key as `typeof(TEvent).Name` — matching `EventEnvelope<TEvent>.EventType`'s convention in `04.Contracts` so the same event type routes identically over `07.Messaging` and over webhooks.
- Retry/backoff is configured once, on the named `HttpClient`, via `Microsoft.Extensions.Http.Resilience`'s standard resilience handler — never a hand-rolled retry loop inside `WebhookDispatcher`.
- `WebhookDeliveryOptions.MaxConcurrentDeliveries` bounds the fan-out in `DispatchAsync` — unbounded `Task.WhenAll` over an arbitrarily large subscription list is a hard violation.

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

---

## Logging (EventId allocation — P-257, WO-041)

`15.Integration` reserves `LoggingEventIdRanges.Integration` (15000-15999, from `SharedKernel.Primitives` — `01.Core` P-249) as its platform-wide `EventId` block. Unlike every other domain retrofitted under WO-041, this is a **single-package domain** (`SharedKernel.Integration.Webhooks` — no `.Abstractions` sibling, per the package-split discipline in the Packages section above), so the root registry's 100-wide-per-package sub-block subdivision rule does not apply here: subdivision is required only "when a domain has multiple packages." The entire 15000-15999 block belongs to `SharedKernel.Integration.Webhooks` today.

| Type | EventId | Level | Trigger |
| --- | --- | --- | --- |
| `WebhookDispatcher.Log.ObserverException` | `LoggingEventIdRanges.Integration + 0` (15000) | Warning | An `IWebhookDeliveryObserver` implementation's `OnAttemptAsync`/`OnCompletedAsync` throws; the exception is caught and logged, never propagated (see the observer-isolation hard violation above) |

This is the domain's only production log statement. Offsets `+1` through `+999` (15001-15999) stay reserved for future logging additions to this package, or — should a genuinely new second outbound delivery channel ever warrant the `.Abstractions` + `.{Provider}` split described in the Packages section — for a second package's own 100-wide sub-block, at which point this table must be re-partitioned following the same declaration-order convention `02.Caching` established.

Every `[LoggerMessage(EventId = ...)]` value in this package must be written as `LoggingEventIdRanges.Integration + {offset}` — never a bare literal integer. `SharedKernel.Integration.Webhooks.csproj` already carries a `<ProjectReference>` to `SharedKernel.Primitives` (added under S-01/D-01 for future-proofing before this package had any logging call site) — P-257 is the first phase to actually consume it.

---

## DI Registration (planned shape)

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

// Dispatching an integration event as a webhook (application-layer call site)
public sealed record OrderShippedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId)
    : IIntegrationEvent;

var results = await webhookDispatcher.DispatchAsync(
    new OrderShippedIntegrationEvent(Guid.NewGuid(), clock.UtcNow, order.Id), ct);

// Verifying an inbound webhook claiming to come from this dispatcher (14.Presentation receiver endpoint)
var isValid = WebhookSignatureVerifier.Verify(
    payloadJson: rawBody,
    timestampHeaderValue: request.Headers[WebhookSignatureHeaders.TimestampHeaderName],
    signatureHeaderValue: request.Headers[WebhookSignatureHeaders.SignatureHeaderName],
    secret: subscription.Secret);
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
