---
name: wo032_design_lock
description: WO-032 phase sequencing decisions for 15.Integration (P-200 through P-204) — why Core was split, and the one design correction made during the lock pass
type: project
---

WO-032 dispatched 5 phases for `15.Integration`: P-200 (Design lock), P-201 (Scaffold), P-202 (Core/Signing), P-203 (Core/Dispatch), P-204 (Docs+Published). All five were processed in one session on 2026-06-26.

**Core was split into two independently-verifiable sub-passes** (P-202 Signing, P-203 Dispatch) rather than one monolithic Core phase, mirroring the precedent set by WO-031 splitting 14.Presentation's Core into WebApi/SignalR sub-phases. Rationale: `WebhookSignatureProvider`/`WebhookSignatureVerifier` are pure, stateless, zero-dependency cryptographic primitives that can and should be proven correct (round-trip, tamper, expiry, malformed-input) in total isolation before the dispatcher, fan-out, retry policy, and DI complexity get layered on top. Building the dispatcher against an already-proven signing layer means dispatcher tests assume signing correctness instead of re-deriving it.

**Design correction found during the P-200 lock pass:** the original domain brain (drafted 2026-06-25, before any phase was opened) said `WebhookDeliveryOptions` would be "Validated eagerly at startup via a SharedKernel.Configuration (01.Core) Options-pattern validator" — phrasing that implied a free-standing validator type or `IValidateOptions<T>` contract. Re-reading `01.Core\SharedKernel.Configuration\Extensions\OptionsExtensions.cs` directly showed the actual (and only) API is:

```csharp
public static IServiceCollection AddValidatedOptions<TOptions>(
    this IServiceCollection services, IConfigurationSection section) where TOptions : class
```

which does `.Bind(section).ValidateDataAnnotations().ValidateOnStart()` — i.e. DataAnnotations-based, no separate validator class. The corrected design: `WebhookDeliveryOptions` carries `[Range]` attributes for mechanical bounds (`MaxAttempts`, `MaxConcurrentDeliveries`) and implements `IValidatableObject` for the one cross-field rule DataAnnotations attributes can't express alone (`MaxBackoffDelay ≥ BaseBackoffDelay`) plus the positive-`TimeSpan` checks. See [[reference_upstream_contracts]] for the full confirmed shape of all four upstream surfaces checked during this pass.

This is the kind of drift the Design phase exists to catch — a brain drafted ahead of any opened phase is useful upfront thinking but is never authoritative until a Design phase re-verifies it against actual current source, not memory of what was documented elsewhere.

**How to apply:** When a future phase touches `WebhookDeliveryOptions` or any other options POCO in this domain, use `AddValidatedOptions<TOptions>(IConfigurationSection)` + DataAnnotations + `IValidatableObject` — never introduce or assume an `IValidateOptions<T>` custom validator class unless `01.Core/SharedKernel.Configuration` ships one in the future (re-check before assuming).
