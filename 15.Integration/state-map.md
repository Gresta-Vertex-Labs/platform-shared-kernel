# 15.Integration — State Map

> **What this file is:** Phase and task tracker for all work within `15.Integration`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.15.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
| --- | --- | --- |
| `SK.15.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.15.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.15.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.15.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.15.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.15.Published` | Published | All tasks in Phase: Published are `●` |
| `SK.15.LoggingRetrofit` | Logging Retrofit (P-257, WO-041) | All tasks in Phase: LoggingRetrofit are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Define IWebhookDispatcher contract | SK.15.Design | SharedKernel.Integration.Webhooks | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.15.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Integration.Webhooks` | Published (Logging Retrofit planned) | `●` (LoggingRetrofit `○`, execution-blocked) | Design, Scaffold, Core, Tests, Docs, and Published all complete — domain finished end to end. Signing: `WebhookSignatureHeaders`, `WebhookSignatureProvider` (HMAC-SHA256), `WebhookSignatureVerifier` (constant-time `FixedTimeEquals`, never throws). Dispatch: `WebhookSubscription`/`IWebhookSubscriptionStore`, `WebhookDeliveryResult`, `WebhookDeliveryOptions` (DataAnnotations + `IValidatableObject`), `WebhookDeliveryExhaustedEvent`, `IWebhookDeliveryObserver`, `WebhookDispatcher` (bounded fan-out via `SemaphoreSlim`, named `HttpClient` + `Microsoft.Extensions.Http.Resilience` standard resilience handler for retry/backoff, exhaustion publishes `WebhookDeliveryExhaustedEvent` via `IEventPublisher` exactly once), `AddSharedKernelWebhooks`/`WithDeliveryObserver<T>()` DI extensions. Full NuGet packaging metadata added (mirrors `12.Security`/`13.ServiceDefaults`/`14.Presentation` convention); `.nupkg`+`.snupkg` pack cleanly with zero warnings (`artifacts/nupkg/`). New `15.Integration/consumer-verify` harness (registered in `Platform.SharedKernel.slnx`) proves `AddSharedKernelWebhooks()` + a registered `IWebhookSubscriptionStore` resolves `IWebhookDispatcher` and completes a real `DispatchAsync` call with zero DI exceptions, and proves omitting `IWebhookSubscriptionStore` produces an immediate, actionable `InvalidOperationException` naming the missing type at first `GetRequiredService<IWebhookDispatcher>()` — not a silent null/no-op. 48/48 tests still passing. **P-257 (WO-041) planned on top of Published**: `WebhookDispatcher`'s single production log statement (`LogObserverException`, a shared private helper called from both `NotifyAttemptAsync` and `NotifyCompletedAsync`, currently a direct `_logger.LogWarning(...)` call) must convert to the `[LoggerMessage]` pattern with an `EventId` inside this domain's reserved `LoggingEventIdRanges.Integration` (15000-15999) block — execution-blocked until `01.Core` ships `LoggingEventIdRanges` (P-249, `○` Pending as of this planning pass). |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.15.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Error` — not consumed by this package's public surface today; referenced for future-proofing only, per D-01) | Available |
| `SK.15.Core` | `01.Core` | `SharedKernel.Configuration` ProjectReference — `OptionsExtensions.AddValidatedOptions<TOptions>(IConfigurationSection)` (DataAnnotations-based: `.Bind().ValidateDataAnnotations().ValidateOnStart()`); `WebhookDeliveryOptions` validates via DataAnnotations attributes + `IValidatableObject` for the cross-field `MaxBackoffDelay ≥ BaseBackoffDelay` check — confirmed D-04, corrects the original brain's vaguer "Options-pattern validator" phrasing | Available |
| `SK.15.Scaffold` | `04.Contracts` | `SharedKernel.Contracts` ProjectReference — `IIntegrationEvent` confirmed minimal (`EventId: Guid`, `OccurredOn: DateTimeOffset` only); `EventEnvelope<TEvent>.EventType` confirmed derived as `typeof(TEvent).Name` (constrains on `IDomainEvent`, a convention parallel — not a shared generic constraint with this domain's `IIntegrationEvent`-based routing) — confirmed D-02 | Available |
| `SK.15.Core` | `07.Messaging` | `SharedKernel.Messaging.Abstractions` ProjectReference — `IEventPublisher.PublishAsync<TEvent>(TEvent, CancellationToken)` confirmed `where TEvent : class` (no `IIntegrationEvent`/`IDomainEvent` constraint) — `WebhookDeliveryExhaustedEvent` still implements `IIntegrationEvent` for cross-domain convention consistency, not because `IEventPublisher` requires it — confirmed D-03 | Available |
| `SK.15.LoggingRetrofit` | `01.Core` | `SharedKernel.Primitives.Logging.LoggingEventIdRanges.Integration` (= 15000) — P-249's registry constant, consumed via the `SharedKernel.Primitives` ProjectReference already present in this package's `.csproj` (added under S-01/D-01, previously unconsumed) | Blocked (P-249 is `○` Pending in `01.Core/state-map.md` as of 2026-07-09) |
| `SK.15.LoggingRetrofit` | `00.Governance` | SK0020 (`DirectILoggerExtensionMethodUsage`) / SK0021 (`HandWrittenLoggerMessageDefineDelegate`) analyzers + `LoggingEventIdIntegrityAssertion` (P-250) — verification tooling only, not a compile-time dependency for this phase's own code change | Not blocking compilation; blocking full acceptance sign-off only |

---

## Phase: Design <!-- phase-key: SK.15.Design -->

> Finalize all interface contracts, signing/verification rules, and DI extension signatures before any implementation begins.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Re-confirm `SharedKernel.Primitives` (`Result<T>`, `Error`, namespace `SharedKernel.Primitives.Results`/`.Errors`) against current source; ratify that neither type is consumed by this domain's public surface today (`WebhookDeliveryResult` is a domain-specific outcome shape, not a `Result<T>` wrapper) — `SharedKernel.Primitives` ProjectReference kept for future-proofing only, not removed | SharedKernel.Integration.Webhooks | `●` |
| D-02 | Re-confirm `SharedKernel.Contracts`'s `IIntegrationEvent` (`04.Contracts\SharedKernel.Contracts\Events\IIntegrationEvent.cs`) — verbatim two-member shape (`Guid EventId`, `DateTimeOffset OccurredOn`) — and `EventEnvelope<TEvent>.EventType` derivation (`typeof(TEvent).Name`, constrained on `IDomainEvent`); ratify `IWebhookDispatcher`'s `typeof(TEvent).Name` routing-key convention as a deliberate parallel to `EventEnvelope<TEvent>`, not a shared generic constraint | SharedKernel.Integration.Webhooks | `●` |
| D-03 | Re-confirm `SharedKernel.Messaging.Abstractions`'s `IEventPublisher` (`07.Messaging\SharedKernel.Messaging.Abstractions\EventPublisher\IEventPublisher.cs`) — two `PublishAsync<TEvent>` overloads, both `where TEvent : class` (no `IIntegrationEvent`/`IDomainEvent` constraint); ratify that `WebhookDeliveryExhaustedEvent` still implements `IIntegrationEvent` for cross-domain convention consistency (uniform `EventId`/`OccurredOn` shape across the platform's integration events), not because `IEventPublisher` requires it | SharedKernel.Integration.Webhooks | `●` |
| D-04 | Re-confirm `SharedKernel.Configuration`'s actual options-validation API (`01.Core\SharedKernel.Configuration\Extensions\OptionsExtensions.cs`) — `AddValidatedOptions<TOptions>(this IServiceCollection, IConfigurationSection)`, DataAnnotations-based (`.Bind(section).ValidateDataAnnotations().ValidateOnStart()`), no custom `IValidateOptions<T>` contract or validator class. **Correction to original brain:** `WebhookDeliveryOptions` must carry DataAnnotations attributes (`[Range]` on `MaxAttempts`/`MaxConcurrentDeliveries`) plus implement `IValidatableObject` for the cross-field `MaxBackoffDelay ≥ BaseBackoffDelay` and positive-`TimeSpan` checks DataAnnotations attributes alone cannot express — not a free-standing validator type as the original phrasing implied | SharedKernel.Integration.Webhooks | `●` |
| D-05 | Re-ratify zero-reference confirmation: no direct or transitive reference to `06.Persistence`, `11.Communication.*`, or `07.Messaging.MassTransit` anywhere in the locked design — verified by inspecting the four confirmed dependency surfaces (D-01–D-04), none of which themselves reference a forbidden layer | SharedKernel.Integration.Webhooks | `●` |
| D-06 | Ratify `WebhookSignatureHeaders` (`X-Webhook-Signature`, `X-Webhook-Timestamp`) as the single source of truth for both header names, and the timestamp-prefixed signing-input convention `"{unixSeconds}.{payloadJson}"` (UTF-8) as the only permitted signing input — both referenced by `WebhookSignatureProvider.Sign` and `WebhookSignatureVerifier.Verify`, never a literal header-name string or payload-only signing input | SharedKernel.Integration.Webhooks | `●` |
| D-07 | Lock final Interface Contracts section of `15.Integration/CLAUDE.md` as the basis for Scaffold (P-201): `WebhookSubscription`/`IWebhookSubscriptionStore`, `IWebhookDispatcher`/`WebhookDeliveryResult`, `WebhookSignatureHeaders`/`WebhookSignatureProvider`/`WebhookSignatureVerifier`, `WebhookDeliveryExhaustedEvent`, `IWebhookDeliveryObserver`, `WebhookDeliveryOptions` (with D-04's correction applied), `AddSharedKernelWebhooks()`/`WithDeliveryObserver<T>()` | SharedKernel.Integration.Webhooks | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.15.Scaffold -->

> Wire up `.csproj` NuGet references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Create `SharedKernel.Integration.Webhooks.csproj` (`net10.0`, `ImplicitUsings` enabled, `Nullable` enabled) with `ProjectReference`s limited exactly to `SharedKernel.Primitives`, `SharedKernel.Configuration`, `SharedKernel.Contracts`, `SharedKernel.Messaging.Abstractions` — no more, no less, per D-01–D-05 | SharedKernel.Integration.Webhooks | `●` |
| S-02 | Add `PackageReference`s to `Microsoft.Extensions.Http` and `Microsoft.Extensions.Http.Resilience` | SharedKernel.Integration.Webhooks | `●` |
| S-03 | Create folder structure matching the locked Interface Contracts layout: `Subscriptions/`, `Dispatch/`, `Signing/`, `Events/`, `Observability/`, `Options/`, `Extensions/` — empty stub files only, no logic | SharedKernel.Integration.Webhooks | `●` |
| S-04 | Create `SharedKernel.Integration.Webhooks.Tests.csproj` nested inside the package folder (`net10.0` classlib) with `ProjectReference`s to `SharedKernel.Integration.Webhooks` and `SharedKernel.Testing`, standard test package set (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`, `FluentAssertions`), and `GlobalUsings.cs` with `global using Xunit;` | SharedKernel.Integration.Webhooks.Tests | `●` |
| S-05 | Register both projects in `Platform.SharedKernel.slnx` under the `15.Integration` solution folder | SharedKernel.Integration.Webhooks, SharedKernel.Integration.Webhooks.Tests | `●` |
| S-06 | Verify `dotnet build` succeeds with zero errors and zero warnings on the empty scaffold | SharedKernel.Integration.Webhooks, SharedKernel.Integration.Webhooks.Tests | `●` |

---

## Phase: Core <!-- phase-key: SK.15.Core -->

> Full implementation of subscription contracts, signing/verification, the retrying dispatcher, and DI registration. Split into two independently-verifiable sub-passes — Signing (pure, stateless cryptographic primitives) before Dispatch (subscription seam, fan-out, retry, DI) — so the dispatcher can be built and tested on top of an already-proven signing layer rather than re-proving cryptographic correctness as a side effect of dispatcher tests.

**Core (Signing) — C-01 through C-06:**

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `WebhookSignatureHeaders` static class — `SignatureHeaderName = "X-Webhook-Signature"`, `TimestampHeaderName = "X-Webhook-Timestamp"` — single source of truth, no literal header-name strings permitted elsewhere | SharedKernel.Integration.Webhooks | `●` |
| C-02 | Implement `WebhookSignatureProvider.Sign(string payloadJson, string secret, DateTimeOffset timestamp) → string` — HMAC-SHA256 (`System.Security.Cryptography.HMACSHA256`) over UTF-8 `"{unixSeconds}.{payloadJson}"` keyed by `secret`, returns lowercase hex digest; stateless, registered as a singleton | SharedKernel.Integration.Webhooks | `●` |
| C-03 | Implement `WebhookSignatureVerifier.Verify(string payloadJson, string timestampHeaderValue, string signatureHeaderValue, string secret, TimeSpan? tolerance = null) → bool` (static class) — default tolerance 5 minutes; never throws for malformed timestamp/signature/missing values, always returns `false` instead | SharedKernel.Integration.Webhooks | `●` |
| C-04 | Wire `CryptographicOperations.FixedTimeEquals` as the exclusive digest-comparison mechanism inside `Verify` — zero `==`/`string.Equals` comparisons against the digest anywhere in the implementation | SharedKernel.Integration.Webhooks | `●` |
| C-05 | Confirm zero reflection and zero allocation beyond what `HMACSHA256`/string formatting requires in both `WebhookSignatureProvider` and `WebhookSignatureVerifier`; confirm full AOT compatibility (BCL only) | SharedKernel.Integration.Webhooks | `●` |
| C-06 | Round-trip test (sign then verify succeeds), tamper tests (mutated payload or header fails), expired-timestamp test (outside tolerance fails regardless of digest correctness), malformed-input tests (never throws) — delivered alongside C-01–C-05, not deferred to a standalone Tests pass | SharedKernel.Integration.Webhooks.Tests | `●` |

**Core (Dispatch) — C-07 through C-17:**

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-07 | Implement `WebhookSubscription` sealed record (`SubscriptionId: Guid`, `Url: Uri`, `Secret: string`, `EventTypes: IReadOnlyList<string>`, `IsActive: bool`) — pure DTO, no behavior, no serialization/storage in this package | SharedKernel.Integration.Webhooks | `●` |
| C-08 | Implement `IWebhookSubscriptionStore` interface (`GetActiveSubscriptionsAsync(string eventType, CancellationToken ct) → Task<IReadOnlyList<WebhookSubscription>>`) — interface only, no default implementation; consuming service supplies it | SharedKernel.Integration.Webhooks | `●` |
| C-09 | Implement `WebhookDeliveryResult` sealed record (`SubscriptionId: Guid`, `IsSuccess: bool`, `StatusCode: int?`, `Attempts: int`, `Error: string?`) | SharedKernel.Integration.Webhooks | `●` |
| C-10 | Implement `WebhookDeliveryOptions` options POCO (section `SharedKernel:Integration:Webhooks`) with DataAnnotations attributes (`[Range]` on `MaxAttempts`/`MaxConcurrentDeliveries`) plus `IValidatableObject` for the cross-field `MaxBackoffDelay ≥ BaseBackoffDelay` and positive-`TimeSpan` checks DataAnnotations attributes alone cannot express — per D-04's correction, registered via `SharedKernel.Configuration`'s `AddValidatedOptions<TOptions>(IConfigurationSection)` | SharedKernel.Integration.Webhooks | `●` |
| C-11 | Implement `WebhookDeliveryExhaustedEvent` sealed record implementing `IIntegrationEvent` (`EventId: Guid`, `OccurredOn: DateTimeOffset`, `SubscriptionId: Guid`, `EventType: string`, `Attempts: int`, `LastError: string?`) | SharedKernel.Integration.Webhooks | `●` |
| C-12 | Implement `IWebhookDeliveryObserver` interface (`OnAttemptAsync`, `OnCompletedAsync`) — optional, zero-or-more registered via `WithDeliveryObserver<T>()`; an observer's exception caught and logged at `LogLevel.Warning`, never propagated | SharedKernel.Integration.Webhooks | `●` |
| C-13 | Implement `WebhookDispatcher : IWebhookDispatcher` — `DispatchAsync<TEvent>` (fan-out via `IWebhookSubscriptionStore.GetActiveSubscriptionsAsync(typeof(TEvent).Name, ct)`, bounded by `WebhookDeliveryOptions.MaxConcurrentDeliveries`, no unbounded `Task.WhenAll`) and `DispatchToSubscriptionAsync<TEvent>` (single-subscription path, backed by the named `HttpClient` resolved via `IHttpClientFactory` — never `new HttpClient()` or raw injection) | SharedKernel.Integration.Webhooks | `●` |
| C-14 | Wire retry/backoff exclusively through `Microsoft.Extensions.Http.Resilience`'s standard resilience handler configured once on the named `HttpClient` (`AddHttpClient(...).AddStandardResilienceHandler(...)`) — no hand-rolled retry loop inside `WebhookDispatcher` | SharedKernel.Integration.Webhooks | `●` |
| C-15 | Wire exhaustion handling — on exhausting `MaxAttempts` without a 2xx response, publish exactly one `WebhookDeliveryExhaustedEvent` via `IEventPublisher.PublishAsync` (confirmed `where TEvent : class`, per D-03) before returning the failed `WebhookDeliveryResult`; per-subscription HTTP failures always surface as `WebhookDeliveryResult` with `IsSuccess == false`, never a thrown exception | SharedKernel.Integration.Webhooks | `●` |
| C-16 | Implement `AddSharedKernelWebhooks(this IServiceCollection, Action<WebhookDeliveryOptions>? configure = null)` and `WithDeliveryObserver<TObserver>(this IServiceCollection)` DI extensions — registers options+validator, `WebhookSignatureProvider` (singleton), `IWebhookDispatcher → WebhookDispatcher` (scoped), named `HttpClient` with standard resilience handler; deliberately does not register `IWebhookSubscriptionStore` (required, consumer-supplied) or any `IWebhookDeliveryObserver` (optional, consumer-supplied) | SharedKernel.Integration.Webhooks | `●` |
| C-17 | Fan-out test (N active subscriptions, inactive/non-matching excluded, one failure does not affect others), retry/backoff test (transient failure retried up to `MaxAttempts`, success on a later attempt reflected in `Attempts`), exhaustion test (exactly one `WebhookDeliveryExhaustedEvent` via `16.Testing`'s `InMemoryEventPublisher.ShouldHavePublishedOnce<WebhookDeliveryExhaustedEvent>()`), observer-exception-isolation test, options-validator rejection tests (zero `MaxAttempts`, `MaxBackoffDelay < BaseBackoffDelay`, non-positive `TimeSpan`) — delivered alongside C-07–C-16, not deferred to a standalone Tests pass | SharedKernel.Integration.Webhooks.Tests | `●` |

---

## Phase: Tests <!-- phase-key: SK.15.Tests -->

> Unit test coverage for signing/verification, retry/backoff, fan-out dispatch, and delivery-exhausted notification. Coverage is delivered alongside Core (C-06 for Signing, C-17 for Dispatch) rather than as a deferred standalone pass — this phase tracks the consolidated coverage checklist, not new work.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Confirm signing/verification coverage delivered under C-06: round-trip, tamper (mutated payload, mutated header), expired-timestamp, malformed-input (never throws) | SharedKernel.Integration.Webhooks.Tests | `●` |
| T-02 | Confirm dispatch coverage delivered under C-17: fan-out isolation, retry/backoff with attempt counting, exhaustion publishes exactly once, observer-exception isolation, options-validator rejections | SharedKernel.Integration.Webhooks.Tests | `●` |
| T-03 | Confirm HTTP delivery tests stub the named `HttpClient` via a fake `DelegatingHandler` registered through `IHttpClientFactory` test wiring — no real network calls, no Testcontainers used anywhere in this package's test suite | SharedKernel.Integration.Webhooks.Tests | `●` |

---

## Phase: Docs <!-- phase-key: SK.15.Docs -->

> XML doc comments on all public APIs, README with usage examples, configuration reference.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | Add XML doc comments to every public type and member across `Subscriptions/`, `Dispatch/`, `Signing/`, `Events/`, `Observability/`, `Options/`, `Extensions/`; enable XML documentation file generation in the `.csproj` with zero missing-doc warnings | SharedKernel.Integration.Webhooks | `●` |
| DO-02 | Write README covering: minimal setup (`AddSharedKernelWebhooks()` + consumer-supplied `IWebhookSubscriptionStore`), custom `WebhookDeliveryOptions`, `WithDeliveryObserver<T>()` registration, dispatching an integration event (`IWebhookDispatcher.DispatchAsync`), and verifying an inbound webhook (`WebhookSignatureVerifier.Verify`) from a `14.Presentation` receiver endpoint | SharedKernel.Integration.Webhooks | `●` |
| DO-03 | Write configuration reference for `WebhookDeliveryOptions` — every property, default value, validation bound, and the `SharedKernel:Integration:Webhooks` configuration section path | SharedKernel.Integration.Webhooks | `●` |

---

## Phase: Published <!-- phase-key: SK.15.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Add full NuGet packaging metadata (`PackageId`, `Description`, `Authors`, `RepositoryUrl`, `PackageTags`, etc.) to `SharedKernel.Integration.Webhooks.csproj` | SharedKernel.Integration.Webhooks | `●` |
| P-02 | Run `dotnet pack` producing `.nupkg` + `.snupkg` with zero warnings | SharedKernel.Integration.Webhooks | `●` |
| P-03 | Consumer-verify harness proving `AddSharedKernelWebhooks()` + a registered `IWebhookSubscriptionStore` resolves `IWebhookDispatcher` from the container with zero DI exceptions | SharedKernel.Integration.Webhooks | `●` |
| P-04 | Consumer-verify harness proving omitting `IWebhookSubscriptionStore` registration produces a clear, actionable DI resolution failure — not a silent null or no-op | SharedKernel.Integration.Webhooks | `●` |
| P-05 | Update Package Board to reflect `SharedKernel.Integration.Webhooks` at `Published`/`●` | SharedKernel.Integration.Webhooks | `●` |

---

## Phase: LoggingRetrofit <!-- phase-key: SK.15.LoggingRetrofit -->

> Retrofit the single production log statement in `SharedKernel.Integration.Webhooks` to the platform-mandated `[LoggerMessage]` source-generated pattern, and give it an `EventId` inside this domain's own `LoggingEventIdRanges.Integration` (15000-15999) reserved block. Dispatched from WO-041's root logging-standard directive (P-257), depends on `01.Core` P-249 (`LoggingEventIdRanges` registry design) and `00.Governance` P-250 (SK0020/SK0021 analyzer + `LoggingEventIdIntegrityAssertion` design).

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| LR-01 | Confirm the existing `<ProjectReference>` to `SharedKernel.Primitives` in `SharedKernel.Integration.Webhooks.csproj` (added under S-01/D-01 for future-proofing, previously unconsumed by any public surface) now resolves `SharedKernel.Primitives.Logging.LoggingEventIdRanges.Integration`; no `.csproj` change required — this is a verification-only task, unlike every other WO-041 domain phase, which each needed a *new* `<ProjectReference>` | SharedKernel.Integration.Webhooks | `○` |
| LR-02 | Convert `WebhookDispatcher.LogObserverException(Exception ex, string observerTypeName)` — the single shared private logging helper called from both `NotifyAttemptAsync` and `NotifyCompletedAsync` — from a direct `_logger.LogWarning(ex, "Webhook delivery observer {ObserverType} threw an exception; delivery outcome is unaffected.", observerTypeName)` call to a `[LoggerMessage]`-attributed partial method (`ObserverException`) on a new nested `private static partial class Log`, `EventId = LoggingEventIdRanges.Integration + 0` (= 15000), `Level = LogLevel.Warning`, identical message template and `{ObserverType}` placeholder; `WebhookDispatcher` becomes `sealed partial class` to host the nested partial `Log` class's generated members | SharedKernel.Integration.Webhooks | `○` |
| LR-03 | Sweep `SharedKernel.Integration.Webhooks` production source confirming zero remaining direct `ILogger.LogInformation/LogWarning/LogError/LogDebug/LogCritical/LogTrace` extension-method calls and zero hand-written `LoggerMessage.Define`/`LoggerMessage.DefineScope` delegates anywhere in the package (this is the domain's only log statement, so the sweep is expected to find none after LR-02) | SharedKernel.Integration.Webhooks | `○` |
| LR-04 | Update `15.Integration/CLAUDE.md`: add a "Logging (EventId allocation)" subsection documenting `LoggingEventIdRanges.Integration` (15000-15999) as reserved to this single-package domain with no sub-block subdivision needed today (per the root registry's own rule that subdivision is required only for a domain with multiple packages) — `ObserverException` at `+0` (15000), remainder of the 15000-15999 block reserved for future logging additions to this or a future second package; add a "Logging rules" implementation-rules subsection mirroring the platform-wide `[LoggerMessage]`-only mandate | — | `○` |
| LR-05 | Regression-run all existing `SharedKernel.Integration.Webhooks.Tests` (48 tests, including `Observability/WebhookDeliveryObserverTests.cs`'s observer-exception-isolation coverage) confirming zero behavioral change to the log message text, level, or `{ObserverType}` structured property name — only the `EventId` value and authoring mechanism change; the existing observer-exception test already asserts the delivery outcome is unaffected by a throwing observer and needs no new assertion to cover this conversion | SharedKernel.Integration.Webhooks.Tests | `○` |

### LoggingRetrofit — Why this domain's retrofit is the smallest of the ten WO-041 domain phases

`SharedKernel.Integration.Webhooks` has exactly one production log statement in the entire package — `WebhookDispatcher.LogObserverException`, a single private helper invoked from two call sites (`NotifyAttemptAsync`, `NotifyCompletedAsync`) rather than two separately-authored log statements. There is no pre-existing `EventId` collision to fix here (unlike `02.Caching`'s confirmed 4001/4002 collision) and no hand-written `LoggerMessage.Define` delegate to convert (unlike `02.Caching`'s `RedisCacheInvalidationBus`) — this phase exists purely so a platform-wide standard has zero exceptions, per P-257's own stated rationale. Because there is only one package in this domain today (no `.Abstractions` sibling, per the package-split discipline documented in the Packages section above), no 100-wide sub-block subdivision is needed — the single log statement is simply assigned `LoggingEventIdRanges.Integration + 0`, and the remaining 15001-15999 offsets stay reserved for this package's future growth or a genuinely new second delivery-channel package, should one ever be introduced.

### LoggingRetrofit — Dependencies

- Requires `01.Core` P-249 to be **implemented** (not merely designed) before `LoggingEventIdRanges.Integration` physically exists for LR-02's `[LoggerMessage(EventId = LoggingEventIdRanges.Integration + 0)]` expression to compile. As of this phase's authoring (2026-07-09), `01.Core`'s `SK.01.P249` tasks are `0/4` done (`○` Pending) — this phase is planned now but **execution-blocked** until `01.Core` ships `LoggingEventIdRanges`.
- Does **not** require `00.Governance` P-250 to be implemented for this phase's own code change to compile or its own tests to pass — SK0020/SK0021/`LoggingEventIdIntegrityAssertion` are verification tooling, not a compile-time dependency. Full acceptance (per P-257's own acceptance criteria) does require `00.Governance` P-250 to have shipped by the time this phase is verified.
- Unblocks: closes the last of `15.Integration`'s zero-exception gaps in the platform-wide `[LoggerMessage]` logging standard (WO-041).

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | --- | --- | --- | --- |
| `SK.15.Design` | Design | 7 | 7 | 0 | `●` |
| `SK.15.Scaffold` | Scaffold | 6 | 6 | 0 | `●` |
| `SK.15.Core` | Core | 17 | 17 | 0 | `●` |
| `SK.15.Tests` | Tests | 3 | 3 | 0 | `●` |
| `SK.15.Docs` | Docs | 3 | 3 | 0 | `●` |
| `SK.15.Published` | Published | 5 | 5 | 0 | `●` |
| `SK.15.LoggingRetrofit` | Logging Retrofit (P-257) | 5 | 0 | 5 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-25] Sub state-map initialized — phase key registry, 6 phases scaffolded at `○`, no tasks yet; Package Board and Cross-Domain Dependencies populated from the newly-drafted `CLAUDE.md` domain brain (claude)
- [2026-06-26] WO-032 (P-200–P-204) dispatched — Design phase populated and closed (D-01–D-07, all `●`): drift-checked `SharedKernel.Primitives`, `SharedKernel.Contracts` (`IIntegrationEvent`, `EventEnvelope<TEvent>`), `SharedKernel.Messaging.Abstractions` (`IEventPublisher`), and `SharedKernel.Configuration` against current source — one correction found (D-04: `WebhookDeliveryOptions` validates via DataAnnotations + `IValidatableObject` through `AddValidatedOptions<TOptions>(IConfigurationSection)`, not a free-standing validator type as originally phrased); Scaffold (S-01–S-06), Core split into Signing (C-01–C-06) and Dispatch (C-07–C-17) sub-passes, Tests (T-01–T-03, tracking coverage delivered inside Core rather than deferred), Docs (DO-01–DO-03), and Published (P-01–P-05) phases populated; Package Board and Cross-Domain Dependencies refreshed with confirmed contract shapes (integration-arch-planner)
- [2026-06-26] S-01→S-06 → `●` in SK.15.Scaffold — `.csproj` wired to the four locked ProjectReferences + `Microsoft.Extensions.Http`/`.Http.Resilience`; 7 stub folders created; Tests project wired (`SharedKernel.Testing`, xUnit/FluentAssertions, `GlobalUsings.cs`); both projects already present in `.slnx`; `dotnet build` clean on both target projects (0 errors; pre-existing unrelated `02.Caching` NU1605s and `SharedKernel.Testing` SQLite NU1903 advisories confirmed present on `main` before this change) (state-map-phase)
- [2026-06-26] C-01→C-17 → `●` in SK.15.Core — Signing (`WebhookSignatureHeaders`, `WebhookSignatureProvider`, `WebhookSignatureVerifier` with `CryptographicOperations.FixedTimeEquals`) and Dispatch (`WebhookSubscription`/`IWebhookSubscriptionStore`, `WebhookDeliveryResult`, `WebhookDeliveryOptions`, `WebhookDeliveryExhaustedEvent`, `IWebhookDeliveryObserver`, `WebhookDispatcher` with bounded fan-out + standard resilience handler retry/backoff + exactly-once exhaustion publish via `IEventPublisher`, `AddSharedKernelWebhooks`/`WithDeliveryObserver<T>()`) fully implemented; `InternalsVisibleTo` added for test access to `WebhookHttpClientName`/`WebhookAttemptTracker`; attempt counting wired via `HttpRequestOptionsKey<T>` tracker incremented from `HttpStandardResilienceOptions.Retry.OnRetry`; 48/48 tests passing (21 signing + 27 dispatch/options/DI), 0 build warnings/errors on both target projects (integration-phase-implementer)
- [2026-06-26] T-01→T-03 → `●` in SK.15.Tests — audit-only pass, zero gaps found: re-read every test file in `SharedKernel.Integration.Webhooks.Tests` and re-ran the suite (48/48 passing) to confirm coverage already delivered under C-06/C-17 satisfies T-01 (signing round-trip/tamper/expired-timestamp/malformed-input), T-02 (fan-out isolation, retry/backoff attempt counting, exactly-once exhaustion via `InMemoryEventPublisher`, observer-exception isolation, options-validator rejections), and T-03 (`WebhookTestHarness`/`StubHttpMessageHandler` stub the named `HttpClient` via `IHttpClientFactory`, no real network/Testcontainers); no new test files written; propagated to root (integration-phase-implementer)
- [2026-06-26] DO-01→DO-03 → `●` in SK.15.Docs — `GenerateDocumentationFile` enabled in `SharedKernel.Integration.Webhooks.csproj` (zero warnings after fixing one unresolved `cref` in `WebhookSignatureVerifier.cs`; XML doc comments were already comprehensive from Core phase work); `README.md` written covering minimal setup, custom options, observer registration, dispatch, and inbound verification; `docs/configuration-reference.md` written covering every `WebhookDeliveryOptions` property/default/bound and the config section path; 48/48 tests still passing (integration-phase-implementer)
- [2026-06-26] P-01→P-05 → `●` in SK.15.Published — full NuGet packaging metadata added to `SharedKernel.Integration.Webhooks.csproj` mirroring the `12.Security`/`13.ServiceDefaults`/`14.Presentation` convention (`PackageId`, `Version 1.0.0`, `Authors`/`Company "Gresta-Vertex-Labs"`, MIT license, `README.md` packed via `PackagePath="\"`, symbol package); `dotnet pack` produces `.nupkg`+`.snupkg` with zero warnings, copied to `artifacts/nupkg/` (the established repo-root convention, confirmed against `06.Persistence/nupkg` and root `artifacts/nupkg/`; both extensions are already `.gitignore`d). New `15.Integration/consumer-verify` harness (registered in `Platform.SharedKernel.slnx`, mirrors the `13.ServiceDefaults`/`14.Presentation` consumer-verify pattern) proves two surfaces: (1) `AddSharedKernelWebhooks()` + a registered `IWebhookSubscriptionStore` + a stand-in `IEventPublisher` resolves `IWebhookDispatcher` and completes a real `DispatchAsync` call against zero subscriptions with zero DI exceptions; (2) omitting `IWebhookSubscriptionStore` causes `GetRequiredService<IWebhookDispatcher>()` itself to throw `InvalidOperationException` naming `IWebhookSubscriptionStore` in the message — failure surfaces immediately at first resolution, not deferred into a silently-resolved dispatcher that no-ops inside `DispatchAsync`. Discovered during harness construction: `AddSharedKernelWebhooks()`'s `BindConfiguration` call requires `IConfiguration` registered in the container even when no configuration section is bound — the harness registers an empty `ConfigurationBuilder().Build()` instance, which any consuming service's real `Program.cs` already provides via `WebApplication.CreateBuilder()`/`Host.CreateApplicationBuilder()`. 48/48 tests still passing. `SharedKernel.Integration.Webhooks` now `●` Published — **15.Integration domain (WO-032) complete end to end: Design → Scaffold → Core → Tests → Docs → Published** (integration-phase-implementer)
- [2026-07-09] LoggingRetrofit phase planned (WO-041, P-257) — this domain's production log surface is the smallest of the ten WO-041 domain phases: exactly one log statement, `WebhookDispatcher.LogObserverException`, a single shared private helper invoked from `NotifyAttemptAsync` and `NotifyCompletedAsync` (not two separately-authored call sites). No pre-existing `EventId` collision and no hand-written `LoggerMessage.Define` delegate to fix — unlike `02.Caching`'s confirmed 4001/4002 collision — this phase exists purely so the platform-wide `[LoggerMessage]` standard has zero exceptions. 5 tasks added (LR-01→LR-05) under new `SK.15.LoggingRetrofit` phase key: LR-01 confirms the pre-existing `SharedKernel.Primitives` `ProjectReference` (added under S-01/D-01 for future-proofing, previously unconsumed) now resolves `LoggingEventIdRanges.Integration`; LR-02 converts `LogObserverException` to a `[LoggerMessage]`-attributed method on a new nested `Log` class, `EventId = LoggingEventIdRanges.Integration + 0` (15000); LR-03 sweeps for zero remaining direct `ILogger`/hand-written `Define` usage; LR-04 documents the EventId allocation in `15.Integration/CLAUDE.md`; LR-05 regression-runs all 48 existing tests confirming zero behavioral change beyond the `EventId`/authoring mechanism. Since this is a single-package domain (no `.Abstractions` sibling), no 100-wide sub-block subdivision is needed — offsets `+1`..`+999` stay reserved for future growth. Execution is **blocked** until `01.Core` ships `LoggingEventIdRanges` (P-249, `0/4` done as of this planning pass); full acceptance additionally needs `00.Governance`'s SK0020/SK0021/`LoggingEventIdIntegrityAssertion` (P-250) to have shipped, though that is not a compile-time blocker for this phase's own code change (integration-arch-planner, WO-041)
