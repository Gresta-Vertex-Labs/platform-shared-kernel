# 01.Core — Domain Brain

## What This Domain Is

The foundational building blocks domain. Every other domain in the shared kernel depends on this layer, so it must reference nothing from outside `01.Core`. It ships twelve **published** packages today, covering: functional primitives (`Result<T>`, `Error`), exception-boundary and multi-result-aggregation railway extensions (`ResultTry`, `ResultCombine`), system abstractions (`IClock` — internally `TimeProvider`-backed since P-295 — SmartEnums, base exceptions, BCL extensions), a time-ordered identifier generator (`IIdGenerator`, UUIDv7-backed), a two-path guard system (`Guard.Against` / `Guard.Throw`), Options-pattern validation, a Feature Flag abstraction (including weighted-variant/gradual-rollout evaluation), dependency-free cryptographic primitives (secret-agnostic one-way hashing, AES-GCM symmetric encryption — both sync and async, with an additive envelope-encryption seam via `IEnvelopeEncryptionProvider` and a bounded-TTL caching decorator via `CachedEncryptionKeyProvider` (P-446/WO-068, shipped) — RSA/ECDSA + HMAC signing, secure random generation, non-secret content fingerprinting via `IContentHasher`, and RFC 6238/4226 TOTP/HOTP second-factor primitives (`Base32`, `IHotpGenerator`, `ITotpGenerator`, `TotpProvisioningUri`, `ITotpReplayGuard`/`TotpVerifier`, `RecoveryCodeGenerator` — P-451/WO-069, shipped), a dependency-free payload compression primitive (`SharedKernel.Compression`, Brotli-default/GZip-keyed), culture-independent financial/identity format validation (`SharedKernel.Validation` — IBAN/BIC/PAN/ISO 4217/ISO 3166/E.164/VAT + a pluggable national-ID registry, P-443/WO-067, shipped) plus its `FluentValidation` rule adapter (`SharedKernel.Validation.FluentValidation` — a third-party NuGet dependency, P-444/WO-067, shipped), a vendor-backed KMS key provider (`SharedKernel.Cryptography.KeyVault.Azure` — Azure Key Vault Keys, a package with a third-party NuGet dependency, P-447/WO-068, shipped), the platform-wide `LoggingEventIdRanges` registry — a compile-time constant reserving each folder-map domain's `EventId` numbering block for the `[LoggerMessage]` logging convention enforced repo-wide, now spanning 00 through 20 (`Idempotency`/`Scheduling`/`Reporting` bases added via `SK.01.LoggingRangesNewDomains`, shipped) — and the `WellKnownHeaders` / `WellKnownBaggageKeys` / `WellKnownTagKeys` registries, the platform-wide source of cross-service propagation identifier literals (HTTP/gRPC header names, `Activity` baggage keys, and `Activity` tag/attribute keys) that every domain touching correlation-id, tenant-id, or error-classification propagation must reference instead of redeclaring locally, PII/data-classification taxonomy and masking (`SharedKernel.DataPrivacy` — `DataClassification`/`SensitiveDataCategory` marker attributes, `PiiMasking.*` deterministic helpers, `IDataSubjectRequestHandler`, P-474/WO-076, shipped), and a culture-keyed error-message catalog seam (`SharedKernel.Localization` — `ILocalizationCatalog`/`InMemoryLocalizationCatalog`/`StringLocalizerLocalizationCatalog`, a package with a first-party Microsoft NuGet dependency, P-482/WO-078, shipped).

Philosophy: **Zero external dependencies for Primitives. Pure C#. AOT-first. Railway-oriented.**

> **Why cryptography lives here, not in `12.Security`:** `12.Security` owns identity/authentication concerns (`IUserContext`, `ITenantProvider`, JWT/OIDC). Generic crypto primitives — hashing, encryption, signing, secure random — are a separate concern needed by services that have no identity stack at all (background workers, batch jobs, internal tools). Bundling them into `12.Security` would force every consumer to pull in OIDC/JWT machinery just to hash a secret or encrypt a payload. `SharedKernel.Cryptography` lives in `01.Core` because, like `SharedKernel.Primitives`, it references nothing else in the platform — `12.Security.Oidc` may depend on it for token-signing primitives, never the reverse. This is also why `IOneWayHasher` is named and shaped the way it is (WO-034): a `01.Core` primitive must stay free of any single consuming-domain's vocabulary, including the auth domain's own "password" terminology.

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Primitives` | `Result<T>`, `Error`, `ErrorType`, `IClock`, `IIdGenerator`, `SmartEnum<TEnum,TValue>`, `LoggingEventIdRanges`, `WellKnownHeaders`, `WellKnownBaggageKeys`, `WellKnownTagKeys` | nothing |
| `SharedKernel.Core` | Base exceptions, BCL extension methods, `Result<T>` railway extensions (`Map`/`MapError`/`Bind`/`Match`/`Tap`/`ResultTry`/`ResultCombine`) | `SharedKernel.Primitives` |
| `SharedKernel.Guards` | Two-path guard system: `Guard.Against.*` (functional) + `Guard.Throw.*` (imperative) | `SharedKernel.Primitives`, `SharedKernel.Core` |
| `SharedKernel.Configuration` | Options-pattern validation, `AddValidatedOptions` DI extension | `SharedKernel.Primitives` |
| `SharedKernel.FeatureManagement` | `IFeatureManager` abstraction (boolean + weighted-variant evaluation) + `Microsoft.FeatureManagement` adapter | `SharedKernel.Primitives` |
| `SharedKernel.Cryptography` | Secret-agnostic one-way hashing, AES-256-GCM symmetric encryption (sync + async `*Async` overloads), async KMS-capable `IEncryptionKeyProvider`, additive `IEnvelopeEncryptionProvider`/`CachedEncryptionKeyProvider` (P-446/WO-068, shipped, breaking), RSA/ECDSA + HMAC signing, secure random/token generation, non-secret content fingerprinting (`IContentHasher`), RFC 6238/4226 TOTP/HOTP + `Base32`/`TotpProvisioningUri`/`ITotpReplayGuard`/`TotpVerifier`/`RecoveryCodeGenerator` (P-451/WO-069, shipped, additive) | `SharedKernel.Primitives`, `SharedKernel.Configuration` |
| `SharedKernel.Compression` | Generic payload compression (`IPayloadCompressor`): Brotli default, GZip keyed alternate | `SharedKernel.Primitives`, `SharedKernel.Configuration` |
| `SharedKernel.Validation` *(shipped, P-443/WO-067)* | Culture-independent format validators: IBAN, BIC, PAN (Luhn + network detection), ISO 4217, ISO 3166, E.164, VAT baseline, pluggable per-country `INationalIdValidator` registry (TCKN default); dual-mode standalone `Result`/bool + `Guard.Against.*` extensions | `SharedKernel.Primitives`, `SharedKernel.Guards` |
| `SharedKernel.Validation.FluentValidation` *(shipped, P-444/WO-067)* | `IRuleBuilder<T,string>` rule adapter for every `SharedKernel.Validation` validator — the domain's only package with a third-party NuGet dependency | `SharedKernel.Validation`, `FluentValidation` (NuGet) |
| `SharedKernel.Cryptography.KeyVault.Azure` *(shipped, P-447/WO-068)* | Azure Key Vault Keys implementation of `IEncryptionKeyProvider` + `IEnvelopeEncryptionProvider` | `SharedKernel.Cryptography`, `SharedKernel.Configuration`, `Azure.Security.KeyVault.Keys`, `Azure.Identity` (NuGet) |
| `SharedKernel.DataPrivacy` *(shipped, P-474/WO-076)* | `DataClassification`/`SensitiveDataCategory` marker attributes, `PiiMasking.*` pure helpers, `IDataSubjectRequestHandler` | `SharedKernel.Primitives` |
| `SharedKernel.Localization` *(shipped, P-482/WO-078)* | `ILocalizationCatalog` keyed by `(code, CultureInfo)`; `InMemoryLocalizationCatalog` default (with parent-culture-chain fallback down to `CultureInfo.InvariantCulture`) + `StringLocalizerLocalizationCatalog` resx-composition path | `SharedKernel.Primitives`, `Microsoft.Extensions.Localization.Abstractions` (NuGet) |

All twelve packages listed above are published. `SharedKernel.Validation` (P-443/WO-067), `SharedKernel.Validation.FluentValidation` (P-444/WO-067), `SharedKernel.Cryptography.KeyVault.Azure` (P-447/WO-068), `SharedKernel.DataPrivacy` (P-474/WO-076), and `SharedKernel.Localization` (P-482/WO-078) shipped as the eighth, ninth, tenth, eleventh, and twelfth published packages — see `SK.01.P443`/`SK.01.P444`/`SK.01.P447`/`SK.01.P474`/`SK.01.P482`. All twelve target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Functional primitives | Pure C# 13 — no NuGet dependencies |
| System abstractions | Pure C# 13 — no NuGet dependencies |
| Guard clauses | Pure C# 13 — no NuGet dependencies; compiled/cached `System.Text.RegularExpressions.Regex` for format/email guards |
| Options validation | `Microsoft.Extensions.Options.DataAnnotations` |
| Feature flags | `Microsoft.FeatureManagement` (abstracted behind `IFeatureManager`) |
| Cryptographic primitives | Pure BCL `System.Security.Cryptography` only — `Rfc2898DeriveBytes` (PBKDF2), `AesGcm`, `RSA`, `ECDsa`, `HMACSHA256`, `SHA256`, `RandomNumberGenerator`, `CryptographicOperations.FixedTimeEquals`. Zero third-party NuGet dependencies. |
| Payload compression | Pure BCL `System.IO.Compression` only — `BrotliStream` (default), `GZipStream` (keyed alternate). Zero third-party NuGet dependencies. |
| Identifier generation | Pure BCL — `Guid.CreateVersion7()` (RFC 9562 UUID v7). Zero third-party NuGet dependencies. |
| Time abstraction | Pure BCL — `System.TimeProvider` (shipped since .NET 8) backs `SystemClock` internally; `IClock` remains the only source of time exposed to domain/application code. |
| Format validation *(P-443, shipped)* | Pure C# 13 — no NuGet dependencies; IBAN/BIC/PAN/ISO 4217/ISO 3166/E.164/VAT checks are hand-rolled, not delegated to a third-party validation library |
| FluentValidation adapter *(P-444, shipped)* | `FluentValidation` 11.x (NuGet) — confined to `SharedKernel.Validation.FluentValidation`; never a dependency of `SharedKernel.Validation` itself |
| KMS key management *(P-447, shipped)* | `Azure.Security.KeyVault.Keys` + `Azure.Identity` (NuGet) — confined to `SharedKernel.Cryptography.KeyVault.Azure`; never a transitive dependency of `SharedKernel.Cryptography` itself |
| TOTP/HOTP second factor *(P-451, shipped)* | Pure BCL `System.Security.Cryptography` (`HMACSHA1`/`HMACSHA256`/`HMACSHA512`) — RFC 6238/4226. Zero third-party NuGet dependencies. |
| Data privacy taxonomy *(P-474, design-locked)* | Pure C# 13 — no NuGet dependencies; attributes are pure metadata, never reflected over at runtime |
| Localization *(P-482, shipped)* | `Microsoft.Extensions.Localization.Abstractions` (NuGet, first-party Microsoft) — wraps `IStringLocalizer`/`IStringLocalizerFactory`, never a bespoke resx pipeline |

---

## Interface Contracts

### `SharedKernel.Primitives` — public surface

```
IHasSuccessFlag  (public interface — zero members)
    — implemented by both Result<T> and Result (non-generic)
    — purpose: pipeline behaviors use `response is IHasSuccessFlag f && !f.IsSuccess` to check outcome
      without reflection or dynamic; fully AOT-clean, no [RequiresUnreferencedCode] annotation
    — carries no properties: callers check IsSuccess/IsFailure on the concrete type after the cast

IResultOfT<T>  (public interface — implemented by Result<T> only)
    .IsSuccess                                             → bool
    .IsFailure                                             → bool
    .Value                                                 → T   (throws InvalidOperationException if failure)
    — purpose: enables `where TResponse : IResultOfT<TResponse>` generic constraint in pipeline behaviors
      so FailureResponseFactory-style construction is a direct interface call — zero reflection,
      zero Expression tree compilation, AOT-clean
    — Result (non-generic) does NOT implement this interface (it carries no typed value payload)

IFailureFactory<TSelf>  (public interface — self-referential/CRTP shape, implemented by Result<T> only)
    where TSelf : IFailureFactory<TSelf>
    static abstract TSelf Failure(Error error)             → TSelf
    — purpose: enables `where TResponse : IFailureFactory<TResponse>` generic constraint so a caller who
      only knows the open generic TResponse (never learning the inner T) can call `TResponse.Failure(error)`
      directly via a C# static abstract interface member — zero reflection, zero Type.MakeGenericType /
      Type.GetMethod / MethodBase.Invoke, zero [RequiresUnreferencedCode]
    — Result<T> implements IFailureFactory<Result<T>> via its existing `Failure(Error error)` static
      factory — no new member, no signature change; the pre-existing method now also satisfies this
      interface
    — Result (non-generic) does NOT implement this interface, consistent with IResultOfT<T>'s exclusion —
      callers needing a non-generic Result failure use the existing `TResponse == typeof(Result)` fast
      path in the consuming dispatcher (05.Application)
    — additive to IHasSuccessFlag / IResultOfT<T> (P-230), not a replacement: those answer "read the
      outcome/value of a known-shape response"; this answers "construct a failure of an unknown
      Result<T> shape from just TResponse" — something IResultOfT<T> cannot do because it is
      parameterized on the inner value type, not on itself

Result<T>  (sealed class — not struct; zero-value problem with generic struct payloads)
    implements IHasSuccessFlag, IResultOfT<T>, IFailureFactory<Result<T>>
    .Success(T value)                                      → Result<T>
    .Failure(Error error)                                  → Result<T>
    .IsSuccess                                             → bool
    .IsFailure                                             → bool
    .Value                                                 → T   (throws InvalidOperationException if failure)
    .Error                                                 → Error (throws InvalidOperationException if success)
    implicit operator Result<T>(T value)                   → Result<T>.Success
    implicit operator Result<T>(Error error)               → Result<T>.Failure

Result  (non-generic, readonly struct — void operations; no typed value payload)
    implements IHasSuccessFlag
    .Success()                                             → Result
    .Failure(Error error)                                  → Result
    implicit operator Result(Error error)                  → Result.Failure

Error  (sealed record)
    .None                                                  → Error (sentinel — no error; never use null)
    .Unexpected(string code, string message)               → Error
    .Validation(string code, string message)               → Error
    .NotFound(string code, string message)                 → Error
    .Conflict(string code, string message)                 → Error
    .Unauthorized(string code, string message)             → Error
    .BusinessRule(string code, string message)             → Error  (domain invariant violation — HTTP 422; distinct from Validation)
    .Forbidden(string code, string message)                → Error  (permitted-to-attempt-in-general, this specific instance/condition not satisfied — HTTP 403;
                                                                       distinct from Unauthorized. Shipped P-384/WO-059.)
    .Code                                                  → string
    .Message                                               → string
    .Type                                                  → ErrorType

ErrorType  (enum)
    None | Unexpected | Validation | NotFound | Conflict | Unauthorized | BusinessRule | Forbidden
    — BusinessRule: domain invariant violation; maps to HTTP 422 Unprocessable Entity at presentation layer;
      semantically distinct from Validation (input format/presence) and Unexpected (system fault)
    — Forbidden: caller IS generally permitted to attempt this kind of operation, but this specific
      instance/condition is not satisfied (e.g. a maker-checker dual-approval gate rejecting the same user
      who submitted the request, or a role/permission attribute rejecting an authenticated-but-under-privileged
      caller); maps to HTTP 403 Forbidden at the presentation layer. Semantically distinct from Unauthorized,
      which means the caller is not permitted to attempt this at all (HTTP 401 — no/invalid credentials).
      Substituting Unauthorized for a genuinely Forbidden condition is a platform anti-pattern this member
      exists specifically to remove. Mirrors BusinessRule's addition pattern (P-042) — additive, no existing
      member's numeric value changes. A downstream consumer with an exhaustive switch/switch expression over
      ErrorType carrying no discard/default arm will need a source-level update to keep compiling once this
      ships — a compile-time signal, not a runtime break. Shipped P-384/WO-059 (SharedKernel.Primitives 1.1.0);
      no companion ErrorCodes constant was added (unlike BusinessRule's ErrorCodes.Domain.RuleViolated) —
      no consuming phase asked for a shared code constant

ErrorCodes  (static class — well-known string constants, organized as nested static classes)
    ErrorCodes.Validation.Required
    ErrorCodes.Validation.OutOfRange
    ErrorCodes.NotFound.Default
    ErrorCodes.Conflict.Default
    ErrorCodes.Unauthorized.Default
    ErrorCodes.Domain.RuleViolated                         → "domain.rule.violated"  (canonical code for BusinessRuleViolationException)
    ErrorCodes.Unexpected.Default                          → "unexpected.exception"  (default code used by ResultTry when the caller supplies no custom Error mapping)
    — consuming packages may define additional local constants; no enum versioning problem

ValidationResult  (sealed record — multi-error aggregate, distinct from Result<T>)
    .IsValid                                               → bool
    .Errors                                                → IReadOnlyList<Error>
    .Success()                                             → ValidationResult
    .Failure(IReadOnlyList<Error> errors)                  → ValidationResult

ValidationResult<T>  (sealed record — generic multi-error aggregate)
    .IsValid                                               → bool
    .Errors                                                → IReadOnlyList<Error>
    .Value                                                 → T
    .Success(T value)                                      → ValidationResult<T>
    .Failure(IReadOnlyList<Error> errors)                  → ValidationResult<T>

IClock
    UtcNow                                                 → DateTimeOffset
    Today                                                  → DateOnly

SystemClock  (sealed class, implements IClock)
    SystemClock()                                          — defaults to TimeProvider.System
    SystemClock(TimeProvider timeProvider)                 — caller-supplied TimeProvider (e.g. a FakeTimeProvider in tests)
    — internally sources UtcNow from an injected System.TimeProvider (P-295, WO-049) instead of calling
      DateTimeOffset.UtcNow directly; Today derives from the same TimeProvider-sourced instant
      (DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime)); no mutable state beyond the held
      TimeProvider reference
    — IClock's own public surface (UtcNow, Today) is unchanged by this — TimeProvider is an internal
      implementation detail of SystemClock, never an alternative time source exposed to 03.Domain/05.Application
      call sites; SK0001 (DirectDateTimeUsageAnalyzer) behavior is unchanged

IIdGenerator
    NewId()                                                → Guid
    — purpose: a time-ordered, database-index-friendly alternative to Guid.NewGuid() (UUID v4) for any
      call site generating a new primary-key-shaped identifier (notably 03.Domain's IAggregateFactory
      implementations); opt-in — no existing identifier call site is forced to adopt it

UuidV7IdGenerator  (sealed class, implements IIdGenerator) — implemented P-293/WO-049
    — backed by Guid.CreateVersion7() (RFC 9562 UUID version 7); no mutable state; stateless/thread-safe
    — values whose embedded 48-bit millisecond timestamps differ compare as non-decreasing under the default
      Guid comparer (CompareTo/`<`), restoring B-tree insert locality on a Postgres/SQL Server clustered/primary-key
      index that a fully-random Guid.NewGuid() (UUID v4) does not provide, while retaining no-central-coordinator
      distributed generation. Two values sharing the *same* millisecond timestamp carry no ordering guarantee
      relative to each other — Guid.CreateVersion7() fills everything below the timestamp with cryptographically
      random bits (RFC 9562's "random" sub-method, not the optional "monotonic random" counter-based sub-method) —
      confirmed empirically: a tight loop generating 1000 values in immediate succession and asserting strict
      non-decreasing ordering failed in testing. This is still exactly what restores index locality in practice:
      real production inserts are spread across many milliseconds, and same-millisecond ties still land
      immediately adjacent to each other in the index regardless of the random tie-break
    — ships with no DI extension method (mirrors SystemClock/IClock's own no-extension precedent for a
      single-implementation seam): a consuming service registers it via a plain
      `services.AddSingleton<IIdGenerator, UuidV7IdGenerator>()` call at its own composition root — see
      DI Registration below. Note: SharedKernel.Primitives does already carry one package-owned DI extension
      today (ClockExtensions.AddClock(), which is why the package references
      Microsoft.Extensions.DependencyInjection.Abstractions at all) — IIdGenerator's no-extension design is a
      deliberate per-abstraction choice for this phase, not evidence that the package is dependency-free of
      Microsoft.Extensions.DependencyInjection.Abstractions
    — cross-reference only, not a contract change: 03.Domain/CLAUDE.md's IAggregateFactory guidance may note
      this option exists for services wanting index-friendly identifiers; that edit is 03.Domain's own
      jurisdiction (domain-arch-planner), not tracked here — mirrors the "consuming domain retrofit is each
      consuming domain's own responsibility" precedent already established for WellKnownHeaders/WellKnownBaggageKeys

SmartEnum<TEnum, TValue>  (abstract base, TEnum : SmartEnum<TEnum,TValue>)
    .FromValue(TValue value)                               → TEnum   (throws if not found)
    .TryFromValue(TValue value, out TEnum? result)         → bool
    .FromName(string name)                                 → TEnum   (throws if not found)
    .List                                                  → IReadOnlyList<TEnum>  (static compile-time list — no reflection)
    .Name                                                  → string
    .Value                                                 → TValue

LoggingEventIdRanges  (static class — compile-time constant registry, zero reflection)
    .DomainRangeWidth                                      → int  (= 1000; width of one domain's reserved EventId block)
    .PackageSubBlockWidth                                  → int  (= 100; recommended width of one package's sub-block within a
                                                                     multi-package domain's 1000-wide block, allocated in that
                                                                     domain's package declaration order)
    .Governance                                            → int  (= 0)      — 00.Governance
    .Core                                                  → int  (= 1000)   — 01.Core
    .Caching                                               → int  (= 2000)   — 02.Caching
    .Domain                                                → int  (= 3000)   — 03.Domain
    .Contracts                                             → int  (= 4000)   — 04.Contracts
    .Application                                           → int  (= 5000)   — 05.Application
    .Persistence                                           → int  (= 6000)   — 06.Persistence
    .Messaging                                             → int  (= 7000)   — 07.Messaging
    .Storage                                               → int  (= 8000)   — 08.Storage
    .Search                                                → int  (= 9000)   — 09.Search
    .Intelligence                                          → int  (= 10000)  — 10.Intelligence
    .Communication                                         → int  (= 11000)  — 11.Communication
    .Security                                              → int  (= 12000)  — 12.Security
    .ServiceDefaults                                       → int  (= 13000)  — 13.ServiceDefaults
    .Presentation                                          → int  (= 14000)  — 14.Presentation
    .Integration                                           → int  (= 15000)  — 15.Integration
    .Testing                                               → int  (= 16000)  — 16.Testing
    .Workflows                                             → int  (= 17000)  — 17.Workflows
    .Idempotency                                           → int  (= 18000)  — 18.Idempotency
    .Scheduling                                            → int  (= 19000)  — 19.Scheduling
    .Reporting                                             → int  (= 20000)  — 20.Reporting
    — each domain constant = {two-digit folder-map number} * 1000; reserves a contiguous 1000-wide EventId block
      ({value}..{value}+999) exactly matching the root CLAUDE.md folder map (00 through 20)
    — a domain with multiple packages must subdivide its own 1000-wide block into 100-wide sub-blocks, one per
      package, in the order that domain's packages are declared (e.g., 02.Caching: Redis.Core=2000-2099,
      Redis (L2)=2100-2199, Redis.DistributedLocking=2200-2299, Redis.HashStore=2300-2399, Redis.PubSub=2400-2499);
      this registry enforces only the domain-level 1000-wide boundary — intra-domain sub-block assignment is each
      domain's own responsibility (and where the historical Redis.Core/Redis.PubSub 4001/4002 collision came from)

WellKnownHeaders  (static class — compile-time constant registry, zero reflection)
    .CorrelationId                                         → string  (= "X-Correlation-Id")
    .TenantId                                              → string  (= "X-Tenant-Id")
    — the single authoritative source for HTTP/gRPC-metadata header names carrying cross-service
      propagation identifiers; replaces independently-redeclared literals in
      `11.Communication.Rest.TenantIdDelegatingHandler`, `11.Communication.Grpc.TenantIdInterceptor`,
      and `13.ServiceDefaults.MultiTenancy.HeaderTenantResolutionStrategy` (P-259, WO-042)

WellKnownBaggageKeys  (static class — compile-time constant registry, zero reflection)
    .CorrelationId                                         → string  (= "correlation.id")
    — the single authoritative source for `System.Diagnostics.Activity` baggage / distributed-trace
      propagation key names; reconciles `14.Presentation.CorrelationIdMiddleware` (writer) with
      `13.ServiceDefaults.BaggageLogRecordProcessor` (reader) — a confirmed live mismatch existed
      between these two before this registry (P-259, WO-042)

WellKnownTagKeys  (static class — compile-time constant registry, zero reflection)
    .TenantId                                              → string  (= "tenant.id")
    .CorrelationId                                          → string  (= "correlation.id")
    .ErrorType                                              → string  (= "error.type")
    .ErrorCode                                              → string  (= "error.code")
    — the single authoritative source for `System.Diagnostics.Activity.SetTag(...)` span-attribute key
      names; distinct call-site shape from `WellKnownBaggageKeys` (`Activity.SetBaggage`/`AddBaggage`) even
      where the literal value is identical (e.g. `CorrelationId` = "correlation.id" in both) — 00.Governance's
      SK0022 analyzer regulates `SetTag` and `SetBaggage` as separate recognized call-site shapes. Dotted
      lowercase values chosen to match OpenTelemetry semantic-convention style and stay consistent with the
      pre-existing baggage-key literal. Introduced pre-emptively (P-294, WO-049): SK0022 already recognizes
      `Activity.SetTag(...)` as a regulated shape with no registry to point at yet — every domain that starts
      emitting span tags (05.Application, 07.Messaging, 11.Communication, 13.ServiceDefaults, 14.Presentation,
      17.Workflows) is a candidate for the same class of drift WellKnownBaggageKeys was created to fix after
      the fact. No existing domain's shipped `Activity.SetTag` call sites are changed by this phase — retrofit
      is each consuming domain's own follow-up, exactly as documented for the other two registries
```

### `SharedKernel.Core` — public surface

```
Base exceptions  (all derive from SharedKernelException; string-only constructors are forbidden)
    SharedKernelException(string message, Error error)
    DomainException(Error error)
    ValidationException(IReadOnlyList<Error> errors)       — bridges ValidationResult to exception world
    NotFoundException(Error error)
    ConflictException(Error error)
    UnauthorizedException(Error error)

Result<T> railway extension methods  (static, AOT-safe)
    .Map<TOut>(Func<T, TOut> map)                          → Result<TOut>       (success: transform; failure: pass-through)
    .MapError(Func<Error, Error> map)                      → Result<T>          (failure: transform; success: pass-through)
    .Bind<TOut>(Func<T, Result<TOut>> bind)               → Result<TOut>       (short-circuits on failure)
    .Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) → TOut  (fold to single value)
    .Tap(Action<T> action)                                 → Result<T>          (side-effect on success, returns original)

Result (non-generic) railway extension
    .Match(Action onSuccess, Action<Error> onFailure)      → void               (void fold for void operations)

Async railway overloads (this Task<Result<T>> extensions)
    .Map / .MapError / .Bind / .Match / .Tap              → Task<Result<...>> / Task<TOut>
    — sync-lambda and async-lambda (Func<T, Task<TOut>>) overloads both provided
    — outer extension body avoids async/await where only work is awaiting the input (no needless state machine)

ResultTry  (static class — exception-boundary entry point, P-292/WO-049)
    .Try<T>(Func<T> operation)                             → Result<T>
    .Try<T>(Func<T> operation, Func<Exception, Error> onException)          → Result<T>   (custom mapping overload)
    .TryAsync<T>(Func<Task<T>> operation)                  → Task<Result<T>>
    .TryAsync<T>(Func<Task<T>> operation, Func<Exception, Error> onException) → Task<Result<T>>
    — invokes the delegate; on normal completion returns Result<T>.Success(value); on any thrown exception
      (never rethrown) returns Result<T>.Failure(Error.Unexpected(ErrorCodes.Unexpected.Default, "{ExceptionType}: {ExceptionMessage}"))
      unless a custom onException mapper is supplied
    — an AggregateException is flattened (.Flatten().InnerExceptions) before message construction, so a
      caught Task.Wait()/.Result-style aggregate surfaces every inner exception's type+message, not just the
      generic outer AggregateException message
    — the sanctioned seam for the one legitimate place Result-oriented code still touches a throwing
      third-party SDK call or a BCL method with no Result-returning equivalent; every call site otherwise
      hand-rolling try/catch-to-Result should route through this instead
    — TryAsync is a genuine async/await method (the one exception to the "avoid async/await when only
      awaiting the input" railway rule below): catching an exception thrown during an awaited operation
      requires the try/catch to wrap the await itself, which is impossible without an async state machine

ResultCombine  (static class — multi-result aggregation, P-292/WO-049)
    .Combine(params Result[] results)                      → ValidationResult
    .Combine(IEnumerable<Result> results)                  → ValidationResult
    .Combine<T>(params Result<T>[] results)                → ValidationResult<IReadOnlyList<T>>
    .Combine<T>(IEnumerable<Result<T>> results)             → ValidationResult<IReadOnlyList<T>>
    — evaluates every input Result/Result<T> (no short-circuit on first failure); if all succeed, returns
      ValidationResult.Success() / ValidationResult<IReadOnlyList<T>>.Success(collectedValuesInInputOrder);
      if one or more fail, returns ValidationResult.Failure(allFailingErrors) /
      ValidationResult<IReadOnlyList<T>>.Failure(allFailingErrors) carrying every failing Error, not just the first
    — complements ValidationResult's existing multi-error shape: gives callers a way to *produce* that shape
      from several independent Result-returning checks instead of manually appending to a List<Error>

BCL extension methods  (all static, no reflection)
    string         : .ToSnakeCase(), .ToCamelCase(), .ToPascalCase(), .IsNullOrWhiteSpace()
    IEnumerable<T> : .ToBatches(int size), .IsNullOrEmpty(), .WhereNotNull()
    DateTimeOffset : .ToUnixMilliseconds(), .StartOfDay(), .EndOfDay()
    Guid           : .IsEmpty()
```

### `SharedKernel.Configuration` — public surface

```
AddValidatedOptions<TOptions>(IConfiguration section)
    → registers IOptions<TOptions>, IOptionsSnapshot<TOptions>,
      IOptionsMonitor<TOptions>, and calls .ValidateDataAnnotations().ValidateOnStart()

[ValidateOptions] attribute
    → marker attribute; triggers DataAnnotations + custom IValidateOptions<T> evaluation at startup
```

### `SharedKernel.Guards` — public surface

```
IGuardClause  (public marker interface — no members)
    — returned by Guard.Against; all guard logic is chained off this interface via extension methods
    — DefaultGuardClause is the private sealed implementation; callers never reference it directly

Guard  (static class)
    .Against                                                  → IGuardClause  (entry point for functional path)

Guard.Throw  (nested static class — imperative path)
    Mirrors every Against.* extension as a void method.
    On non-null Error return: throws DomainException(error).
    On null return (guard passed): returns without throwing.

Guard clause extensions on IGuardClause — all return Error? (null = passed, non-null = violation):

    Null/empty
        .Null<T>(T? value, string paramName)                 → Error?   (reference types only)
        .NullOrEmpty(string? value, string paramName)        → Error?
        .NullOrWhiteSpace(string? value, string paramName)   → Error?

    String length
        .ShorterThan(string value, int minLength, string paramName)   → Error?
        .LongerThan(string value, int maxLength, string paramName)    → Error?

    Numeric  (overloaded for int, decimal, long)
        .NegativeOrZero(T value, string paramName)           → Error?
        .Negative(T value, string paramName)                 → Error?
        .NotPositive(T value, string paramName)              → Error?

    Range
        .OutOfRange<T>(T value, T min, T max, string paramName)  → Error?   (where T : IComparable<T>)

    Default / Guid
        .Default<T>(T value, string paramName)               → Error?   (EqualityComparer<T>.Default — no reflection)
        .InvalidGuid(Guid value, string paramName)           → Error?   (fails on Guid.Empty)

    Format / Email
        .InvalidFormat(string value, string pattern, string paramName)  → Error?
            — uses static compiled Regex field keyed by pattern (ConcurrentDictionary); bounded timeout; zero new Regex per call
        .Email(string? value, string paramName)              → Error?
            — uses same cached-regex strategy; no third-party NuGet

    Collections  (IEnumerable<T> enumerated once per call)
        .Empty<T>(IEnumerable<T> source, string paramName)       → Error?
        .MaxCount<T>(IEnumerable<T> source, int max, string paramName)  → Error?
        .MinCount<T>(IEnumerable<T> source, int min, string paramName)  → Error?

    Boolean predicate  (caller supplies Error — enables arbitrary business-rule guards)
        .True(bool condition, Error error)                   → Error?   (returns error if condition is false)
        .False(bool condition, Error error)                  → Error?   (returns error if condition is true)

    SmartEnum
        .InvalidSmartEnum<TEnum, TValue>(TValue id)          → Error?   (where TEnum : SmartEnum<TEnum,TValue>)
            — calls SmartEnum<TEnum,TValue>.TryFromValue; zero reflection

GuardDescriptions  (internal static class — not public API)
    — all error message templates as const string; {0}/{1} placeholders; string.Format at call site
```

### `SharedKernel.FeatureManagement` — public surface

```
IFeatureManager
    IsEnabledAsync(string feature, CancellationToken ct)                              → bool
    IsEnabledAsync<TContext>(string feature, TContext ctx, CancellationToken ct)      → bool
    GetVariantAsync(string feature, CancellationToken ct)                             → FeatureVariant
    GetVariantAsync<TContext>(string feature, TContext ctx, CancellationToken ct)     → FeatureVariant
    — variant methods implemented P-298/WO-049; bridge Microsoft.FeatureManagement's IVariantFeatureManager
      the same way IsEnabledAsync already bridges its plain boolean evaluation, without leaking any
      Microsoft.FeatureManagement type into this interface's public surface
    — the existing boolean IsEnabledAsync members and their behavior are completely unchanged (additive-only)
    — GetVariantAsync<TContext> has NO generic per-TContext contextual-filter equivalent to
      IsEnabledAsync<TContext> — confirmed via reflection against the real Microsoft.FeatureManagement 4.5.0
      assembly: IVariantFeatureManager's only context-aware variant overload is fixed to a concrete
      Microsoft.FeatureManagement.FeatureFilters.ITargetingContext (UserId + Groups), with no generic
      counterpart. MicrosoftFeatureManagerAdapter bridges this by deriving the targeting UserId from
      context?.ToString() — repeated calls with an equal context value are deterministic by construction,
      but a TContext without a meaningful ToString() override collapses every instance of that type to the
      same targeting bucket. A string context (a tenant id, a user id) is the most direct, predictable choice.

FeatureVariant  (sealed record — P-298/WO-049)
    .Name                                                  → string           (caller-defined variant identifier, e.g. "ControlGroup" / "VariantB")
    .Configuration                                         → string?          (raw configuration payload for this variant, if any; caller deserializes to its own strongly-typed shape)
    .Unassigned                                            → FeatureVariant   (static sentinel — Name = "Unassigned", Configuration = null)
    — a feature with no configured variants, an unresolvable allocation, or an unknown feature name falls
      back to the FeatureVariant.Unassigned sentinel rather than throwing — mirrors Error.None's
      "never use null for the empty case" convention; named after Microsoft.FeatureManagement's own
      VariantAssignmentReason.None semantics ("variant allocation did not happen; no variant is assigned")

FeatureDefinition  (sealed record)
    .Name                                                  → string
    .DefaultValue                                          → bool
    .Description                                           → string?

FeatureVariantDefinition  (sealed record — P-298/WO-049)
    .Name                                                  → string           (variant identifier, e.g. "VariantB")
    .Weight                                                → int              (allocation weight, e.g. percentage points; relative to sibling variants for the same feature)
    .Configuration                                         → string?          (configuration payload surfaced to callers via FeatureVariant.Configuration on assignment)
    — sibling record to FeatureDefinition for the definitions API; models one weighted allocation branch of
      a gradual-rollout/A-B-experiment feature, not a replacement for FeatureDefinition's boolean shape

AddSharedKernelFeatureManagement(IConfiguration config)
    → registers IFeatureManager backed by Microsoft.FeatureManagement.IVariantFeatureManager (a superset of
      the older Microsoft.FeatureManagement.IFeatureManager, confirmed via reflection to carry both the
      boolean IsEnabledAsync members and the variant GetVariantAsync members — one injected dependency
      covers the whole adapter); the variant path (GetVariantAsync) requires no additional configuration
      beyond what Microsoft.FeatureManagement's own variant/allocation configuration schema already needs
    — `config` MUST be the application's ROOT IConfiguration — confirmed empirically (P-298/WO-049):
      Microsoft.FeatureManagement's ConfigurationFeatureDefinitionProvider resolves the legacy .NET-schema
      flag dictionary ("FeatureManagement": {...}) and the Microsoft Feature Management variant/allocation
      schema ("feature_management": { "feature_flags": [...] }, an entirely different, unscoped, snake_case
      root key) independently, both relative to whatever IConfiguration instance it is given. Passing a
      value already scoped to the "FeatureManagement" section (e.g. via config.GetSection("FeatureManagement"))
      silently makes the variant schema unreachable — plain boolean flags still resolve either way, which is
      exactly why an earlier version of this method's implementation carried this defect undetected
```

### `SharedKernel.Cryptography` — public surface

```
IOneWayHasher
    Hash(string secret)                                         → string                  (self-describing encoded output: algorithm id + iteration count + salt + subkey, Base64)
    Verify(string hash, string secret)                          → HashVerificationResult
    — secret-agnostic one-way hash/verify contract: "password" is one example consumer, not the sole
      purpose. Equally suited to API keys, recovery codes, security-question answers, or any other
      one-way, slow, salted-hash-then-verify secret.

HashVerificationResult  (enum)
    Failed | Success | SuccessRehashNeeded
    — SuccessRehashNeeded: the stored hash used an older iteration count/algorithm version; caller should re-Hash and persist the new value

Pbkdf2OneWayHasher  (sealed class, implements IOneWayHasher)
    — PBKDF2-HMACSHA256 via Rfc2898DeriveBytes.Pbkdf2; default 600,000 iterations (OWASP 2023+ guidance);
      iteration count is read from CryptographyOptions.Pbkdf2Iterations and embedded in the output so
      raising it later never invalidates already-stored hashes
    — Renamed from Pbkdf2PasswordHasher (WO-034, P-210–P-213): same mechanism, generalized name and
      parameter (secret, not password) — the PBKDF2 algorithm, output format, and rehash-detection
      behavior are unchanged

ISymmetricEncryptionService
    Encrypt(byte[] plaintext)                                   → EncryptedPayload        (always encrypts with the provider's current key; blocks a real thread if the registered IEncryptionKeyProvider is genuinely network-bound — see EncryptAsync)
    EncryptAsync(byte[] plaintext, CancellationToken ct = default) → ValueTask<EncryptedPayload>   (P-446/WO-068, shipped — never blocks a thread; prefer this on hot/high-throughput paths)
    Decrypt(EncryptedPayload payload)                           → Result<byte[]>          (failure: Error.Unexpected — tamper, wrong key, or unknown KeyId; never throws CryptographicException directly; same thread-blocking caveat as Encrypt)
    DecryptAsync(EncryptedPayload payload, CancellationToken ct = default) → ValueTask<Result<byte[]>>   (P-446/WO-068, shipped — async counterpart of Decrypt)
    EncryptToString(string plaintext)                           → string                  (convenience: UTF-8 → Encrypt → single self-describing Base64 string, KeyId+Nonce+Ciphertext+Tag packed together)
    EncryptToStringAsync(string plaintext, CancellationToken ct = default) → ValueTask<string>   (P-446/WO-068, shipped)
    DecryptToString(string encoded)                             → Result<string>          (convenience inverse of EncryptToString)
    DecryptToStringAsync(string encoded, CancellationToken ct = default) → ValueTask<Result<string>>   (P-446/WO-068, shipped)
    — the four sync members are RETAINED (not removed) and bridge internally onto the async
      `IEncryptionKeyProvider` via `.GetAwaiter().GetResult()` — genuinely non-blocking when the provider
      resolves synchronously (the config-based default, or a `CachedEncryptionKeyProvider` cache hit), but
      BLOCKS A REAL THREAD when the provider is genuinely network-bound on a cache miss; every retained sync
      member's XML docs state this IN CAPITALS and direct hot-path callers to the `*Async` overloads instead.
      `AesGcmEncryptionService` shares one pure, synchronous `EncryptCore`/`DecryptCore` pair between the sync
      and async members so both call shapes are guaranteed byte-identical for the same input

EncryptedPayload  (sealed record)
    .KeyId                                                      → string                  (which key version encrypted this payload)
    .Nonce                                                      → byte[]                  (96-bit, random per call — never reused)
    .Ciphertext                                                 → byte[]
    .Tag                                                        → byte[]                  (128-bit AES-GCM authentication tag)

AesGcmEncryptionService  (sealed class, implements ISymmetricEncryptionService)
    — AES-256-GCM via System.Security.Cryptography.AesGcm; authenticated (tamper-evident) encryption only —
      never an unauthenticated mode such as CBC/ECB

IEncryptionKeyProvider  (BREAKING as of P-446/WO-068, shipped — the prior synchronous shape is gone)
    GetCurrentKeyAsync(CancellationToken ct = default)          → ValueTask<CryptographicKey>        (used for every new Encrypt/EncryptAsync call)
    GetKeyAsync(string keyId, CancellationToken ct = default)   → ValueTask<CryptographicKey?>       (used to Decrypt older payloads; null if the key was retired/unknown)
    — implemented by the consuming service (Key Vault, environment config, secret store); SharedKernel.Cryptography
      ships no default implementation and holds no key material itself
    — the prior synchronous `GetCurrentKey()`/`GetKey(string)` members were REMOVED OUTRIGHT (not kept as a
      parallel overload) — every implementer must migrate; a synchronous/config-based implementer migrates
      mechanically by returning an already-completed `new ValueTask<CryptographicKey>(...)`, no behavior
      change required. A genuine KMS/HSM-backed implementer (Azure Key Vault, AWS KMS, Vault) can now `await`
      its SDK call directly instead of needing a blocking-on-async anti-pattern. Fail-closed is structural: an
      unreachable KMS must propagate a thrown exception — never a silent no-encryption fallback

IEnvelopeEncryptionProvider  (P-446/WO-068, shipped — additive, distinct from IEncryptionKeyProvider)
    GenerateDataKeyAsync(CancellationToken ct = default)        → ValueTask<EnvelopeDataKey>
    UnwrapDataKeyAsync(byte[] wrappedDataKey, string masterKeyId, CancellationToken ct = default) → ValueTask<Result<byte[]>>
    — asks a KMS/HSM-held master key to generate+wrap a fresh symmetric data key, or unwrap a previously-
      wrapped one, without master key material ever leaving the KMS boundary; a provider may implement both
      `IEncryptionKeyProvider` and `IEnvelopeEncryptionProvider` (neither interface is collapsed into the
      other). Ships no default implementation — same "consumer implements" shape as `IEncryptionKeyProvider`

EnvelopeDataKey  (sealed record — P-446/WO-068, shipped)
    .PlaintextKey                                               → byte[]                  (use immediately, then discard — NEVER persist)
    .WrappedKey                                                 → byte[]                  (the only form safe to persist)
    .MasterKeyId                                                → string                  (required to later unwrap WrappedKey)

CachedEncryptionKeyProvider  (sealed class, implements IEncryptionKeyProvider — P-446/WO-068, shipped, additive)
    ctor(IEncryptionKeyProvider inner, TimeProvider timeProvider, TimeSpan ttl)
    — bounded-TTL decorator over any IEncryptionKeyProvider: a cache hit inside the TTL window never calls
      the inner provider; an expired/missing entry always re-fetches. Single-flight per cache key (the
      current key, or one specific keyId): N concurrent callers past expiry trigger exactly one inner-provider
      call — implemented via a ConcurrentDictionary compare-and-swap race combined with a Lazy<Task<T>> whose
      factory itself executes at most once even under contention. A failed refresh propagates the thrown
      exception to every caller awaiting that single-flight resolution — NEVER a stale fallback — and the
      failed slot is discarded so the next call retries rather than staying permanently poisoned. Ships with
      no package-owned DI extension — mirrors the IIdGenerator/SystemClock(TimeProvider) no-extension
      precedent; composed explicitly at the consumer's own composition root

CryptographicKey  (sealed record)
    .Id                                                         → string
    .Material                                                   → byte[]                  (32 bytes for AES-256)

IAsymmetricSignatureService
    Sign(byte[] data, string keyId)                             → byte[]
    Verify(byte[] data, byte[] signature, string keyId)         → bool

RsaSignatureService  (sealed class, implements IAsymmetricSignatureService)
    — RSA, 2048-bit minimum, PSS padding, SHA-256

EcdsaSignatureService  (sealed class, implements IAsymmetricSignatureService)
    — ECDSA on the P-256 curve, SHA-256

IAsymmetricKeyProvider
    GetRsaKey(string keyId)                                     → RSA                     (throws KeyNotFoundException if unknown)
    GetEcdsaKey(string keyId)                                   → ECDsa                   (throws KeyNotFoundException if unknown)
    — implemented by the consuming service (Key Vault, certificate store, environment config); mirrors IEncryptionKeyProvider
      for the asymmetric-signing case. SharedKernel.Cryptography ships no default implementation and holds no key material.
      Introduced during P-207 implementation — required because IAsymmetricSignatureService.Sign/Verify take only a `keyId`
      string with no resolution mechanism defined; this is the resolver RsaSignatureService/EcdsaSignatureService depend on.

IHmacSigner
    Sign(byte[] data, byte[] secret)                            → byte[]
    Verify(byte[] data, byte[] signature, byte[] secret)        → bool

HmacSha256Signer  (sealed class, implements IHmacSigner)
    — HMACSHA256; Verify uses CryptographicOperations.FixedTimeEquals — never `==` or `SequenceEqual`
      on secret-derived bytes (timing-attack resistant)

ISecureRandomGenerator
    NextBytes(int length)                                       → byte[]
    NextToken(int length = 32)                                  → string                  (URL-safe Base64, no padding — safe for query strings / headers)

CryptoRandomGenerator  (sealed class, implements ISecureRandomGenerator)
    — backed by System.Security.Cryptography.RandomNumberGenerator; System.Random and Guid.NewGuid()
      are never acceptable substitutes for this interface

IContentHasher  (P-296/WO-049)
    ComputeHash(byte[] content)                                 → byte[]                  (raw digest bytes)
    ComputeHash(Stream content)                                 → byte[]                  (streaming — never materializes the full content in memory)
    ComputeHashAsync(Stream content, CancellationToken ct = default) → ValueTask<byte[]>   (async streaming variant for large blob uploads)
    — fast, non-salted, non-iterated cryptographic digest for NON-SECRET content-fingerprinting use cases
      only: object-storage ETags/checksums, content-addressable deduplication keys, cache-key derivation
      from a payload body. Deliberately the architectural opposite of IOneWayHasher: IOneWayHasher is
      deliberately slow (600,000 PBKDF2 iterations) to resist brute-force attacks on secrets — exactly the
      wrong tool for hashing a 50MB upload to compute its ETag. NEVER use IContentHasher for passwords, API
      keys, recovery codes, or any other secret — use IOneWayHasher for those. The two contracts must never
      be conflated or merged into one.

ContentHasherExtensions  (static class — convenience encodings built on IContentHasher)
    ComputeHashHex(this IContentHasher hasher, byte[] content)      → string    (lowercase hex-encoded digest)
    ComputeHashBase64(this IContentHasher hasher, byte[] content)   → string    (Base64-encoded digest)

Sha256ContentHasher  (sealed class, implements IContentHasher)
    — backed by System.Security.Cryptography.SHA256.HashData(byte[])/.HashData(Stream)/.HashDataAsync(Stream,...)
      one-shot static BCL APIs (streaming-safe internally, no manual IncrementalHash bookkeeping needed);
      algorithm-swappable by construction — a future second digest implementation could register a second
      IContentHasher without changing this contract; only SHA-256 ships in this phase

Base32  (static class — SHIPPED, P-451/WO-069)
    Encode(byte[] data)                                         → string                  (RFC 4648 Base32, unpadded — matches authenticator-app expectations)
    Decode(string base32Text)                                   → Result<byte[]>          (never throws on malformed input, mirrors Decrypt/Decompress's "expected failure surfaces as Result" shape)

IHotpGenerator  (SHIPPED, P-451/WO-069)
    GenerateCode(byte[] secret, long counter, int digits = 6, HotpAlgorithm algorithm = Sha1)   → string
    ValidateCode(byte[] secret, string code, long counter, int digits = 6, HotpAlgorithm algorithm = Sha1) → bool
    — RFC 4226 core; dynamic truncation per §5.3; ValidateCode compares via CryptographicOperations.FixedTimeEquals
      on the ASCII-encoded candidate/expected code (never == or SequenceEqual on secret-derived output)

HotpAlgorithm  (enum — SHIPPED, P-451/WO-069)
    Sha1 (default, per RFC 4226) | Sha256 | Sha512

HotpGenerator  (sealed class, implements IHotpGenerator — SHIPPED, P-451/WO-069)
    — backed by HMACSHA1/HMACSHA256/HMACSHA512 depending on HotpAlgorithm; digits constrained to 1–9 (int-safe modulo)

ITotpGenerator  (SHIPPED, P-451/WO-069)
    GenerateCode(byte[] secret, int digits = 6, int stepSeconds = 30, HotpAlgorithm algorithm = Sha1)  → string  (uses IClock for "now")
    GenerateCode(byte[] secret, DateTimeOffset timestamp, int digits = 6, int stepSeconds = 30, HotpAlgorithm algorithm = Sha1) → string  (explicit-timestamp overload, for testability)
    ValidateCode(byte[] secret, string code, int digits = 6, int stepSeconds = 30, int driftWindow = 1, HotpAlgorithm algorithm = Sha1) → bool  (uses IClock for "now")
    ValidateCode(byte[] secret, string code, DateTimeOffset timestamp, int digits = 6, int stepSeconds = 30, int driftWindow = 1, HotpAlgorithm algorithm = Sha1) → bool  (explicit-timestamp overload, for testability — additive to the design, mirrors GenerateCode's own testable-overload shape)
    — RFC 6238; composes IHotpGenerator internally via counter = floor(unixSeconds / stepSeconds); driftWindow
      is the number of steps before/after the current step to accept; time source is IClock — NEVER DateTime.UtcNow

TotpGenerator  (sealed class, implements ITotpGenerator — SHIPPED, P-451/WO-069)

TotpProvisioningUri  (static class — SHIPPED, P-451/WO-069)
    Build(string issuer, string accountName, byte[] secret, int digits = 6, int stepSeconds = 30, HotpAlgorithm algorithm = Sha1) → Uri
    — produces the otpauth://totp/{Issuer}:{AccountName}?secret=...&issuer=...&digits=...&period=...&algorithm=...
      "Key Uri Format" authenticator apps (Google Authenticator and compatible) consume for enrollment

ITotpReplayGuard  (SHIPPED, P-451/WO-069)
    HasBeenUsedAsync(string identityKey, string code, CancellationToken ct = default)          → ValueTask<bool>
    MarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default) → ValueTask
    — implemented by the consuming service (in-memory for single-instance dev, Redis-backed for production
      multi-replica); mirrors 12.Security.Oidc's DPoP replay-check seam — never a direct 02.Caching reference
      from this package; SharedKernel.Cryptography ships no default implementation

TotpVerifier  (sealed class — SHIPPED, P-451/WO-069)
    VerifyAsync(string identityKey, byte[] secret, string code, CancellationToken ct = default) → ValueTask<bool>
    — composes ITotpGenerator.ValidateCode + ITotpReplayGuard; returns false on an invalid code OR a code
      already used within its validity window; calls MarkUsedAsync only after a fresh valid code, before
      returning true — this is the type that actually prevents double-acceptance of one code, not ValidateCode
      itself (which stays pure/stateless by design); internally uses ITotpGenerator's default-parameter
      ValidateCode (digits=6, stepSeconds=30, driftWindow=1) and derives MarkUsedAsync's validityWindow as
      stepSeconds * (2 * driftWindow + 1) = 90 seconds at those defaults

RecoveryCodeGenerator  (sealed class — SHIPPED, P-451/WO-069)
    GenerateCodes(int count = 10, int lengthBytes = 5)          → IReadOnlyList<string>   (via ISecureRandomGenerator)
    — generates plaintext backup codes shown once to the user; NEVER persists or hashes them itself — hashing
      at rest via the existing IOneWayHasher before persistence is the consuming service's responsibility,
      exactly like any other secret

CryptographyOptions  (bound via IOptions<T>; validated via SharedKernel.Configuration's AddValidatedOptions)
    .Pbkdf2Iterations                                           → int                     (default 600_000)
    .DefaultSigningKeyId                                        → string?

AddSharedKernelCryptography(IConfiguration configuration)
    → registers CryptographyOptions (validated, ValidateOnStart), IOneWayHasher, ISymmetricEncryptionService,
      IAsymmetricSignatureService, IHmacSigner, ISecureRandomGenerator, and (as of P-296/WO-049)
      IContentHasher as singletons (all are stateless and thread-safe); as of P-451/WO-069, SHIPPED, additionally
      registers IHotpGenerator, ITotpGenerator, and TotpVerifier as singletons — ITotpReplayGuard is NEVER
      registered by this package, the consuming service supplies its own. ITotpGenerator additionally requires
      the consumer to separately register SharedKernel.Primitives.Clocks.IClock (e.g. services.AddClock()) and
      TotpVerifier additionally requires the consumer's own ITotpReplayGuard — this method does not register
      either, so resolving ITotpGenerator/TotpVerifier without them throws at resolution time, not registration time
    — IAsymmetricSignatureService has two concrete implementations sharing one interface: RsaSignatureService
      is registered as the unkeyed default; both RsaSignatureService and EcdsaSignatureService are additionally
      registered as .NET 8+ keyed singletons via CryptographyServiceCollectionExtensions.RsaSignatureServiceKey
      ("Rsa") / EcdsaSignatureServiceKey ("Ecdsa"). Resolve ECDSA explicitly via
      provider.GetRequiredKeyedService<IAsymmetricSignatureService>(EcdsaSignatureServiceKey).
    — does NOT register IEncryptionKeyProvider, IAsymmetricKeyProvider, or any signing key material — the
      consuming service supplies its own IEncryptionKeyProvider and IAsymmetricKeyProvider (Key Vault,
      environment config, certificate store, etc.); this package never ships default key material
```

### `SharedKernel.Compression` — public surface (P-297/WO-049)

```
IPayloadCompressor
    Compress(byte[] data)                                       → byte[]
    Compress(Stream input, Stream output)                       → void                    (writes compressed bytes of input to output; both streams caller-owned)
    CompressAsync(Stream input, Stream output, CancellationToken ct = default) → Task
    Decompress(byte[] compressed)                                → Result<byte[]>          (Error.Unexpected on corrupt/truncated input — never throws InvalidDataException OR InvalidOperationException directly, see remarks)
    Decompress(Stream input, Stream output)                      → Result                  (non-generic — the decompressed payload already landed in the caller's output stream, no typed value to carry)
    DecompressAsync(Stream input, Stream output, CancellationToken ct = default) → Task<Result>
    — generic, cross-cutting compress/decompress of an arbitrary byte payload or stream — the direct sibling
      of ISymmetricEncryptionService's "general-purpose encrypt/decrypt of arbitrary payloads" role: same
      shape, same zero-dependency BCL-only constraint, orthogonal concern
    — compression and encryption are frequently combined; the correct order is always compress-THEN-encrypt,
      never the reverse — compressing already-encrypted/high-entropy ciphertext wastes CPU for no size
      benefit, since ciphertext has no redundancy left to compress. This package never compresses a payload
      that has already passed through ISymmetricEncryptionService.Encrypt
    — CORRECTED post-implementation (P-297/WO-049): the design originally assumed both algorithms surface
      corrupt input as InvalidDataException only (mirroring GZipStream). Empirically verified against the
      shipped BCL: GZipStream does throw InvalidDataException, but BrotliStream's decoder throws
      InvalidOperationException ("Decoder ran into invalid data") for corrupt Brotli input instead. Both
      BrotliPayloadCompressor and GZipPayloadCompressor catch `InvalidDataException or InvalidOperationException`
      in every Decompress overload — never InvalidDataException alone
    — ALSO DISCOVERED empirically (P-297/WO-049): neither BrotliStream nor GZipStream reliably detects a
      compressed stream missing only its *trailing* bytes (genuine truncated-transfer scenario) as an error —
      GZipStream does not validate its own trailing CRC32/ISIZE footer on read, and BrotliStream has no fixed
      magic-number header the way gzip does, so it can decode a truncated stream's remaining bytes without
      raising anything at all. Prefix-truncation (missing the leading bytes) IS reliably caught for gzip via
      its magic number, but still not for Brotli. Only genuine bit-level corruption and unrecognized/garbage
      input are reliably detected for both algorithms — this is a confirmed BCL characteristic, not a defect
      in this package. Services requiring guaranteed end-to-end truncation detection must pair compression
      with a separate integrity check (e.g. IContentHasher or a known expected length), never rely on the
      compression format's own error signaling alone

BrotliPayloadCompressor  (sealed class, implements IPayloadCompressor)
    — backed by System.IO.Compression.BrotliStream; the default, best-ratio choice for the JSON/text-shaped
      payloads this platform mostly moves; registered as both the unkeyed default AND the "Brotli"-keyed
      singleton

GZipPayloadCompressor  (sealed class, implements IPayloadCompressor)
    — backed by System.IO.Compression.GZipStream; a keyed alternate for interop with systems that
      specifically require gzip; registered only as the "GZip"-keyed singleton (mirrors EcdsaSignatureService's
      keyed-only registration in SharedKernel.Cryptography — no unkeyed registration)

CompressionOptions  (bound via IOptions<T>; validated via SharedKernel.Configuration's AddValidatedOptions)
    .Level                                                       → System.IO.Compression.CompressionLevel  (default: CompressionLevel.Optimal)

AddSharedKernelCompression(IConfiguration configuration)
    → registers CompressionOptions (validated, ValidateOnStart); registers BrotliPayloadCompressor as both
      the unkeyed IPayloadCompressor default and the CompressionServiceCollectionExtensions.BrotliPayloadCompressorKey
      ("Brotli") keyed singleton; registers GZipPayloadCompressor as the GZipPayloadCompressorKey ("GZip")
      keyed singleton only. Resolve GZip explicitly via
      provider.GetRequiredKeyedService<IPayloadCompressor>(GZipPayloadCompressorKey).
```

### `SharedKernel.Validation` — public surface (P-443/WO-067, SHIPPED — eighth published package)

```
IbanValidator / BicValidator / PanValidator / IsoCurrencyValidator / IsoCountryValidator / E164PhoneValidator / VatValidator  (static classes)
    IsValid(string? value)                                      → bool
    Validate(string? value)                                     → Result   (non-generic — format checks carry no typed value payload)
    — IbanValidator uses mod-97 + a per-country length table (not a fixed-length assumption)
    — PanValidator additionally exposes DetectNetwork(string value) → CardNetwork
    — VatValidator is a baseline cross-jurisdiction format check — XML docs must state VAT format varies
      enormously per country and this is not an exhaustive per-country validator

CardNetwork  (plain enum — not a SmartEnum, since BIN-range detection carries no per-value behavior beyond the name)
    Unknown | Visa | Mastercard | Amex | Discover

INationalIdValidator
    .CountryCode                                                → string   (ISO 3166 alpha-2)
    IsValid(string idNumber)                                    → bool

INationalIdValidatorRegistry
    TryGetValidator(string countryCode, out INationalIdValidator? validator) → bool

NationalIdValidatorRegistry  (sealed class, implements INationalIdValidatorRegistry)
    — ConcurrentDictionary-backed, thread-safe, pre-seeded with TckNationalIdValidator at "TR"

TckNationalIdValidator  (sealed class, implements INationalIdValidator)
    — Turkey's 11-digit TCKN checksum algorithm; the built-in default given this platform's primary market

ValidationErrorCodes  (static class — package-local nested string-constant catalog, mirrors ErrorCodes's shape
    but NEVER added to SharedKernel.Primitives.ErrorCodes)
    ValidationErrorCodes.Iban.InvalidFormat / .InvalidCheckDigit / .InvalidLength
    ValidationErrorCodes.Bic.InvalidFormat
    ValidationErrorCodes.Pan.FailedLuhnCheck / .UnknownNetwork
    ValidationErrorCodes.Currency.UnknownCode
    ValidationErrorCodes.Country.UnknownCode
    ValidationErrorCodes.Phone.InvalidFormat
    ValidationErrorCodes.Vat.InvalidFormat
    ValidationErrorCodes.NationalId.UnknownCountry / .InvalidChecksum

GuardValidationExtensions  (static class — Guard.Against.* extensions on IGuardClause, functional path only;
    Guard.Throw.* parity is intentionally out of scope — SharedKernel.Guards' Throw nested class is hardcoded
    and cannot be extended from an outside package)
    InvalidIban(this IGuardClause, string? value)               → Error?
    InvalidBic(this IGuardClause, string? value)                → Error?
    InvalidPan(this IGuardClause, string? value)                → Error?
    InvalidCurrencyCode(this IGuardClause, string? value)       → Error?
    InvalidCountryCode(this IGuardClause, string? value)        → Error?
    InvalidPhoneNumber(this IGuardClause, string? value)        → Error?
    InvalidVatNumber(this IGuardClause, string? value)          → Error?
    InvalidNationalId(this IGuardClause, string? value, string countryCode, INationalIdValidatorRegistry registry) → Error?
    — the one guard requiring an explicit registry instance parameter, since national-ID validation is
      registry-based rather than compile-time-generic like InvalidSmartEnum

AddSharedKernelValidation()
    → registers INationalIdValidatorRegistry as a singleton (pre-seeded default); exposes a chained
      .AddNationalIdValidator<TValidator>() extension for a consuming service to register additional countries
```

### `SharedKernel.Validation.FluentValidation` — public surface (P-444/WO-067, SHIPPED — ninth published package, depends on P-443)

```
ValidationRuleBuilderExtensions  (static class — IRuleBuilder<T, string> extensions)
    MustBeValidIban<T>(this IRuleBuilder<T, string>)             → IRuleBuilderOptionsConditions<T, string>
    MustBeValidBic<T>(this IRuleBuilder<T, string>)              → IRuleBuilderOptionsConditions<T, string>
    MustBeValidPan<T>(this IRuleBuilder<T, string>)              → IRuleBuilderOptionsConditions<T, string>
    MustBeValidCurrencyCode<T>(this IRuleBuilder<T, string>)     → IRuleBuilderOptionsConditions<T, string>
    MustBeValidCountryCode<T>(this IRuleBuilder<T, string>)      → IRuleBuilderOptionsConditions<T, string>
    MustBeValidPhoneNumber<T>(this IRuleBuilder<T, string>)      → IRuleBuilderOptionsConditions<T, string>
    MustBeValidVatNumber<T>(this IRuleBuilder<T, string>)        → IRuleBuilderOptionsConditions<T, string>
    MustBeValidNationalId<T>(this IRuleBuilder<T, string>, Func<T, string> countryCodeSelector, INationalIdValidatorRegistry registry) → IRuleBuilderOptionsConditions<T, string>
    — the FluentValidation 11.x return type is IRuleBuilderOptionsConditions<T,TProperty>, NOT
      IRuleBuilder<T,string>/IRuleBuilderOptions<T,string> — confirmed via a real CS0266 compiler error; the
      input parameter type is IRuleBuilder<T,string>, but its fluent-chaining return type differs
    — every rule is built on FluentValidation's Custom(...) extension (never Must(predicate).WithErrorCode(...)),
      so it can inspect WHICH ValidationErrorCodes constant the underlying static validator actually produced
      and attach that exact code to the ValidationFailure — Must+WithErrorCode can only ever attach one fixed
      code per rule, which silently breaks parity for any validator with more than one failure code
      (IbanValidator.Validate alone can fail with InvalidFormat/InvalidCheckDigit/InvalidLength)
    — uses ValidationContext<T>.PropertyPath, not the deprecated PropertyName (CS0618 in FluentValidation 11.x;
      same value, no warning)
    — a rule built on Custom(...) does not honor a chained .WithMessage(...)/.WithErrorCode(...) afterward —
      the message/code always come from the underlying SharedKernel.Validation validator
    — documented composition recipe with 05.Application.Behaviors' ValidationBehavior: an AbstractValidator<T>
      calling .MustBeValidIban() inside a rule already wired into that pipeline behavior, no extra plumbing —
      but ValidationBehavior itself (confirmed by reading its source) currently projects
      Error.Validation(failure.PropertyName, failure.ErrorMessage): the FluentValidation PROPERTY NAME becomes
      the downstream Error.Code, NOT failure.ErrorCode. A consumer wanting the finer-grained ValidationErrorCodes
      constant on the outward Error reads failure.ErrorCode directly — that mapping is a consumer/future
      ValidationBehavior decision, not something this package or ValidationBehavior does today
```

### `SharedKernel.Cryptography.KeyVault.Azure` — public surface (P-447/WO-068, SHIPPED — tenth published package, depends on P-446)

```
AzureKeyVaultEncryptionKeyProvider  (sealed class, implements IEncryptionKeyProvider + IEnvelopeEncryptionProvider)
    — direct-retrieval mode (IEncryptionKeyProvider) is built INTERNALLY ON TOP OF the envelope-wrap mode
      (IEnvelopeEncryptionProvider): GetCurrentKeyAsync generates/caches a local AES-256 data key via
      GenerateDataKeyAsync, exposing only the already-in-memory plaintext data key as CryptographicKey.Material
      — never a second, parallel raw-export code path (Azure Key Vault Keys does not export raw HSM-protected
      key material by default). The vault's own master key material never crosses the process boundary either way
    — envelope-wrap mode (IEnvelopeEncryptionProvider) is the vendor-idiomatic path, backed by
      CryptographyClient.WrapKeyAsync/UnwrapKeyAsync (RSA-OAEP or AES-KW depending on key type)
    — fails closed: any Azure SDK exception (unreachable vault, RequestFailedException for permission/auth
      failure) propagates directly from every member — no silent fallback
    — ships ZERO caching of its own — composes with SharedKernel.Cryptography's CachedEncryptionKeyProvider
      (P-446) externally rather than duplicating it; two independent caches with different TTL semantics
      must never both wrap the same provider

AzureKeyVaultCryptographyOptions  (bound via IOptions<T>; validated via SharedKernel.Configuration's AddValidatedOptions)
    .VaultUri                                                   → Uri
    .KeyNames                                                   → IReadOnlyDictionary<string, string>   (keyId → Azure Key Vault key name)
    .Credential                                                 → TokenCredential?   (defaults to Azure.Identity.DefaultAzureCredential when null)

AddSharedKernelAzureKeyVaultCryptography(IConfiguration configuration)
    → registers AzureKeyVaultCryptographyOptions (validated, ValidateOnStart) and AzureKeyVaultEncryptionKeyProvider
      as both IEncryptionKeyProvider and IEnvelopeEncryptionProvider (same singleton instance, two service-type
      registrations); does NOT register any caching decorator
```

### `SharedKernel.DataPrivacy` — public surface (P-474/WO-076, SHIPPED — eleventh published package)

```
DataClassification  (enum)
    Public | Internal | Confidential | Restricted

DataClassificationAttribute  (sealed class : Attribute, [AttributeUsage(Property | Field)])
    .Classification                                             → DataClassification
    — pure metadata; NEVER read via reflection in production code — sole sanctioned consumer is
      00.Governance's compile-time analyzer (P-476) and human documentation

SensitiveDataCategory  (enum)
    Pii | PaymentCard | Credential | Health

SensitiveDataCategoryAttribute  (sealed class : Attribute, [AttributeUsage(Property | Field)])
    .Category                                                   → SensitiveDataCategory
    — same "metadata only, never reflected over in production" constraint as DataClassificationAttribute

PiiMasking  (static class — pure, allocation-minimal, deterministic, null/empty-safe functions; zero reflection)
    Email(string? email)                                        → string   (e.g. "j.doe@example.com" → "j***@example.com")
    Phone(string? phoneNumber)                                  → string   (keeps last 2–4 digits)
    Pan(string? cardNumber)                                     → string   (keeps last 4 digits only)
    Suppress(string? value)                                     → string   (fixed sentinel regardless of input — for fields with no safe partial reveal)
    — SHIPPED (P-474): the design left several exact thresholds/edge cases unspecified; every one was
      resolved deliberately during implementation and is now the binding contract (see each method's own
      XML docs for the full statement, and 01.Core/README.md for worked examples):
        * Email: null/empty/whitespace → "" ; local part >1 char → firstChar + fixed "***" (never
          proportional to real length); local part 0-1 char → "***" only (a 1-char local part would be
          fully disclosed by "firstChar+mask" otherwise); no "@" anywhere → the whole value is masked by
          the same local-part rule, no "@domain" suffix; 2+ "@" characters split on the LAST one (the
          most domain-like trailing segment)
        * Phone: only Unicode digit characters count/are masked — every non-digit separator (+, space,
          -, (, )) is preserved verbatim in its original position; reveal window is total-digit-count-
          dependent (>=4 digits -> last 4; 2-3 digits -> last 2; <2 digits -> none), resolving D-59's
          "2-4" range explicitly rather than leaving it ambiguous
        * Pan: fixed "last 4" always — deliberately NOT narrowed for a sub-4-digit input the way Phone
          is, since the design specified Pan's rule as a fixed count, not a range; digit counting/
          separator handling otherwise mirrors Phone
        * Suppress: returns the same fixed RedactedSentinel ("[REDACTED]") for every input, including null
      T-58's "no reflection anywhere in the package" claim is verified by a compiled-assembly
      System.Reflection.Metadata/PEReader scan of the production DLL's TypeReference table (not a source
      grep) — flags any non-attribute System.Reflection.* type reference, explicitly excluding attribute-
      suffixed names since the SDK's own auto-generated AssemblyCompanyAttribute/AssemblyMetadataAttribute
      (driven by this repo's centralized NuGet packaging metadata) would otherwise false-positive on every
      SharedKernel package. This pattern (real PE-metadata inspection over a source grep) is reusable for
      any future "no reflection in this package" claim elsewhere in 01.Core

IDataSubjectRequestHandler  (no default/reflection-based implementation ships — consuming service implements
    against its own data)
    ExportDataAsync(string subjectId, CancellationToken ct = default)      → Task<Result<DataSubjectExportBundle>>
    RequestErasureAsync(string subjectId, CancellationToken ct = default)  → Task<Result<DataSubjectErasureReceipt>>
    — cross-service erasure orchestration is explicitly out of scope for this package

DataSubjectExportBundle  (sealed record)
    .SubjectId                                                  → string
    .ExportedAtUtc                                              → DateTimeOffset
    .Data                                                       → IReadOnlyDictionary<string, object?>

DataSubjectErasureReceipt  (sealed record)
    .SubjectId                                                  → string
    .ErasedAtUtc                                                → DateTimeOffset
    .RecordsAffected                                            → int
```

### `SharedKernel.Localization` — public surface (P-482/WO-078, SHIPPED — twelfth published package)

```
ILocalizationCatalog
    TryGetString(string code, CultureInfo culture, out string? value)      → bool
    — keyed on the same `code` string 01.Core.Primitives.Error's factories already require; returns false and
      out value is null on an unregistered/untranslated lookup — NEVER throws, NEVER returns a blank string.
      The caller (14.Presentation's Error.ToProblemDetails(), P-484) owns the fallback-to-throw-site-message
      behavior — this contract only ever signals "not found"

InMemoryLocalizationCatalog  (sealed class, implements ILocalizationCatalog)
    AddTranslation(string code, CultureInfo culture, string value)         → InMemoryLocalizationCatalog  (chained builder)
    — dictionary-backed, keyed by (code, CultureInfo.Name), ordinal/case-sensitive on code; the zero-config
      default for a service with a handful of translated codes
    — SHIPPED (P-482): the design left parent-culture fallback unspecified — resolved deliberately during
      implementation as this type's own behavior (not mandated by ILocalizationCatalog itself): a lookup for a
      specific culture (e.g. tr-TR) walks up through each CultureInfo.Parent (tr) and finally
      CultureInfo.InvariantCulture before returning false, mirroring standard ResourceManager/IStringLocalizer
      fallback semantics. Seed a translation under CultureInfo.InvariantCulture for a universal default reached
      by every culture with no more specific entry of its own

StringLocalizerLocalizationCatalog  (sealed class, implements ILocalizationCatalog)
    — wraps a caller-supplied Microsoft.Extensions.Localization.IStringLocalizerFactory + a resource type/name;
      resolves TryGetString by checking the returned LocalizedString.ResourceNotFound; lets a service with full
      .resx-file tooling compose behind the same seam without a second bespoke pipeline
    — SHIPPED (P-482): never forwards LocalizedString.Value without checking ResourceNotFound first — a real
      IStringLocalizer returns the requested key itself as .Value on a miss, so forwarding blindly would surface
      the raw error code as a fake "translation," inverting the never-blank contract into something worse
    — SHIPPED (P-482): IStringLocalizer's indexer carries no per-call culture parameter in this framework version
      (confirmed by reflecting over the installed Microsoft.Extensions.Localization.Abstractions 10.0.11 assembly
      — no WithCulture member exists on the interface). TryGetString therefore temporarily swaps the ambient
      CultureInfo.CurrentUICulture for the duration of the synchronous call and restores it in a finally block

LocalizationServiceCollectionExtensions
    AddInMemoryLocalizationCatalog(...)                         — registers InMemoryLocalizationCatalog as ILocalizationCatalog
    AddStringLocalizerCatalog<TResource>()                      — registers StringLocalizerLocalizationCatalog as ILocalizationCatalog
    — two independent, mutually-exclusive opt-in registrations; deliberately NEVER named AddSharedKernelLocalization()
      — that name is reserved for 13.ServiceDefaults's separate culture-resolution middleware entry point (P-483)
```

---

## Implementation Rules

- `IHasSuccessFlag` is a **zero-member marker interface** — it must never grow properties or methods. Its sole purpose is `is IHasSuccessFlag` identity checks in pipeline behaviors; any behavioral addition would couple it to a specific consumer concern.
- `IResultOfT<T>` exposes **exactly** `IsSuccess`, `IsFailure`, and `Value` — the minimum surface needed for reflection-free `FailureResponseFactory`-style construction. It must never expose `.Error` (that would replicate the full `Result<T>` surface and encourage bypassing the concrete type).
- Neither `IHasSuccessFlag` nor `IResultOfT<T>` may carry a `[RequiresUnreferencedCode]` annotation — the AOT-clean guarantee is non-negotiable. If any future change would require such an annotation, redesign instead.
- `IFailureFactory<TSelf>` is a self-referential (CRTP) interface using a C# static abstract interface member (`static abstract TSelf Failure(Error error)`, constrained `where TSelf : IFailureFactory<TSelf>`) — this is the only sanctioned mechanism for reflection-free construction of a failure instance of an unknown closed `Result<T>` shape from just `TResponse`. It must never be satisfied via `Type.MakeGenericType`/`Type.GetMethod`/`MethodBase.Invoke` or any other reflection-based bridge — those are exactly the patterns this interface exists to eliminate.
- `Result<T>`'s existing `Failure(Error error)` static factory method also satisfies `IFailureFactory<Result<T>>` — no new member is introduced; do not add a second, differently-named static factory method to serve the interface.
- `Result` (non-generic) must never implement `IFailureFactory<Result>` — consumers needing a non-generic `Result` failure use a `TResponse == typeof(Result)` fast-path check in the consuming dispatcher, not this interface. This mirrors the `IResultOfT<T>` exclusion rationale.
- `IFailureFactory<TSelf>` may never carry a `[RequiresUnreferencedCode]` annotation — same hard constraint as `IHasSuccessFlag`/`IResultOfT<T>`.
- `Result` (non-generic readonly struct) implements `IHasSuccessFlag` but must **never** implement `IResultOfT<T>` — it carries no typed value payload and the interface's `Value` property would be unsound.
- `SharedKernel.Primitives` has **zero NuGet dependencies** — pure C# only.
- `SharedKernel.Guards` has **zero NuGet dependencies** — references only `SharedKernel.Primitives` and `SharedKernel.Core`.
- `Result<T>` is a **sealed class** (not a struct) — the zero-value problem with generic struct payloads makes struct unsound at scale.
- `Result` (non-generic) may be a **readonly struct** — it carries no typed value payload so the zero-value concern does not apply.
- `Result<T>` must never throw on its own operations (`.IsSuccess`, `.IsFailure`). Only `.Value` and `.Error` accessors throw `InvalidOperationException` on wrong access.
- `Error.None` is the sentinel — never use `null` to represent "no error".
- `ValidationResult` / `ValidationResult<T>` are **distinct** from `Result<T>` — use `ValidationResult` for compound multi-error input validation; use `Result<T>` for single-error operation outcomes. Never conflate the two.
- `ErrorCodes` uses a **nested static class string-constant** approach — not enums. Consuming packages may add local constants without forking the SharedKernel.
- `IClock` is the only permitted source of time in all packages — `DateTime.UtcNow` or `DateTimeOffset.UtcNow` direct usage anywhere in this domain is a hard violation.
- `SystemClock`'s internal `TimeProvider` adapter (P-295/WO-049) is an implementation detail, never a second public time source — `IClock`'s own surface (`UtcNow`, `Today`) does not change, and no `03.Domain`/`05.Application` call site is ever given license to inject or call `TimeProvider` directly instead of `IClock`. SK0001's "IClock is the only permitted time source" rule is unaffected by this internal change.
- `SystemClock` must default to `TimeProvider.System` when no `TimeProvider` is supplied — a consuming service that wants one shared, coordinated time source across `IClock`-consuming code and `TimeProvider`-consuming infrastructure (e.g. Polly v8 resilience pipelines) supplies its own `TimeProvider` to `SystemClock`'s constructor.
- `IIdGenerator`/`UuidV7IdGenerator` (P-293/WO-049) must never replace `Guid.NewGuid()` at any existing call site automatically — it is a purely opt-in alternative. `UuidV7IdGenerator` must be backed by `Guid.CreateVersion7()` only — never a hand-rolled UUIDv7 byte-layout implementation.
- `SmartEnum` value lookup (`FromValue`, `FromName`) must **not** use reflection in the hot path — use a static compile-time list built at type initialization.
- `LoggingEventIdRanges` is the single canonical source of domain-level `EventId` base values platform-wide — every package anywhere in the repo that authors `[LoggerMessage]` methods must derive its `EventId` values from this registry (domain base + local offset), never an ad hoc numeric literal disconnected from the domain's reserved block.
- `LoggingEventIdRanges` fields are `const int` only — never `static readonly`, never enum-backed, never computed at runtime. `[LoggerMessage(EventId = ...)]` requires a compile-time constant expression (e.g. `EventId = LoggingEventIdRanges.Application + 42`), so anything short of a `const` breaks every consumer at compile time.
- Adding a new folder-map domain (00–20 today) means adding exactly one new `const int` field to `LoggingEventIdRanges` — never renumbering or reassigning an existing domain's base value; doing so would silently invalidate every already-shipped `EventId` in that domain.
- `LoggingEventIdRanges` only reserves the domain-level 1000-wide block. It does not (and must not attempt to) enforce or assign the 100-wide per-package sub-blocks inside a multi-package domain — that allocation is each domain's own documentation responsibility (see `13.ServiceDefaults`/`00.Governance` for the mechanical collision-detection layer).
- `WellKnownHeaders`, `WellKnownBaggageKeys`, and `WellKnownTagKeys` fields are `public const string` only — never `static readonly`, never computed at runtime, mirroring the `LoggingEventIdRanges` const-only rule (headers are used in attribute-free contexts here, but the same "no ambiguity, no runtime mutation" rationale applies).
- `WellKnownHeaders` / `WellKnownBaggageKeys` / `WellKnownTagKeys` values must always match today's de facto platform standard exactly — this registry is a pure promotion of existing literals, never an opportunity to silently change an already-shipped header, baggage, or tag key name. Changing a value here is a breaking, cross-domain change requiring explicit work orders in every consuming domain, not a routine edit. (`WellKnownTagKeys` is a partial exception at introduction time — P-294/WO-049 introduces it pre-emptively, before any domain has shipped a conflicting tag-key literal, so there is no existing de facto value to preserve; once a domain adopts a `WellKnownTagKeys` constant, this rule applies to it in full.)
- Every consuming domain that redeclares a cross-service propagation identifier (correlation id, tenant id, error classification, or any future one) as a private literal or local `const` must be retrofitted to reference `WellKnownHeaders` / `WellKnownBaggageKeys` / `WellKnownTagKeys` instead — never leave a second, parallel literal alive once the shared constant exists. That retrofit is each consuming domain's own responsibility, not `01.Core`'s.
- `04.Contracts` must never become the home for cross-service propagation constants — `SharedKernel.Communication.Grpc` carries a hard governance rule (P-163, `GrpcNeverReferencesContracts`) forbidding a `04.Contracts` reference; routing these constants through `04.Contracts` would force reintroducing exactly the coupling that rule removes. `01.Core` is the only architecturally legal home.
- Base exceptions always carry an `Error` payload; string-only constructors are not allowed.
- Async railway extension methods must **not** use `async`/`await` on the outer extension body where the only async work is awaiting the input — avoid unnecessary state machine allocation. `ResultTry.TryAsync` is the one documented exception: it must use a genuine `async`/`await` body because catching an exception thrown during an awaited delegate requires the `try`/`catch` to wrap the `await` itself.
- `ResultTry.Try`/`TryAsync` must never rethrow — every exception the supplied delegate throws is caught and converted to `Result<T>.Failure(...)`. An `AggregateException` must be `.Flatten()`-ed before building the `Error.Message` so every inner exception is represented, not just the outer aggregate's generic message.
- `ResultTry` must never add an `Exception` reference to `Error`'s equality-participating members — only the exception's type name and message (as plain strings) may flow into `Error.Message`.
- `ResultCombine.Combine` must evaluate **every** input `Result`/`Result<T>` — it must never short-circuit on the first failure. On any failure it returns a `ValidationResult`/`ValidationResult<IReadOnlyList<T>>` carrying **every** failing `Error`, not just the first.
- `ResultTry` and `ResultCombine` are additive static classes in `SharedKernel.Core` — they must never require a change to `Result<T>`, `Result`, `ValidationResult`, `ValidationResult<T>`, `IHasSuccessFlag`, `IResultOfT<T>`, or `IFailureFactory<TSelf>`.
- `AddValidatedOptions` must call `.ValidateOnStart()` — misconfigured apps must fail at startup, not at first access.
- `IFeatureManager` is the only permitted feature-flag interface in consuming services — never inject `Microsoft.FeatureManagement.IFeatureManager` directly. This includes the variant/allocation path (P-298/WO-049): `GetVariantAsync` must never leak a `Microsoft.FeatureManagement` type (e.g. `Variant`, `VariantAssignmentReason`) through `IFeatureManager`'s public surface — always the neutral `FeatureVariant` record.
- `IFeatureManager`'s variant surface (`GetVariantAsync`) is additive-only — the existing boolean `IsEnabledAsync` members and their behavior must never change as a side effect of adding variant support. `MicrosoftFeatureManagerAdapter` deliberately keeps discarding the caller-supplied `ct` on both `IsEnabledAsync` overloads even though the `IVariantFeatureManager` interface it is now built on technically accepts one where the previously-injected `IFeatureManager` did not — forwarding it would be an observable behavior change this rule forbids.
- `AddSharedKernelFeatureManagement(configuration)` must always pass its `configuration` parameter through to `Microsoft.FeatureManagement`'s own `AddFeatureManagement(...)` call **unmodified** — never `configuration.GetSection("FeatureManagement")` or any other pre-scoped subsection. Confirmed empirically (P-298/WO-049): pre-scoping silently makes the Microsoft Feature Management variant/allocation schema (`feature_management:feature_flags`) unreachable with no error or warning, because it lives under a different, unscoped root key that a pre-scoped `IConfiguration` can no longer see.
- Guard extensions return `Error?` — **null means the guard passed**, non-null means violation. Never use `Error.None` as the "passed" sentinel in guard returns; use actual `null` so callers can distinguish cleanly.
- `Guard.Throw.*` methods are thin wrappers: call the matching `Against.*` extension, throw `DomainException(error)` if the result is non-null, otherwise return. No independent logic.
- `IGuardClause` is a public marker interface with no members — `DefaultGuardClause` (the implementation) is `private sealed` to the `Guard` class. Callers must never reference `DefaultGuardClause` directly.
- `InvalidFormat` and `Email` guard extensions must use a **static cached `Regex`** (e.g., via `ConcurrentDictionary<string, Regex>` keyed by pattern) with a bounded `RegexOptions.Compiled` timeout — a new `Regex` instance must never be created per call.
- Collection guards (`Empty`, `MaxCount`, `MinCount`) must enumerate the `IEnumerable<T>` source **at most once** per call — use `Count()` or a single materialization pass.
- `GuardDescriptions` is `internal` — it is not part of the public API and must not be exposed to consumers.
- `InvalidSmartEnum<TEnum, TValue>` must use `SmartEnum<TEnum, TValue>.TryFromValue` — no reflection, no `Enumeration<T>` or parallel type.
- `SharedKernel.Cryptography` has **zero third-party NuGet dependencies** — pure BCL `System.Security.Cryptography` only; references only `SharedKernel.Primitives` (for `Result<T>`/`Error`) and `SharedKernel.Configuration` (for `AddValidatedOptions`).
- Never use `System.Random` or `Guid.NewGuid()` for any security-sensitive value (tokens, keys, nonces, salts) — always go through `ISecureRandomGenerator`, which is backed by `RandomNumberGenerator`.
- Never compare HMACs, signatures, or any secret-derived byte sequence with `==`, `Equals`, or `SequenceEqual` — always `CryptographicOperations.FixedTimeEquals` to avoid timing attacks.
- `IOneWayHasher` output must be self-describing (embed algorithm identity, iteration count, and salt in the stored string) so `CryptographyOptions.Pbkdf2Iterations` can be raised later without invalidating existing hashes. Never store salt and hash in separate columns requiring a schema migration to rotate.
- No one-way *secret* hashing via raw `SHA256`/`SHA512`/`MD5` anywhere in the platform — only through `IOneWayHasher`. This applies to passwords *and* any other one-way secret (API keys, recovery codes, security-question answers) — `IOneWayHasher` is the single sanctioned path for all of them, never a parallel hand-rolled KDF call per secret type. This rule governs **secret** hashing only (WO-049 clarification) — for non-secret content fingerprints (object-storage ETags/checksums, content-addressable dedup keys, cache-key derivation from a payload body), `IContentHasher` is the sanctioned path instead; see the next rule. `IOneWayHasher` and `IContentHasher` must never be conflated or merged into one contract — they serve deliberately opposite performance/security profiles (slow+salted+iterated vs. fast+single-pass).
- `IContentHasher` (P-296/WO-049) must never be used for passwords, API keys, recovery codes, or any other secret — it is a fast, non-salted, non-iterated digest for non-secret content fingerprinting only. Reaching for raw `SHA256.HashData(...)` directly anywhere else in the platform, instead of going through `IContentHasher`, is the same class of hand-rolled-cryptography violation this domain already prohibits for secrets.
- `IOneWayHasher` must never be renamed back to a domain-specific name (e.g., `IPasswordHasher`) and must never grow domain-specific parameter names (e.g., `password`) — it is a `01.Core` primitive shared across every one-way-secret use case, not an auth-domain type. (WO-034 corrected exactly this leak.)
- `ISymmetricEncryptionService` must use an AEAD cipher (AES-GCM) — never an unauthenticated mode (CBC/ECB without a separate MAC).
- `ISymmetricEncryptionService.Decrypt` must return `Result<byte[]>`, never throw `CryptographicException` directly — a tampered payload or unrecognized key is an expected failure mode for this contract, not a bug.
- Symmetric/asymmetric key material is never hardcoded, embedded in source, or read directly from `IConfiguration` inside `SharedKernel.Cryptography` itself — it is always resolved through `IEncryptionKeyProvider` (encryption) or a caller-supplied `keyId` (signing), both implemented by the consuming service.
- `SharedKernel.Cryptography` must remain usable by non-web/worker services with zero ASP.NET Core, JWT, or OIDC dependencies. `12.Security.Oidc` may depend on `SharedKernel.Cryptography` for token-signing primitives; the dependency never runs in the other direction.
- `SharedKernel.Compression` (P-297/WO-049) has **zero third-party NuGet dependencies** — pure BCL `System.IO.Compression` only; references only `SharedKernel.Primitives` (for `Result<T>`/`Error`) and `SharedKernel.Configuration` (for `AddValidatedOptions`), mirroring `SharedKernel.Cryptography`'s exact reference shape.
- `IPayloadCompressor.Decompress` must return `Result<byte[]>` / `Result` (stream overload), never throw a decompression-format exception directly — a corrupt or truncated compressed payload is an expected failure mode for this contract, mirroring `ISymmetricEncryptionService.Decrypt`'s existing failure-handling shape. Both `BrotliPayloadCompressor` and `GZipPayloadCompressor` must catch `InvalidDataException` **or** `InvalidOperationException` — `GZipStream` throws the former for corrupt input, but `BrotliStream`'s decoder throws the latter ("Decoder ran into invalid data"); catching only `InvalidDataException` (the design's original, unverified assumption) lets Brotli corruption escape uncaught. Verified empirically during P-297/WO-049 implementation, not assumed from prose.
- Neither `BrotliPayloadCompressor` nor `GZipPayloadCompressor` can be relied upon to detect a compressed stream that is missing only its *trailing* bytes (suffix truncation) as a decompression failure — this is a confirmed BCL characteristic (`GZipStream` does not validate its trailing CRC32/ISIZE footer on read; `BrotliStream` has no fixed magic-number header to validate at all), not a defect to fix in this package. Do not write a test or a caller expectation assuming suffix-truncated input always surfaces as a `Result` failure for either algorithm — prefix truncation (missing header bytes) is reliably caught for gzip only. A service needing guaranteed truncation detection must pair compression with a separate integrity check (`IContentHasher` or a known expected length).
- Compression must always happen **before** encryption when both are applied to the same payload, never the reverse — compressing already-encrypted/high-entropy ciphertext wastes CPU for no size benefit. This ordering rule must be stated in `IPayloadCompressor`'s XML docs, not just this brain.
- `SharedKernel.Compression` ships no `.Abstractions`/`.{Provider}` sibling-package split — a single package with a keyed-DI algorithm choice (`BrotliPayloadCompressor` unkeyed default + "Brotli"/"GZip"-keyed singletons), mirroring `SharedKernel.Cryptography`'s `IAsymmetricSignatureService` RSA/ECDSA keyed-singleton precedent rather than sibling `.Brotli`/`.GZip` packages — the algorithm set is small, closed, and purely-BCL, exactly the condition under which that precedent applies.
- No static mutable state anywhere in this domain.
- **(P-443/WO-067, SHIPPED)** `SharedKernel.Validation` must never be folded into `SharedKernel.Guards` — Guards' value is deliberate minimalism (a generic precondition/argument-guard surface with no topic-specific catalog); a whole country/format-algorithm catalog belongs in its own package. `Guard.Against.*` extension methods for format validators live in `SharedKernel.Validation` (extending `IGuardClause` from the referencing side, confirmed to work exactly as designed — a marker interface's extension methods can be authored from any referencing package with zero changes to the defining package), never inside `SharedKernel.Guards` itself.
- **(P-443/WO-067, SHIPPED)** `Guard.Throw.*` parity is intentionally never added for format-validator guards — `SharedKernel.Guards`' `Guard.Throw` nested class is hand-enumerated and hardcoded inside that package; adding to it requires modifying `SharedKernel.Guards` itself, out of `SharedKernel.Validation`'s reach and never requested by WO-067's acceptance criteria (functional `Against.*` path only).
- **(P-443/WO-067, SHIPPED)** `ValidationErrorCodes` is a package-local nested-static-class string-constant catalog living entirely inside `SharedKernel.Validation` — it must never be added as a new nested category under `SharedKernel.Primitives.ErrorCodes`. `ErrorCodes`'s own documented rule ("consuming packages may add local constants without forking the SharedKernel") already covers this; a whole country-algorithm error-code catalog must never bloat the platform's most-depended-upon primitives package.
- **(P-443/WO-067, SHIPPED)** `CardNetwork` is a plain `enum`, not a `SmartEnum<TEnum,TValue>` — BIN-range network detection carries no per-value behavior beyond the name, so the `SmartEnum` machinery would be pure ceremony here.
- **(P-443/WO-067, SHIPPED)** `IbanValidator`'s per-country length table and every shipped test vector (IBAN, PAN, TCKN) were verified against real published sources (canonical ISO/SWIFT/Wikipedia IBAN examples, Stripe's published test-card catalogue, the published TCKN checksum formula) rather than hand-constructed — a future validator added to this package should hold itself to the same bar rather than inventing a "valid" example to satisfy its own implementation.
- **(P-443/WO-067, SHIPPED)** `VatValidator` is a baseline format check only (2-letter prefix + 2-12 alphanumeric characters) — it performs no per-country checksum and must never be documented or extended to imply otherwise without a dedicated new phase.
- **(P-444/WO-067, SHIPPED)** Every `ValidationRuleBuilderExtensions.MustBeValid*()` rule must be built on FluentValidation's `Custom(...)` extension, never `Must(predicate).WithErrorCode(fixedCode)` — a rule-fixed error code cannot represent a validator whose `Validate` call can fail with more than one distinct `ValidationErrorCodes` constant (e.g. `IbanValidator`'s `InvalidFormat`/`InvalidCheckDigit`/`InvalidLength`). Any future rule added to this package must propagate `result.Error.Code`/`result.Error.Message` from the underlying `SharedKernel.Validation` call verbatim, never hardcode a single code.
- **(P-444/WO-067, SHIPPED)** `IRuleBuilder<T,TProperty>.Custom(...)` returns `IRuleBuilderOptionsConditions<T,TProperty>` in FluentValidation 11.x, not `IRuleBuilderOptions<T,TProperty>` — a future rule-builder extension in this package (or any other FluentValidation adapter in this platform) must use the correct return type or the build fails with CS0266.
- **(P-444/WO-067, SHIPPED)** `SharedKernel.Validation` itself must never gain a `FluentValidation` reference as a result of this package's existence — `SharedKernel.Validation.FluentValidation` is a one-way dependency onto it, never the reverse.
- **(P-446/WO-068, shipped, BREAKING)** `IEncryptionKeyProvider`'s two members are `GetCurrentKeyAsync`/`GetKeyAsync` (`ValueTask`-returning, `CancellationToken`-aware) — the prior synchronous members were **removed**, never kept as a parallel additive overload; a KMS-backed implementer must never be allowed to silently offer a thread-blocking synchronous path alongside the async one.
- **(P-446/WO-068, shipped)** `ISymmetricEncryptionService`'s synchronous `Encrypt`/`Decrypt`/`EncryptToString`/`DecryptToString` members are **retained**, never removed, and bridge to the async `IEncryptionKeyProvider` via `.GetAwaiter().GetResult()` — every one of their XML docs states IN CAPITALS that this blocks a real thread when the registered provider is genuinely network-bound, and directs hot-path/high-throughput callers to the `*Async` overloads instead. This is what keeps `06.Persistence`'s structurally-synchronous-only EF Core `ValueConverter` pipeline solvable in its own follow-on phase (P-448) rather than forcing every downstream domain (`02.Caching`, `06.Persistence`, `07.Messaging`, `15.Integration`, `17.Workflows`) into a simultaneous breaking cascade from this one phase.
- **(P-446/WO-068, shipped)** `IEnvelopeEncryptionProvider`/`EnvelopeDataKey` are additive and distinct from `IEncryptionKeyProvider` — a provider may implement both, but neither interface may be collapsed into the other. `EnvelopeDataKey.PlaintextKey` must never be persisted by any caller; only `.WrappedKey` is safe to persist.
- **(P-446/WO-068, shipped)** `CachedEncryptionKeyProvider` must never serve a cached key past its configured TTL bound under any condition, including a concurrent inner-provider failure during refresh — a failed refresh must propagate to every caller awaiting that single-flight resolution, never fall back to the expired cached value. It ships with no package-owned DI extension, consistent with the `IIdGenerator`/`SystemClock(TimeProvider)` no-extension precedent.
- **(P-446/WO-068, shipped)** Fail-closed on an unreachable KMS is structural, not a documented convention: `GetCurrentKeyAsync`/`GetKeyAsync`/`GenerateDataKeyAsync`/`UnwrapDataKeyAsync` must propagate a thrown exception on an unreachable provider — there must be no code path that silently proceeds with a placeholder/no-op key.
- **(P-447/WO-068, shipped)** `SharedKernel.Cryptography.KeyVault.Azure`'s `AzureKeyVaultEncryptionKeyProvider` implements direct-retrieval mode (`IEncryptionKeyProvider`) internally on top of its own envelope-wrap mode (`IEnvelopeEncryptionProvider`) — never as two independent, divergent code paths. This is a deliberate design resolution to Azure Key Vault Keys not exporting raw HSM-protected key material by default; a future maintainer must not "fix" this into two separate paths. `CryptographicKey.Id`/`EnvelopeDataKey.MasterKeyId` are self-decodable length-prefixed Base64 envelopes (mirroring `AesGcmEncryptionService.Pack`'s existing binary style) so the provider needs no persistent store of its own.
- **(P-447/WO-068, shipped)** `SharedKernel.Cryptography.KeyVault.Azure` ships **zero caching of its own** beyond the single process-lifetime "current data key" slot needed to keep `CryptographicKey.Id` stable across repeated `GetCurrentKeyAsync` calls — it composes with `SharedKernel.Cryptography`'s `CachedEncryptionKeyProvider` (P-446) externally for bounded-TTL caching. Two independent caches with different TTL semantics must never both wrap the same provider instance.
- **(P-447/WO-068, shipped)** `Azure.Security.KeyVault.Keys` and `Azure.Identity` never leak as a transitive dependency of `SharedKernel.Cryptography` itself — confined to `SharedKernel.Cryptography.KeyVault.Azure`, verified via a direct `.nuspec` inspection in `SharedKernel.Consumer.Tests`, not merely asserted.
- **(P-451/WO-069, shipped)** `ITotpGenerator`/`IHotpGenerator`'s `ValidateCode` members must stay pure and stateless — no replay awareness. Replay protection is composed one layer up, in `TotpVerifier`, so the RFC implementation remains independently testable and the replay-guard orchestration remains an independently swappable concern.
- **(P-451/WO-069)** `ITotpReplayGuard` must live in the same package as `ITotpGenerator`/`TotpVerifier` — never split into a separate package. `TotpVerifier` needs the replay guard internally to reject a reused code; splitting it out would create a circular dependency between the two halves.
- **(P-451/WO-069)** `TotpGenerator`/`HotpGenerator` must source "now" from `IClock` — never `DateTime.UtcNow` — consistent with this domain's platform-wide `IClock`-only rule (SK0001).
- **(P-451/WO-069)** `RecoveryCodeGenerator` must never persist or hash the codes it generates — hashing at rest via the existing `IOneWayHasher` before persistence is the consuming service's responsibility, exactly like any other secret.
- **(P-474/WO-076, shipped)** `DataClassificationAttribute`/`SensitiveDataCategoryAttribute` must never be read via reflection in production code — they are pure compile-time/documentation metadata whose sole sanctioned consumer is `00.Governance`'s analyzer (P-476) and human documentation. A reflection-based runtime read of either attribute anywhere in production code is exactly the pattern this domain already prohibits for logging (root `CLAUDE.md`'s "never a reflection-based property walk" rule) and would directly contradict the rule these attributes exist to support.
- **(P-474/WO-076, shipped)** `PiiMasking.*` functions must be pure, allocation-minimal, deterministic, and null/empty-safe — never throw on null or empty input, never perform I/O, never carry hidden state. `Email`/`Phone`/`Pan` return `string.Empty` for null/whitespace input; `Suppress` alone returns its fixed sentinel for every input including null (see the public-surface block above for the exact masking rules chosen where the design left thresholds ambiguous).
- **(P-474/WO-076, shipped)** `IDataSubjectRequestHandler` must ship with no default or reflection-based implementation — each consuming service implements it against its own data. `SharedKernel.DataPrivacy` must never grow a cross-service erasure orchestrator; that composition, if it ever exists, belongs to a future `19.Scheduling`/`17.Workflows` phase, not this package.
- **(P-482/WO-078, shipped)** `ILocalizationCatalog.TryGetString` must never throw and must never return a blank/empty string on an unregistered or untranslated lookup — it returns `false` and a `null` out value; the caller owns the fallback-to-original-message behavior. `01.Core.Primitives.Error` must remain completely unchanged by this package — no new property, no breaking change.
- **(P-482/WO-078, shipped)** This package's own DI registration extensions (`AddInMemoryLocalizationCatalog`, `.AddStringLocalizerCatalog<TResource>()`) must never be named `AddSharedKernelLocalization()` — that name is reserved for `13.ServiceDefaults`'s separate culture-resolution middleware entry point (P-483). The two are genuinely distinct concerns (a message-lookup contract here vs. request-culture-resolution middleware there) and must never share one ambiguous name across domains. Enforced by a reflection-based test scanning every public static method this assembly declares.
- **(P-482/WO-078, shipped)** `InMemoryLocalizationCatalog`'s parent-culture-chain fallback (a lookup for `tr-TR` walks `tr` then `CultureInfo.InvariantCulture`) is specific to that implementation, never a requirement `ILocalizationCatalog` itself imposes — a different implementation is free to require an exact `(code, culture)` match only. Do not assume every `ILocalizationCatalog` performs fallback.
- **(P-482/WO-078, shipped)** `StringLocalizerLocalizationCatalog.TryGetString` must always check `LocalizedString.ResourceNotFound` before using `.Value` — never forward `.Value` unconditionally. On a miss, `IStringLocalizer` returns the requested key itself as `.Value`, so an unconditional forward would report a "found" translation that is really just the error code echoed back.

---

## DI Registration (expected shape)

```csharp
// IClock — needed by any service that reads time. Defaults to TimeProvider.System internally (P-295).
services.AddSingleton<IClock, SystemClock>();

// IClock with a caller-supplied TimeProvider — for services that want one shared, coordinated time
// source across IClock-consuming domain code and TimeProvider-consuming infrastructure (e.g. Polly v8).
services.AddSingleton<TimeProvider>(myTimeProvider);
services.AddSingleton<IClock>(sp => new SystemClock(sp.GetRequiredService<TimeProvider>()));

// IIdGenerator — opt-in, time-ordered (UUIDv7) identifier generation. Deliberately no package-owned
// AddIdGenerator() extension for this one abstraction (unlike IClock's AddClock(), see below); plain
// registration at the consumer's own composition root.
services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();

// Validated Options — per-options call, section comes from IConfiguration
services.AddValidatedOptions<MyServiceOptions>(configuration.GetSection("MyService"));

// Feature Management — IsEnabledAsync (boolean) and GetVariantAsync (weighted variant/gradual rollout,
// P-298) both resolve through the same IFeatureManager registration. `configuration` MUST be the app's
// root IConfiguration, never pre-scoped to "FeatureManagement" — a pre-scoped section silently hides the
// Microsoft Feature Management variant/allocation schema ("feature_management:feature_flags").
services.AddSharedKernelFeatureManagement(configuration);

// Cryptography — registers IOneWayHasher, ISymmetricEncryptionService, IAsymmetricSignatureService,
// IHmacSigner, ISecureRandomGenerator, and IContentHasher (P-296). The consuming service must separately
// register its own IEncryptionKeyProvider (and any signing key material) — this package ships no key material.
// IEncryptionKeyProvider is async as of P-446/WO-068 (breaking) — a synchronous/config-backed implementer
// migrates mechanically by returning an already-completed `new ValueTask<CryptographicKey>(...)`.
services.AddSharedKernelCryptography(configuration);
services.AddSingleton<IEncryptionKeyProvider, MyAsyncKeyVaultBackedKeyProvider>();

// Cryptography — bounded-TTL key caching (P-446/WO-068, shipped, additive). Config-supplied keys remain the
// default and need no caching; this is an explicit opt-in for a provider whose resolution is genuinely
// expensive (a real KMS call). No package-owned DI extension — plain composition, as shown here.
services.AddSingleton<IEncryptionKeyProvider>(sp =>
    new CachedEncryptionKeyProvider(new MyKmsBackedKeyProvider(...), TimeProvider.System, TimeSpan.FromMinutes(5)));

// Compression (P-297) — registers BrotliPayloadCompressor as both the unkeyed IPayloadCompressor default
// and the "Brotli"-keyed singleton, plus GZipPayloadCompressor as the "GZip"-keyed singleton only.
services.AddSharedKernelCompression(configuration);
var gzip = provider.GetRequiredKeyedService<IPayloadCompressor>(
    CompressionServiceCollectionExtensions.GZipPayloadCompressorKey);

// Validation (P-443, shipped) — registers INationalIdValidatorRegistry pre-seeded with TckNationalIdValidator
// ("TR"). Format validators themselves (IbanValidator, PanValidator, etc.) are static — no DI registration
// needed. Registration order between the two calls never matters — the registry singleton's factory pulls
// every DI-registered INationalIdValidator via sp.GetServices<INationalIdValidator>() when first constructed.
services.AddSharedKernelValidation()
    .AddNationalIdValidator<MySecondCountryNationalIdValidator>();

// Cryptography.KeyVault.Azure (P-447, shipped) — implements both IEncryptionKeyProvider and
// IEnvelopeEncryptionProvider (same singleton, two service-type registrations); wrap in
// CachedEncryptionKeyProvider (above) if caching is desired — this package ships none of its own.
services.AddSharedKernelAzureKeyVaultCryptography(configuration);

// Cryptography — TOTP/HOTP (P-451/WO-069, shipped, additive). AddSharedKernelCryptography (above) also
// registers IHotpGenerator, ITotpGenerator, and TotpVerifier as singletons. ITotpGenerator additionally
// requires IClock (register via services.AddClock() or the IClock lines above — this method does not
// register it), and TotpVerifier additionally requires the consumer's own ITotpReplayGuard (never
// registered by this package — supply your own: in-memory for dev, Redis-backed for production multi-replica).
services.AddClock();
services.AddSharedKernelCryptography(configuration);
services.AddSingleton<ITotpReplayGuard, MyRedisBackedTotpReplayGuard>();

// DataPrivacy (P-474, shipped) — no DI extension: DataClassificationAttribute/SensitiveDataCategoryAttribute
// are pure metadata (applied directly on types), and PiiMasking is a static class. IDataSubjectRequestHandler
// is registered by the consuming service against its own implementation, like any other application-owned
// contract — this package ships no default/reflection-based implementation of its own.
services.AddSingleton<IDataSubjectRequestHandler, MyServiceDataSubjectRequestHandler>();

// Localization (P-482, shipped) — exactly one of the two, never both against the same ILocalizationCatalog service type.
services.AddInMemoryLocalizationCatalog(catalog => catalog
    .AddTranslation("validation.iban.invalid_format", CultureInfo.GetCultureInfo("tr-TR"), "Geçersiz IBAN formatı"));
// — or —
services.AddStringLocalizerCatalog<MyResourceMarker>();
```

`SharedKernel.Core` and `SharedKernel.Guards` ship **no DI extensions** — they are pure libraries with no `Microsoft.Extensions.DependencyInjection.Abstractions` reference at all. `SharedKernel.Primitives` is not fully dependency-free of that package, however: it already carries one package-owned extension, `ClockExtensions.AddClock()` (registering `SystemClock` as `IClock`), which is why it references `Microsoft.Extensions.DependencyInjection.Abstractions` in the first place. `IIdGenerator` and `SystemClock`'s `TimeProvider` overload deliberately do **not** get an equivalent `AddX()` extension — both are registered with a plain `services.AddSingleton<...>()` call at the consumer's own composition root. This is a per-abstraction design choice (each new abstraction in this package is evaluated on its own merits for whether a convenience extension pulls its weight), not evidence that the package avoids the DI abstractions package altogether.

---

## AOT Compatibility

- `IHasSuccessFlag` and `IResultOfT<T>` are pure interface declarations — no reflection, no attributes, no generic constraints that require dynamic dispatch. AOT-safe by construction.
- `Result<T>` and `Result` implement `IHasSuccessFlag` via normal C# interface implementation — no dynamic casting, no runtime type lookup needed. Callers use `is IHasSuccessFlag` pattern matching, which is a static IL `isinst` instruction, fully AOT-compatible.
- `IResultOfT<T>` is used as a generic constraint (`where TResponse : IResultOfT<TResponse>`) in `05.Application` pipeline behaviors — generic constraints are resolved at JIT/AOT compile time, not at runtime via reflection.
- `IFailureFactory<TSelf>` uses a C# static abstract interface member (`static abstract TSelf Failure(Error error)`), resolved entirely at compile/JIT time via the `where TSelf : IFailureFactory<TSelf>` generic constraint — no `Type.MakeGenericType`, `Type.GetMethod`, `MethodBase.Invoke`, or any other `System.Reflection` call is needed to dispatch to it. This is the reflection-free replacement for `05.Application`'s prior `ResultOfTDispatcher<TResponse>.BuildFactory` reflection bridge (P-236, WO-039).
- `Result<T>`, `Result`, `Error`, `ValidationResult`, `ValidationResult<T>` are sealed classes/records — no reflection, fully AOT-safe.
- `ErrorCodes` is a static class of string constants — no runtime lookup, fully AOT-safe.
- `SmartEnum` base uses a static `IReadOnlyList<TEnum>` built at type-initialization — no reflection in value lookup.
- `LoggingEventIdRanges` is a static class of compile-time `const int` values — no reflection, no runtime computation, fully AOT-safe, and directly usable as a `[LoggerMessage(EventId = ...)]` attribute argument (which itself requires a compile-time constant expression). **(SK.01.LoggingRangesNewDomains, shipped)** three additional fields (`Idempotency = 18000`, `Scheduling = 19000`, `Reporting = 20000`) were added the same way — pure additive `const int`, no AOT-safety change.
- `WellKnownHeaders`, `WellKnownBaggageKeys`, and `WellKnownTagKeys` are static classes of compile-time `const string` values — no reflection, no runtime computation, fully AOT-safe; directly usable as header-name literals in `HttpRequestMessage.Headers`, gRPC `Metadata` entries, `Activity.AddBaggage(key, value)`, or `Activity.SetTag(key, value)` calls without any allocation beyond the string constant itself.
- `IIdGenerator`/`UuidV7IdGenerator` calls `Guid.CreateVersion7()` directly — a static BCL method, no reflection, fully AOT-safe.
- `SystemClock`'s `TimeProvider`-backed internals (P-295) call `TimeProvider.GetUtcNow()` directly — no reflection, fully AOT-safe; `TimeProvider` itself has shipped in the BCL since .NET 8 and requires no NuGet reference.
- `ResultTry` and `ResultCombine` are static classes — AOT-safe by default. `ResultTry.TryAsync`'s `async`/`await` body is ordinary compiler-generated async state-machine code, not a reflection-based construct, and remains fully AOT-safe.
- All railway extension methods are static — AOT-safe by default. Async overloads use `Task` continuation patterns to avoid AOT-hostile constructs.
- `Microsoft.Extensions.Options` is AOT-compatible as of .NET 8+ — verify on each upgrade.
- `Microsoft.FeatureManagement` — verify AOT status on each major upgrade, including the variant/allocation API surface `GetVariantAsync` (P-298) bridges; the `IFeatureManager` wrapper allows a swap if needed. Flag (do not block on) any AOT gap found in the variant API specifically. P-298 implementation confirmed no new gap: `IVariantFeatureManager` inherits the same pre-existing "no full AOT-trimming manifest" status as the rest of `Microsoft.FeatureManagement` 4.5.0, not a worse one.
- All BCL extension methods are static — AOT-safe by default.
- `IGuardClause` and all guard extension methods are static — AOT-safe. `DefaultGuardClause` is sealed, no virtual dispatch.
- `EqualityComparer<T>.Default` used in `Default<T>` guard is AOT-safe — it uses static dispatch via generic specialization in .NET 10.
- `InvalidFormat` / `Email` use `Regex` constructed with `RegexOptions.Compiled` in a static field — the compiled delegate is created once at type-initialization, which is AOT-compatible. `ConcurrentDictionary` is used only for pattern-keyed caching of caller-supplied patterns in `InvalidFormat`; the email regex is a fixed static field.
- `OutOfRange<T>` uses the `IComparable<T>` constraint — static generic dispatch, no boxing for value types, AOT-safe.
- `Pbkdf2OneWayHasher`, `AesGcmEncryptionService`, `RsaSignatureService`, `EcdsaSignatureService`, `HmacSha256Signer`, `CryptoRandomGenerator`, and `Sha256ContentHasher` (P-296) are sealed classes calling directly into BCL `System.Security.Cryptography` types (`Rfc2898DeriveBytes`, `AesGcm`, `RSA`, `ECDsa`, `HMACSHA256`, `RandomNumberGenerator`, `SHA256`) — no reflection, fully AOT-safe.
- `CryptographyOptions` binds via `Microsoft.Extensions.Options`, the same AOT-compatible (.NET 8+) path used by `SharedKernel.Configuration`.
- `BrotliPayloadCompressor` and `GZipPayloadCompressor` (P-297) are sealed classes calling directly into BCL `System.IO.Compression` types (`BrotliStream`, `GZipStream`) — no reflection, fully AOT-safe. `CompressionOptions` binds via the same `Microsoft.Extensions.Options` AOT-compatible path.
- **(P-443/WO-067, SHIPPED)** All `SharedKernel.Validation` static validators, `NationalIdValidatorRegistry` (`ConcurrentDictionary`-backed, no reflection), and `GuardValidationExtensions` are AOT-safe by construction — no reflection anywhere; the pluggable-registry lookup is a plain dictionary keyed by a `string` country code, not a type-based/reflective lookup.
- **(P-444/WO-067, SHIPPED)** `ValidationRuleBuilderExtensions` itself is AOT-safe by construction — no reflection, static generic methods only. `FluentValidation` 11.x's own AOT status is not independently verified by this package (third-party dependency, same pragmatic stance as `Microsoft.FeatureManagement`) — flag (do not block on) any AOT gap found there; it does not affect any other `01.Core` package since this is the domain's only consumer.
- **(P-444/WO-067, design-locked, implementation pending)** `ValidationRuleBuilderExtensions` are ordinary `IRuleBuilder<T,string>` extension methods — AOT-safety here is bounded by `FluentValidation`'s own AOT status, which must be verified on each version upgrade (mirrors the existing `Microsoft.FeatureManagement` verify-on-upgrade posture).
- **(P-446/WO-068, shipped)** The async `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider` members and `CachedEncryptionKeyProvider`'s single-flight-per-key logic (a `ConcurrentDictionary` compare-and-swap race plus a `Lazy<Task<T>>`) use ordinary `ValueTask`/`Task` continuation patterns — no reflection, fully AOT-safe. The sync-to-async bridge (`.GetAwaiter().GetResult()`) is a plain BCL call, AOT-safe but a runtime blocking concern (documented in Implementation Rules), not an AOT concern.
- **(P-447/WO-068, design-locked, implementation pending)** `AzureKeyVaultEncryptionKeyProvider`'s AOT status is bounded by the Azure SDK's (`Azure.Security.KeyVault.Keys`, `Azure.Identity`) own AOT compatibility — must be verified on each version upgrade, mirroring the `Microsoft.FeatureManagement` precedent; confined entirely to this one package, never propagating an AOT concern into `SharedKernel.Cryptography` itself.
- **(P-451/WO-069, shipped)** `Base32`, `HotpGenerator`, `TotpGenerator`, `TotpProvisioningUri`, and `RecoveryCodeGenerator` call directly into BCL `System.Security.Cryptography` types (`HMACSHA1`/`HMACSHA256`/`HMACSHA512`) and plain string/byte manipulation — no reflection, fully AOT-safe. `TotpVerifier`'s composition of `ITotpGenerator` + `ITotpReplayGuard` is ordinary interface dispatch, AOT-safe.
- **(P-474/WO-076, design-locked, implementation pending)** `DataClassificationAttribute`/`SensitiveDataCategoryAttribute` are plain `Attribute` subclasses — attribute *application* is always AOT-safe; the hard constraint (Implementation Rules) is that this domain never reads them back via reflection at runtime, which would be the actual AOT/trimming hazard. `PiiMasking.*` are pure static string functions — AOT-safe by default.
- **(P-482/WO-078, shipped)** `InMemoryLocalizationCatalog` is a plain dictionary with a `CultureInfo.Parent`-walking loop — no reflection, AOT-safe. `StringLocalizerLocalizationCatalog`'s AOT status is bounded by `Microsoft.Extensions.Localization.Abstractions`'s own AOT compatibility (AOT-compatible as of .NET 8+, same family as `Microsoft.Extensions.Options` — verify on each upgrade); its ambient `CultureInfo.CurrentUICulture` swap is a plain property set/restore, no reflection.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Primitives.Tests/` — Result, Error, IClock, SmartEnum, IHasSuccessFlag, IResultOfT\<T\>, IFailureFactory\<TSelf\> (generic-constraint dispatch producing a correct failure `Result<T>` for at least two distinct closed shapes, e.g. `Result<int>`/`Result<string>`, plus confirmation that `Result` (non-generic) is not assignable to `IFailureFactory<Result>`), `LoggingEventIdRanges` (all 18 domain base constants are pairwise unique, each is a multiple of 1000, and each equals exactly `{domain-folder-number} * 1000` matching the root CLAUDE.md folder map 00 through 17), `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys` (every constant's literal value is pinned exactly — `CorrelationId` header = `"X-Correlation-Id"`, `TenantId` header = `"X-Tenant-Id"`, `CorrelationId` baggage key = `"correlation.id"`, and every `WellKnownTagKeys` field — so a future edit cannot silently drift a cross-service propagation identifier), `IIdGenerator`/`UuidV7IdGenerator` (uniqueness across a large generation batch; values whose embedded millisecond timestamps differ compare as non-decreasing under the default `Guid` comparer — values sharing the same millisecond carry no such guarantee against each other and must not be asserted as strictly ordered), `SystemClock`'s `TimeProvider`-backed internals (parameterless `SystemClock()` reflects real time; `SystemClock(fakeTimeProvider)` reflects the injected provider's current instant, including after the fake advances time; `Today` derives correctly from the same instant)
- `SharedKernel.Core.Tests/` — exceptions, railway extensions, BCL extensions, `ResultTry`/`ResultTry.TryAsync` (delegate success path, thrown-exception-to-`Error.Unexpected` translation including a nested/flattened `AggregateException` case, custom exception-mapper overload, never-rethrows guarantee), `ResultCombine` (all-success non-generic and generic variants, single-failure and all-failure variants verifying every collected `Error` surfaces — not just the first — for both the non-generic `Result` and generic `Result<T>` overloads)
- `SharedKernel.Guards.Tests/` — guard functional path (Against.*), guard throw path (Throw.*), boundary theories
- `SharedKernel.Configuration.Tests/` — ValidatedOptions eager validation
- `SharedKernel.FeatureManagement.Tests/` — IFeatureManager enable/disable, context variant, `GetVariantAsync` deterministic variant assignment given a fixed context/seed, predictable fallback for an unconfigured feature (`FeatureVariant.Unassigned`), a regression check that the existing boolean `IsEnabledAsync` surface is unchanged, and a reflection-based test asserting `IFeatureManager`'s public surface never exposes a `Microsoft.FeatureManagement` type
- `SharedKernel.Cryptography.Tests/` — `IOneWayHasher` hash/verify roundtrip and rehash-needed detection across iteration-count changes, covering at least one password-shaped secret and one non-password-shaped secret (e.g., an API key string) to prove the contract is genuinely secret-agnostic; `ISymmetricEncryptionService` encrypt/decrypt roundtrip, tamper detection (flipped ciphertext/tag byte must fail `Decrypt`), and unknown/retired `KeyId` handling; `IAsymmetricSignatureService` sign/verify roundtrip for both RSA and ECDSA with wrong-key and tampered-data failure cases; `IHmacSigner` sign/verify roundtrip and tamper detection; `ISecureRandomGenerator` output length and non-repetition across calls; `IContentHasher` deterministic digest for identical input, differing digest for a single-byte change, streaming (`Stream`/async) vs. in-memory (`byte[]`) overloads producing identical output, and hex/Base64 encoding correctness via `ContentHasherExtensions`; DI registration sanity for `AddSharedKernelCryptography` (now covering six registered services)
- `SharedKernel.Compression.Tests/` — roundtrip for both `BrotliPayloadCompressor` and `GZipPayloadCompressor` (byte[] and stream overloads, sync and async); bit-level corruption and unrecognized/garbage input must surface as a `Result`/`Result<byte[]>` failure and never an unhandled exception for both algorithms — this is reliably true and must be asserted as a shared contract test; truncation detection is **not** reliably true for both algorithms (see Implementation Rules) and must be asserted per-algorithm instead — gzip: prefix-truncation surfaces as failure via its magic number; Brotli: assert only that no exception propagates, never that `IsFailure` is `true`; streaming vs. in-memory overloads produce equivalent decompressed output; DI registration sanity for `AddSharedKernelCompression` (unkeyed Brotli default + both keyed singletons resolve, invalid `CompressionOptions.Level` throws at `IHost.StartAsync()`)
- Railway-extension chains must be covered: map → bind → match over both success and failure paths.
- `SmartEnum` must cover: FromValue hit, FromValue miss (throws), TryFromValue, List completeness.
- Validated options test must assert that a misconfigured `TOptions` throws at `IHost.StartAsync()`.
- Guard tests must cover **both paths independently**: functional `Against.*` (assert returned `Error?`) and throw `Throw.*` (assert `DomainException` thrown on violation, no exception on pass).
- Numeric and string-length guard tests must use `[Theory]` with `[InlineData]` for boundary conditions (exactly at limit, one below, one above).
- Collection guard tests must verify single enumeration — use a counting stub/wrapper `IEnumerable<T>` that increments a counter on `GetEnumerator()` calls.
- **(P-443/WO-067, SHIPPED)** `SharedKernel.Validation.Tests/` (129/129 passing) — every format validator's valid/invalid cases including boundary theories, verified against real published test vectors rather than invented ones (canonical ISO/SWIFT/Wikipedia IBAN examples across GB/DE/FR/CH/TR/NL — 5 distinct lengths; Stripe's published test PANs, independently re-verified against Luhn by hand, covering Visa/Mastercard/Amex/Discover network detection; ISO 4217/3166 known-good and unknown-code cases; E.164 valid/invalid; VAT baseline); `NationalIdValidatorRegistry` (`TckNationalIdValidator` resolves for `"TR"` with correct checksum pass/fail against a vector independently re-derived from the published TCKN formula; an unregistered country returns `false`, never throws; a consumer-registered second country resolves after `.AddNationalIdValidator<TValidator>()`, in either registration order); `Guard.Against.*` validation extensions (`null` on pass, matching `ValidationErrorCodes` constant on fail); DI registration sanity for `AddSharedKernelValidation()`; README-sample compile-verification tests (the exact code shown in `01.Core/README.md`'s and the package's own `README.md`'s usage sections). `SharedKernel.Consumer.Tests` gained 4 tests proving the packed NuGet package resolves through the real dependency graph (54/54 passing).
- **(P-444/WO-067, SHIPPED)** `SharedKernel.Validation.FluentValidation.Tests/` (26/26 passing) — each `.MustBeValid*()` rule's valid/invalid path; explicit multi-code parity assertions for `MustBeValidIban` (format vs. length vs. check-digit each produce their own distinct `ValidationErrorCodes` constant, cross-checked against the standalone `IbanValidator.Validate` call); null-argument guards on `MustBeValidNationalId`; a locally-written harness reproducing `05.Application.Behaviors.Validation.ValidationBehavior`'s exact aggregation shape (read from its real source, not imported — `05.Application.Behaviors`/`16.Testing` stay out of this package's dependency graph) proving zero-extra-plumbing interop; README-sample compile-verification tests. `SharedKernel.Consumer.Tests` gained 2 tests proving the packed NuGet package resolves through the real dependency graph including the third-party `FluentValidation` package (56/56 passing, up from 54/54).
- **(P-446/WO-068, shipped)** `SharedKernel.Cryptography.Tests/` (101/101 passing, up from 73) covers: sync `Encrypt`/`Decrypt` bridging proven genuinely non-blocking under a synchronously-completing `IEncryptionKeyProvider` via a thread-pool-starvation regression guard (min worker threads constrained to 1, high concurrency, tight timeout — a true "sync-over-async became genuinely blocking" detector, not a round-trip-only assertion) and correctness-identical to the pre-migration sync-provider behavior; `*Async` overloads verified against `Decrypt(payload) == DecryptAsync(payload)` byte-for-byte on the same `EncryptedPayload` (ciphertext itself is never byte-compared across calls — a fresh random nonce makes that meaningless); `CachedEncryptionKeyProvider` — a cache hit never calls the inner provider, an expired entry always re-fetches, **a tested proof that a revoked/rotated key is never served past its configured TTL bound**, a concurrent single-flight refresh calls the inner provider exactly once for N simultaneous callers past expiry (proven via real `Task.Run` callers held open on a gate, not sequential awaits), an inner-provider failure during refresh propagates to every waiting caller rather than falling back to a stale value; `IEnvelopeEncryptionProvider` round-trip via a test double; full regression of every pre-existing `SharedKernel.Cryptography.Tests` case against the migrated async contract. Both concurrency-sensitive test classes re-run 3× to confirm no flakiness before being accepted.
- **(P-447/WO-068, design-locked, implementation pending)** `SharedKernel.Cryptography.KeyVault.Azure.Tests/` — integration-style (Azure Key Vault emulator or a skip-if-unavailable-gated dev-tenant vault, mirroring this platform's existing external-dependency test posture): `GenerateDataKeyAsync`→`UnwrapDataKeyAsync` round-trip, `GetCurrentKeyAsync`/`GetKeyAsync` direct-retrieval round-trip built atop the same envelope path, an unreachable vault/permission-denied identity surfacing as a thrown exception.
- **(P-451/WO-069, shipped)** `SharedKernel.Cryptography.Tests/Totp/` covers: `Base32` round-trip against RFC 4648 §10's published vectors; `HotpGenerator` against RFC 4226 Appendix D's published test vectors (all 10 counters); `TotpGenerator` against RFC 6238 Appendix B's published test vectors (SHA-1/256/512 at all 6 documented timestamps, fetched verbatim from the RFC text rather than transcribed from memory — every vector matched the implementation exactly on the first run); clock-drift-window accept/reject via a `FakeTimeProvider`-backed `IClock` (never real wall-clock sleeping); `TotpProvisioningUri.Build` output matching the Key Uri Format field-for-field; **`TotpVerifier` — a test submitting the same valid code twice, proving via NSubstitute interaction verification that the second call is rejected specifically because `ITotpReplayGuard.HasBeenUsedAsync` reported it used** (the phase's headline acceptance criterion), plus proof that `MarkUsedAsync` is never called for an invalid code; `RecoveryCodeGenerator` output count/length correctness and statistical non-repetition; a dedicated `ReadmeSampleCompileTests.cs` compiling the README's enrollment/challenge/verification/recovery-code samples verbatim. 235/235 `SharedKernel.Cryptography.Tests` passing.
- **(P-474/WO-076, shipped)** `SharedKernel.DataPrivacy.Tests/` (56/56 passing) — `PiiMasking.*` deterministic output for known inputs (email local-part masking incl. 1-char/empty/no-`@`/multi-`@` edge cases, phone digit-count-dependent reveal windows with separator preservation, PAN fixed-last-4 incl. 19-digit and sub-4-digit inputs, `Suppress`'s fixed sentinel), null/empty-input never throws; a compiled-assembly `System.Reflection.Metadata`/`PEReader` scan of the production DLL's `TypeReference` table proving no reflection-invocation type is referenced (not a source grep — see the public-surface block above), plus a companion test proving the attribute-exclusion branch is actually exercised; attribute-application mechanics for `DataClassificationAttribute`/`SensitiveDataCategoryAttribute` (a test-only reflective read proving mechanics, never a production-code claim); confirmation `IDataSubjectRequestHandler` has no default implementation registered anywhere in this package. `SharedKernel.Consumer.Tests` gained 6 tests including a `.nuspec` dependency-count assertion proving zero third-party NuGet dependency (67/67 passing, up from 61/61).
- **(P-482/WO-078, shipped)** `SharedKernel.Localization.Tests/` (43/43 passing) — `InMemoryLocalizationCatalog` (registered pair resolves correctly, unregistered pair returns `false`/`null` and never throws/blanks, `AddTranslation` chaining, parent-culture-chain fallback down to `CultureInfo.InvariantCulture`, case-sensitive `code` lookup); `StringLocalizerLocalizationCatalog` (wraps an NSubstitute-doubled `IStringLocalizerFactory`, correctly resolves a found key, correctly signals `false` for `ResourceNotFound` despite `LocalizedString.Value` carrying the raw key, and proves the ambient `CultureInfo.CurrentUICulture` swap is set during the call and restored afterward — including on a thrown exception); DI registration sanity for both extensions, including a reflection-based scan over every public static method this assembly declares confirming none is named `AddSharedKernelLocalization`, with a companion non-vacuous-check test; a `ReadmeSampleCompileTests.cs` compiling every README code sample verbatim. `SharedKernel.Consumer.Tests` gained 5 tests including a `.nuspec` dependency-count assertion proving exactly two dependencies (72/72 passing, up from 67/67).
- **(SK.01.LoggingRangesNewDomains, shipped)** `LoggingEventIdRangesTests` extended so the pairwise-uniqueness/multiple-of-1000/folder-number-to-value theory cases cover all 21 domain base constants (00 through 20), with a dedicated `PreExistingEighteenDomainConstants_AreByteForByteUnchanged` fact hardcoding all 18 prior expected values independently of the shared theory table — a transposition between two existing constants would still pass the pairwise-uniqueness/modulo checks alone (both remain unique multiples of 1000), so only this independent hardcoding catches it. 146/146 `SharedKernel.Primitives.Tests` passing.

---

## Changelog

> Maintained by the core domain agent. One line per significant change.

- [2026-05-14] Domain brain initialized — packages, interfaces, rules, AOT notes
- [2026-05-14] P-001/P-002 applied — added ValidationResult pair, ErrorCodes static class, clarified Result<T> as sealed class vs Result readonly struct, added MapError + void Match on non-generic Result, added async state machine allocation rule
- [2026-05-14] P-003 applied — added SharedKernel.Guards package: IGuardClause marker, Guard.Against/Guard.Throw entry points, full guard extension surface (null/empty, string length, numeric, range, default, Guid, format, email, collection, boolean predicate, SmartEnum), GuardDescriptions internal class, AOT notes for cached Regex and EqualityComparer<T>.Default, updated test rules with boundary theory and single-enumeration requirements
- [2026-05-27] P-042 applied — added ErrorType.BusinessRule enum member (HTTP 422 / domain-invariant-violation semantics), Error.BusinessRule(string code, string message) factory method, ErrorCodes.Domain nested class with RuleViolated constant; all additive — no existing types changed
- [2026-06-26] WO-033 (P-205/P-206) applied — added sixth package `SharedKernel.Cryptography`: `IPasswordHasher`/`Pbkdf2PasswordHasher`, `ISymmetricEncryptionService`/`AesGcmEncryptionService` + `IEncryptionKeyProvider`/`CryptographicKey`/`EncryptedPayload`, `IAsymmetricSignatureService`/`RsaSignatureService`/`EcdsaSignatureService`, `IHmacSigner`/`HmacSha256Signer`, `ISecureRandomGenerator`/`CryptoRandomGenerator`, `CryptographyOptions`, `AddSharedKernelCryptography` DI extension — design locked and project scaffolded (csproj + test stub + slnx registration); zero third-party NuGet dependencies, references only `SharedKernel.Primitives` + `SharedKernel.Configuration`; implementation (Core/Tests/Docs/Published) remains pending (arch-lead)
- [2026-06-26] WO-033 (P-207/P-208/P-209) queued for execution — pure implementation pass against the contracts already locked above; no interface, type, or DI-shape changes. P-207 implements the six sealed classes + `AddSharedKernelCryptography` against `01.Core/state-map.md` tasks C-33→C-38; P-208 delivers full unit coverage (tamper/wrong-key/rehash/DI-startup-failure cases) against T-23→T-28; P-209 closes out XML docs, README examples, NuGet packaging, and consumer dependency-graph verification against DO-10→DO-11 + P-10→P-12. Tracked under new state-map phase key `SK.01.WO033Impl` (core-arch-planner)
- [2026-06-26] P-207 implemented — added `IAsymmetricKeyProvider` (mirrors `IEncryptionKeyProvider` for RSA/ECDSA signing key resolution; not registered by `AddSharedKernelCryptography`, consuming service supplies its own) and documented the keyed-singleton DI pattern resolving the two-implementations-one-interface conflict for `IAsymmetricSignatureService` (`RsaSignatureServiceKey`/`EcdsaSignatureServiceKey` constants); 51 new Cryptography unit tests passing, SK.01.Core now fully `●` (core-phase-implementer)
- [2026-06-26] WO-033 closed (P-208, P-209) — full Cryptography unit-test coverage (58/58) confirmed; XML docs verified complete with zero warnings under `GenerateDocumentationFile`; NuGet metadata added matching the Guards convention; packed to the local feed; consumer-verify extended with 5 tests proving the `Primitives`+`Configuration` transitive chain resolves end-to-end through `AddSharedKernelCryptography`. No new types, interfaces, or DI shapes introduced — docs/packaging-only closeout. All six `01.Core` packages now `Published` (core-phase-implementer)
- [2026-06-26] WO-034 (P-210) applied — locked the rename of the password-hashing surface to a secret-agnostic contract: `IPasswordHasher` → `IOneWayHasher`, `Pbkdf2PasswordHasher` → `Pbkdf2OneWayHasher`, `PasswordVerificationResult` → `HashVerificationResult`; `Hash(string password)`/`Verify(string hash, string password)` → `Hash(string secret)`/`Verify(string hash, string secret)`. Same PBKDF2-HMACSHA256 mechanism, self-describing output, and rehash-needed detection — naming and parameter-vocabulary change only. `AddSharedKernelCryptography` registration updated to the renamed interface/implementation, same singleton lifetime. New rule added: this contract must never regain domain-specific vocabulary. Package version bumps to `2.0.0` (breaking public interface rename) at P-213 closeout (arch-lead, WO-034)
- [2026-06-29] WO-034 closed (P-211, P-212, P-213) — mechanical rename executed across all production `.cs` files (0 warnings/0 errors build); `SharedKernel.Cryptography.Tests` updated to the renamed contract plus a new `Hash_ThenVerify_WithApiKeySecret_ReturnsSuccess` test proving genuine secret-agnostic generalization (59/59 passing); `SharedKernel.Consumer.Tests/ConsumerDependencyGraphTests.cs` updated to `IOneWayHasher`/`HashVerificationResult` (42/42 consumer tests passing, confirming the published `2.0.0` package's Primitives+Configuration transitive chain resolves); `01.Core/README.md` hashing section rewritten showing password and API-key usage side by side. No new types or DI shapes — pure execution of the P-210 design. `SharedKernel.Cryptography` re-packed and published to the local feed at `2.0.0` (core-phase-implementer)
- [2026-07-01] P-230 applied (WO-038) — added `IHasSuccessFlag` zero-member marker interface (implemented by `Result<T>` and `Result`) and `IResultOfT<T>` typed interface (implemented by `Result<T>` only, exposing `IsSuccess`/`IsFailure`/`Value`); both are AOT-clean by construction with no `[RequiresUnreferencedCode]` annotation; purpose: unblock `05.Application.Behaviors` from reflection-based outcome detection (`LoggingBehavior`) and `Expression`-compiled `FailureResponseFactory`; additive-only — no existing `Result<T>` or `Result` member signatures change; interface contracts, AOT notes, implementation rules, and test rules updated in this brain; 8 tasks added to `01.Core/state-map.md` under `SK.01.P230` (core-arch-planner, WO-038)
- [2026-07-03] P-236 applied (WO-039) — added `IFailureFactory<TSelf>` self-referential (CRTP) contract to `SharedKernel.Primitives`: a `static abstract TSelf Failure(Error error)` member reachable via `where TResponse : IFailureFactory<TResponse>`; `Result<T>` implements `IFailureFactory<Result<T>>` through its existing `Failure(Error)` static factory — no new member, no signature change; `Result` (non-generic) deliberately excluded, consistent with `IResultOfT<T>`'s exclusion. Purpose: `05.Application`'s `FailureResponseFactory`/`ResultOfTDispatcher<TResponse>` (P-232, WO-038) claimed reflection-elimination but the shipped code still calls `Type.GetInterfaces()`/`MakeGenericType()`/`GetMethod()`/`Invoke()` — invisible to governance's SK0012 rule (which matches only the literal `MakeGenericMethod` call) but exactly the shape SK0012 exists to eliminate; this phase gives `05.Application` (P-237) the primitive needed to genuinely close the gap. Additive only — `IHasSuccessFlag`, `IResultOfT<T>`, and every existing `Result<T>`/`Result` member signature unchanged. 5 tasks added to `01.Core/state-map.md` under `SK.01.P236` (core-arch-planner, WO-039)
- [2026-07-08] P-249 applied (WO-041) — added `LoggingEventIdRanges` to `SharedKernel.Primitives`: a compile-time `const int` registry reserving one 1000-wide `EventId` block per folder-map domain (00.Governance=0 through 17.Workflows=17000, each = `{domain number} * 1000`), plus `DomainRangeWidth`(1000)/`PackageSubBlockWidth`(100) documentation constants describing the per-package sub-block convention multi-package domains layer on top. Motivation: a live `EventId` collision (`Caching.Redis.Core` vs `Redis.PubSub`, both using 4001/4002) plus zero platform-wide `EventId` coordination mechanism; `01.Core` is the only domain reachable from every domain that logs today (02, 05, 07, 11, 13, 14, 15), making `SharedKernel.Primitives` the correct — and only architecturally legal — home for a cross-domain constant registry. Additive only, zero new NuGet dependencies, no existing `Result<T>`/`Error`/`SmartEnum` member changes. 4 tasks added to `01.Core/state-map.md` under `SK.01.P249` (core-arch-planner, WO-041)
- [2026-07-09] P-249 closed (WO-041) — `LoggingEventIdRanges` implemented in `SharedKernel.Primitives/Logging/LoggingEventIdRanges.cs` exactly matching the locked design (all 18 domain `const int` fields + `DomainRangeWidth`/`PackageSubBlockWidth`); `LoggingEventIdRangesTests` added (pairwise uniqueness, multiple-of-1000, folder-number-to-value theory across all 18 domains); `01.Core/README.md` gained a "LoggingEventIdRanges — EventId Registry" usage section. Pure execution against an already-locked contract — no interface, type, or DI-shape changes. 114/114 `SharedKernel.Primitives.Tests` passing; `SK.01.P249` now fully `●` (core-phase-implementer)
- [2026-07-14] P-259 applied (WO-042) — locked design for `WellKnownHeaders` (`CorrelationId`="X-Correlation-Id", `TenantId`="X-Tenant-Id") and `WellKnownBaggageKeys` (`CorrelationId`="correlation.id") in `SharedKernel.Primitives`: dependency-free `const string` registries mirroring the `LoggingEventIdRanges` precedent, promoting today's de facto propagation-identifier literals into a single authoritative source. Motivation: a confirmed live mismatch between `14.Presentation.CorrelationIdMiddleware` (writes baggage key `"correlation.id"`) and `13.ServiceDefaults.BaggageLogRecordProcessor`'s test suite (hardcodes `"CorrelationId"`) — flagged in WO-041 (DO-07) but left unfixed for lack of a shared source of truth — plus three independent redeclarations of the `"x-tenant-id"` header across `11.Communication.Rest.TenantIdDelegatingHandler`, `11.Communication.Grpc.TenantIdInterceptor`, and `13.ServiceDefaults.MultiTenancy.HeaderTenantResolutionStrategy`. `04.Contracts` was considered and rejected: `SharedKernel.Communication.Grpc` carries a hard governance rule (P-163) forbidding a `04.Contracts` reference, so `01.Core` is the only architecturally legal shared home. Pure promotion — zero behavioral change, zero new NuGet dependencies. Additive only; no existing `Result<T>`/`Error`/`IClock`/`SmartEnum`/guard/options/feature-flag/cryptography/`LoggingEventIdRanges` member or DI shape changes. 4 tasks added to `01.Core/state-map.md` under `SK.01.P259` (D-30 design locked now; C-43/T-34/DO-16 pending implementation) (core-arch-planner, WO-042)
- [2026-07-14] P-259 closed (WO-042) — `WellKnownHeaders`/`WellKnownBaggageKeys` implemented in `SharedKernel.Primitives/Propagation/WellKnownHeaders.cs` and `WellKnownBaggageKeys.cs` exactly matching the locked design; `WellKnownPropagationConstantsTests` added pinning all 3 literal values; `01.Core/README.md` gained a "Well-Known Propagation Constants" usage section. Pure execution against an already-locked contract — no interface, type, or DI-shape changes. 117/117 `SharedKernel.Primitives.Tests` passing; `SK.01.P259` now fully `●` (core-phase-implementer)
- [2026-07-27] P-292 processed (WO-049) — locked design for `ResultTry` (`Try`/`TryAsync` exception-boundary entry points, `SharedKernel.Core`) and `ResultCombine` (`Combine` multi-result aggregation into `ValidationResult`/`ValidationResult<IReadOnlyList<T>>`, `SharedKernel.Core`), plus a small additive `ErrorCodes.Unexpected.Default` constant (`SharedKernel.Primitives`). Both are new static classes — pure additive, zero change to `Result<T>`/`Result`/`ValidationResult`/`IHasSuccessFlag`/`IResultOfT<T>`/`IFailureFactory<TSelf>`. `ResultTry.TryAsync` is documented as the one sanctioned exception to the "avoid async/await when only awaiting the input" railway rule, since exception interception requires wrapping the `await` itself; `AggregateException` is flattened before message construction. `ResultCombine.Combine` never short-circuits — every input is evaluated and every failing `Error` surfaces, not just the first. 9 tasks added (D-31→D-33, C-44→C-46, T-35/T-36, DO-17) (core-arch-planner, WO-049)
- [2026-07-27] P-293 processed (WO-049) — locked design for `IIdGenerator`/`UuidV7IdGenerator` (`SharedKernel.Primitives`), a `Guid.CreateVersion7()`-backed, opt-in alternative to `Guid.NewGuid()` for Postgres-index-friendly, time-ordered identifiers. Ships with **no package-owned DI extension** — deliberately resolved via a plain `services.AddSingleton<IIdGenerator, UuidV7IdGenerator>()` call at the consumer's own composition root, mirroring `IClock`'s existing registration story, to preserve `SharedKernel.Primitives`' zero-NuGet-dependency and no-DI-extension rules (adding a package-owned `AddX()` method would require a `Microsoft.Extensions.DependencyInjection.Abstractions` reference this package must never take). Cross-referencing `03.Domain`'s `IAggregateFactory` guidance is explicitly out of `01.Core`'s jurisdiction — flagged for a `domain-arch-planner` follow-up, not tracked here. 4 tasks added (D-34, C-47, T-37, DO-18) (core-arch-planner, WO-049)
- [2026-07-27] P-294 processed (WO-049) — locked design for `WellKnownTagKeys` (`TenantId`="tenant.id", `CorrelationId`="correlation.id", `ErrorType`="error.type", `ErrorCode`="error.code"), a third dependency-free `const string` registry in `SharedKernel.Primitives/Propagation/` alongside `WellKnownHeaders`/`WellKnownBaggageKeys`, covering `Activity.SetTag(...)` call sites that 00.Governance's SK0022 analyzer already regulates with no registry yet to point at. Introduced pre-emptively — no existing domain's shipped `Activity.SetTag` call sites are touched by this phase; retrofit is each consuming domain's own follow-up, exactly as documented for the other two registries. Root `CLAUDE.md`'s Magic String convention section update is out of `01.Core`'s jurisdiction (arch-lead/`sync-brain`, mirroring how the root WO-042 section was itself added at the root level, not by this agent). 4 tasks added (D-35, C-48, T-38, DO-19) (core-arch-planner, WO-049)
- [2026-07-27] P-293 implemented — `IIdGenerator`/`UuidV7IdGenerator` added to `SharedKernel.Primitives/Identifiers/` exactly per the locked D-34 design; 125/125 `SharedKernel.Primitives.Tests` passing. Testing surfaced and corrected an inaccuracy in the P-293 design note above and elsewhere in this file: `Guid.CreateVersion7()` uses RFC 9562's "random" sub-method (not "monotonic random"), so a tight loop of 1000 successive values failed a strict non-decreasing `CompareTo`/`<` assertion — only values whose embedded millisecond timestamps actually differ are guaranteed non-decreasing; same-millisecond ties carry no ordering guarantee against each other (still fine for the Postgres locality argument, since real inserts span many milliseconds). Also corrected: this file's prior claim that `SharedKernel.Primitives` "never references `Microsoft.Extensions.DependencyInjection.Abstractions`" was already false before this phase — `ClockExtensions.AddClock()` has referenced it since the domain's first session; `IIdGenerator`'s no-extension shape is a deliberate per-abstraction choice, not evidence of a dependency-free package. Interface Contracts, DI Registration, and Test Rules sections updated to state both points precisely; `01.Core/README.md` gained an "IIdGenerator — Time-Ordered Identifiers" section (core-phase-implementer)
- [2026-07-27] P-295 processed (WO-049) — locked design for `SystemClock`'s internal `TimeProvider`-backed rewrite: `UtcNow`/`Today` now source from an injected `System.TimeProvider` (defaulting to `TimeProvider.System`) instead of calling `DateTimeOffset.UtcNow` directly; a new `SystemClock(TimeProvider)` constructor overload lets a consuming service share one coordinated time source across `IClock`-consuming domain code and `TimeProvider`-consuming infrastructure (e.g. Polly v8 resilience pipelines in `11.Communication`). `IClock`'s own public surface (`UtcNow`, `Today`) is completely unchanged, and SK0001 (`DirectDateTimeUsageAnalyzer`)'s "IClock is the only permitted time source" enforcement is unaffected — this is an internal `SystemClock` implementation detail, not a relaxation of that rule. Zero new NuGet dependency (`TimeProvider` has shipped in the BCL since .NET 8). No package-owned DI extension, consistent with the `IIdGenerator` precedent set this same session. `16.Testing`'s `FakeClock` gaining a matching `TimeProvider`-exposing update is flagged as a `testing-arch-planner` follow-up, out of `01.Core`'s jurisdiction. 4 tasks added (D-36, C-49, T-39, DO-20) (core-arch-planner, WO-049)
- [2026-07-27] P-296 processed (WO-049) — locked design for `IContentHasher`/`Sha256ContentHasher`/`ContentHasherExtensions` (`SharedKernel.Cryptography`), a fast, non-salted, non-iterated `SHA256.HashData`-backed digest contract for non-secret content-fingerprinting use cases (object-storage ETags/checksums, dedup keys, cache-key derivation) — the deliberate architectural opposite of the deliberately-slow `IOneWayHasher`. `AddSharedKernelCryptography` now registers six singletons instead of five. Amended the pre-existing "no raw SHA256/MD5 hashing" hard rule to clarify it governs **secret** hashing only and to cross-reference `IContentHasher` as the sanctioned non-secret path — the two contracts must never be conflated. 4 tasks added (D-37, C-50, T-40, DO-21) (core-arch-planner, WO-049)
- [2026-07-27] P-297 processed (WO-049) — locked design for a **seventh `01.Core` package**, `SharedKernel.Compression`: `IPayloadCompressor` (`Compress`/`Decompress`, byte[] and stream overloads, `Decompress` returns `Result<byte[]>`/`Result` rather than throwing on corrupt/truncated input — mirroring `ISymmetricEncryptionService.Decrypt`'s failure shape), `BrotliPayloadCompressor` (unkeyed default + "Brotli"-keyed singleton) and `GZipPayloadCompressor` ("GZip"-keyed singleton only), `CompressionOptions`, and `AddSharedKernelCompression`. No `.Abstractions`/`.{Provider}` sibling-package split — a single package with a keyed-DI algorithm choice, mirroring `SharedKernel.Cryptography`'s RSA/ECDSA keyed-singleton precedent exactly. References only `SharedKernel.Primitives` + `SharedKernel.Configuration`, zero third-party NuGet dependencies. XML docs must state the compress-then-encrypt ordering rule explicitly. Full package lifecycle tracked (Design/Scaffold/Core/Tests/Docs/Published) since this is a brand-new package, mirroring the WO-033 Cryptography precedent. Packages table, Technology Stack table, and Package Board updated to seven packages. 11 tasks added (D-38→D-40, S-19, C-51/C-52, T-41, DO-22, P-13→P-15) (core-arch-planner, WO-049)
- [2026-07-27] P-298 processed (WO-049) — locked design for weighted feature-flag variant/experimentation support on `IFeatureManager` (`SharedKernel.FeatureManagement`): `GetVariantAsync`/`GetVariantAsync<TContext>` returning a neutral `FeatureVariant` record (Name + optional Configuration payload), bridging `Microsoft.FeatureManagement`'s `IVariantFeatureManager` the same way the existing boolean path bridges plain evaluation, without leaking any `Microsoft.FeatureManagement` type through the public surface; `FeatureVariantDefinition` sibling record (Name/Weight/Configuration) added for the definitions API. Purely additive — existing boolean `IsEnabledAsync` surface and behavior unchanged. `AddSharedKernelFeatureManagement` wiring requires no additional configuration beyond `Microsoft.FeatureManagement`'s own variant/allocation schema. Any AOT gap found in the variant API surface is to be flagged, not treated as a blocker. 5 tasks added (D-41, C-53/C-54, T-42, DO-23) (core-arch-planner, WO-049)
- [2026-07-27] P-292 closed (WO-049) — `ResultTry`/`ResultCombine` implemented in `SharedKernel.Core/Extensions/ResultTry.cs` and `ResultCombine.cs`; `ErrorCodes.Unexpected.Default` changed from `"unexpected.default"` to `"unexpected.exception"` in `SharedKernel.Primitives`, exactly matching the design already locked above — verified no shipped `.cs` file elsewhere in the repo referenced the old literal, so no downstream break. Pure execution against an already-locked contract — no interface, type, or DI-shape changes; this brain's existing `ResultTry`/`ResultCombine`/`ErrorCodes.Unexpected.Default` descriptions, AOT notes, and test-rule entries needed zero correction. 118/118 `SharedKernel.Primitives.Tests` + 95/95 `SharedKernel.Core.Tests` passing; `SK.01.P292` now fully `●` (core-phase-implementer)
- [2026-07-27] P-294 closed (WO-049) — `WellKnownTagKeys` implemented in `SharedKernel.Primitives/Propagation/WellKnownTagKeys.cs` exactly matching the D-35 locked design above (no interface/type/DI-shape correction needed — the pre-written Interface Contracts entry already matched shipped reality). `WellKnownPropagationConstantsTests` extended with 4 new pinning tests (one per constant). `01.Core/README.md`'s "Well-Known Propagation Constants" section extended with a `WellKnownTagKeys` usage example and its heading/summary widened to name all three registries. 129/129 `SharedKernel.Primitives.Tests` passing, 0 regressions; `SK.01.P294` now fully `●` (core-phase-implementer)
- [2026-07-27] P-296 closed (WO-049) — `IContentHasher`/`Sha256ContentHasher`/`ContentHasherExtensions` implemented in `SharedKernel.Cryptography/Hashing/` exactly matching the D-37 locked design above (no interface/type/DI-shape correction needed — the pre-written Interface Contracts entry already matched shipped reality). `AddSharedKernelCryptography` now registers `IContentHasher` as its sixth singleton. `Sha256ContentHasherTests` added (determinism, single-byte-change divergence, `byte[]`/`Stream`/async-`Stream` parity, null-argument guards, cancellation, hex/Base64 encoding correctness, and a known-answer test against the well-known empty-input SHA-256 digest); two new DI sanity tests added to `CryptographyServiceCollectionExtensionsTests`. `01.Core/README.md` gained an "IContentHasher — Non-Secret Content Fingerprinting" section placed directly after "One-Way Hashing" for contrast with `IOneWayHasher`. 73/73 `SharedKernel.Cryptography.Tests` passing, 0 build warnings; `SK.01.P296` now fully `●` (core-phase-implementer)
- [2026-07-27] P-297 closed (WO-049) — seventh package `SharedKernel.Compression` shipped in full: `IPayloadCompressor`/`BrotliPayloadCompressor`/`GZipPayloadCompressor`/`CompressionOptions`/`AddSharedKernelCompression` implemented exactly per the locked D-38→D-40 design, with two empirically-verified corrections to that design's assumed BCL behavior (both now reflected in the Interface Contracts and Implementation Rules sections above): (1) `BrotliStream`'s decoder throws `InvalidOperationException`, not `InvalidDataException`, for corrupt input — both compressors now catch `InvalidDataException or InvalidOperationException`; (2) neither `BrotliStream` nor `GZipStream` reliably detects suffix-only truncation as an error (confirmed BCL characteristic — `GZipStream` never validates its trailing CRC32/ISIZE footer on read, `BrotliStream` has no magic-number header at all), so `SharedKernel.Compression.Tests` asserts truncation-detection per-algorithm rather than as one shared contract test. 46/46 `SharedKernel.Compression.Tests` + 46/46 `SharedKernel.Consumer.Tests` passing, 0 build warnings under `GenerateDocumentationFile`; packed to `./nupkgs` at `1.0.0`. `01.Core/README.md` gained a "SharedKernel.Compression" usage section including a "note on truncation detection". `SK.01.P297` now fully `●` (11/11) (core-phase-implementer)
- [2026-07-28] P-298 closed (WO-049) — `FeatureVariant` (with the named `Unassigned` deterministic-fallback sentinel), `FeatureVariantDefinition`, and `IFeatureManager.GetVariantAsync`/`GetVariantAsync<TContext>` implemented in `SharedKernel.FeatureManagement` exactly per the locked D-41 design, via a `MicrosoftFeatureManagerAdapter` rewrite onto `Microsoft.FeatureManagement.IVariantFeatureManager` — confirmed by reflection against the real `4.5.0` assembly to be a strict superset of the previously-injected `Microsoft.FeatureManagement.IFeatureManager`, carrying both the boolean and variant members, so one injected dependency now serves the whole adapter with no additional DI registration needed (Microsoft's own `AddFeatureManagement(...)` already registers its concrete `FeatureManager` against both interfaces). Found and fixed a real, previously-shipped defect during implementation, confirmed via a throwaway console harness against the real package rather than assumed from prose: `AddSharedKernelFeatureManagement` was passing `configuration.GetSection("FeatureManagement")` into `Microsoft.FeatureManagement`'s own `AddFeatureManagement`, which silently made the variant/allocation configuration schema (`feature_management:feature_flags`, a *different*, unscoped, snake_case root key per Microsoft's own schema) completely unreachable — plain boolean flags resolved fine regardless, which is exactly why this went unnoticed. Fixed by passing the root `IConfiguration` instead; zero consumer-visible signature change, fully backward compatible. Also confirmed empirically: `GetVariantAsync<TContext>` has no generic per-`TContext` contextual-filter equivalent in `Microsoft.FeatureManagement` — the variant API is fixed to a concrete `ITargetingContext` (UserId+Groups); `MicrosoftFeatureManagerAdapter` bridges this via `context?.ToString()`, documented explicitly. The two pre-existing `IsEnabledAsync` members deliberately keep discarding `ct` exactly as before, even though the newly-injected interface now technically accepts one, to guarantee byte-for-byte-unchanged behavior per this phase's hard rule. Interface Contracts, Implementation Rules, DI Registration, AOT Compatibility, and Test Rules sections all updated with these findings. 29/29 `SharedKernel.FeatureManagement.Tests` passing (9 pre-existing + 20 new); `SK.01.P298` now fully `●` (5/5) — every WO-049 phase inside `01.Core`'s own jurisdiction (P-292→P-298) is complete (core-phase-implementer)
- [2026-08-14] P-384 processed (WO-059) — locked design for `ErrorType.Forbidden` (next sequential enum value after `BusinessRule = 6`) and `Error.Forbidden(string code, string message)` on `SharedKernel.Primitives`'s `Error`/`ErrorType`, mirroring `BusinessRule`'s exact shape and XML-doc style (P-042/WO-010 precedent). Motivation: two already-dispatched, already-designed WO-058 phases — `05.Application`'s `DualApprovalBehavior` maker-checker short-circuit (P-380, blocking `SK.05.Core` at 77/81 with C-78 `⚑` Blocked) and `14.Presentation`'s `[RequireRole]`/`[RequirePermission]` endpoint attributes (P-381, whose own C-24 already carries the companion HTTP 403 status-code mapping in its own scope) — each independently found this gap by reading shipped source and correctly declined to substitute `Error.Unauthorized(...)`: `Unauthorized` means not-permitted-to-attempt-at-all (HTTP 401), while both consumers need permitted-in-general-but-this-instance-not-satisfied (HTTP 403), a materially different meaning. No `ErrorCodes` companion constant added, unlike `BusinessRule`'s `ErrorCodes.Domain.RuleViolated` — no consuming phase asked for a shared code constant, so none is speculatively added. Purely additive — no existing `ErrorType` member's numeric value changes, no existing `Error` factory's signature changes. Release-notes risk flagged explicitly for the repack: a downstream consumer with an exhaustive `switch`/`switch` expression over `ErrorType` carrying no `discard`/`default` arm will need a source-level update to keep compiling (compile-time signal, not a runtime break) once this ships. Interface Contracts (`Error`/`ErrorType`) updated in this pass. 10 tasks added (D-42/D-43, C-55/C-56, T-43→T-46, DO-24, P-16); D-42/D-43 marked `●` (design locked now), the rest `○` pending a future implementation pass, mirroring the P-292→P-298 (WO-049) precedent (core-arch-planner, WO-059)
- [2026-08-26] Seven phases across four work orders (WO-067/068/069/076/078) plus one cross-cutting housekeeping item processed in a single pass, all design-locked, all implementation pending a future pass — mirroring the P-292→P-298/P-384 "lock design ahead of implementation" precedent throughout. Domain summary paragraph, Packages table, and Technology Stack table updated to reflect twelve total packages (seven published, five design-locked): **P-443** locks a new eighth package, `SharedKernel.Validation` (references `SharedKernel.Primitives` + `SharedKernel.Guards`) — IBAN/BIC/PAN/ISO 4217/ISO 3166/E.164/VAT format validators, a pluggable per-country `INationalIdValidatorRegistry` (TCKN default), and `Guard.Against.*` extensions (functional path only — `Guard.Throw.*` parity declined since that nested class is hardcoded inside `SharedKernel.Guards` and cannot be extended from an outside package); `ValidationErrorCodes` is a new package-local constants class, deliberately never added to `SharedKernel.Primitives.ErrorCodes`. **P-444** locks a new ninth package, `SharedKernel.Validation.FluentValidation` (depends on P-443) — a thin `IRuleBuilder<T,string>` rule adapter, keeping `FluentValidation` out of `SharedKernel.Validation` itself. **P-446** locks a **breaking** change to `IEncryptionKeyProvider` (`GetCurrentKey()`/`GetKey(string)` → `GetCurrentKeyAsync`/`GetKeyAsync`, sync members removed outright) plus additive `ISymmetricEncryptionService.*Async` overloads (existing sync members retained, bridging via `.GetAwaiter().GetResult()`, documented IN CAPITALS as thread-blocking for a genuinely network-bound provider), a new additive `IEnvelopeEncryptionProvider`/`EnvelopeDataKey` contract, and a new additive `CachedEncryptionKeyProvider` bounded-TTL decorator — a deliberate design choice that keeps `06.Persistence`'s structurally-synchronous EF Core `ValueConverter` pipeline problem solvable in that domain's own follow-on phase (P-448, out of jurisdiction) rather than forcing a platform-wide breaking cascade across five downstream domains from this one phase. **P-447** locks a new tenth package, `SharedKernel.Cryptography.KeyVault.Azure` (depends on P-446) — `AzureKeyVaultEncryptionKeyProvider` implementing both `IEncryptionKeyProvider` and `IEnvelopeEncryptionProvider`, with direct-retrieval mode deliberately built internally atop the envelope-wrap mode (Azure Key Vault Keys does not export raw HSM-protected key material by default) — a real design tension resolved explicitly rather than left implicit. **P-451** locks an additive extension to the existing `SharedKernel.Cryptography` package (no new package) — RFC 6238/4226 TOTP/HOTP generation/verification (`Base32`, `IHotpGenerator`, `ITotpGenerator`, `TotpProvisioningUri`), a pluggable `ITotpReplayGuard` composed by `TotpVerifier` (the type that actually prevents double-acceptance of one code — `ValidateCode` itself stays pure/stateless), and `RecoveryCodeGenerator`; the replay guard must live in this same package since `TotpVerifier` needs it internally, never split out. **P-474** locks a new eleventh package, `SharedKernel.DataPrivacy` (references `SharedKernel.Primitives` only) — `DataClassification`/`SensitiveDataCategory` pure-metadata marker attributes (never read via reflection in production — sole sanctioned consumer is `00.Governance`'s P-476 analyzer), `PiiMasking.*` pure helpers, and `IDataSubjectRequestHandler` (no default implementation; cross-service erasure orchestration explicitly out of scope). **P-482** locks a new twelfth package, `SharedKernel.Localization` (references `SharedKernel.Primitives` + `Microsoft.Extensions.Localization.Abstractions`) — `ILocalizationCatalog.TryGetString(code, culture, out value)` keyed on `Error`'s existing `code` string, never throwing/blanking on a miss; `01.Core.Primitives.Error` itself is completely unchanged (the load-bearing design decision — localization happens entirely at `14.Presentation`'s boundary, P-484, out of jurisdiction); this package's own DI extensions are deliberately never named `AddSharedKernelLocalization()`, reserving that name for `13.ServiceDefaults`'s separate middleware (P-483). **P-485** (cross-cutting, no work order, direct arch-lead directive) locks three additive `LoggingEventIdRanges` domain-base constants — `Idempotency = 18000`, `Scheduling = 19000`, `Reporting = 20000` — unblocking the three capability domains ratified this session from authoring their first `[LoggerMessage]` method; zero breaking impact. Interface Contracts, Implementation Rules, DI Registration, AOT Compatibility, and Test Rules sections all updated across every phase above. 88 tasks added across 8 new phase keys (`SK.01.P443`/`P444`/`P446`/`P447`/`P451`/`P474`/`P482`/`P485`); every phase's `D-*` design tasks marked `●` this pass, all `S-*`/`C-*`/`T-*`/`DO-*`/`P-*` tasks `○` pending a future implementation pass (core-arch-planner, WO-067/WO-068/WO-069/WO-076/WO-078)
- [2026-09-02] P-446 implemented and closed (WO-068) — `IEncryptionKeyProvider`'s `GetCurrentKey()`/`GetKey(string)` removed outright and replaced by `GetCurrentKeyAsync`/`GetKeyAsync` (breaking); `ISymmetricEncryptionService` gained additive `EncryptAsync`/`DecryptAsync`/`EncryptToStringAsync`/`DecryptToStringAsync` with the sync members retained (bridging via `.GetAwaiter().GetResult()`, IN CAPITALS blocking warnings) and both call shapes sharing one pure `EncryptCore`/`DecryptCore` pair inside `AesGcmEncryptionService`; new additive `IEnvelopeEncryptionProvider`/`EnvelopeDataKey` (contract only, no default implementation, mirrors `IEncryptionKeyProvider`'s "consumer implements" shape) and `CachedEncryptionKeyProvider` (bounded-TTL, `ConcurrentDictionary`-CAS + `Lazy<Task<T>>` single-flight-per-key refresh, fail-closed on refresh failure, no DI extension). Interface Contracts, Implementation Rules, DI Registration, AOT Compatibility, and Test Rules sections all updated from "design-locked, implementation pending" to shipped state. `SharedKernel.Cryptography.Tests` 101/101 green (28 new tests, incl. a genuine-concurrency single-flight proof using real `Task.Run` callers held on a gate — not sequential awaits — and a thread-pool-starvation regression guard for the sync bridge, both re-run 3× to confirm no flakiness). `01.Core/README.md`'s Symmetric Encryption section rewritten with a breaking-change migration table plus new Envelope Encryption and `CachedEncryptionKeyProvider` sections — every code sample compile-verified in a scratch project, which also surfaced and fixed one pre-existing latent `Result<T>.Match` doc bug in the block being edited (unrelated to this phase's own contract). No `<Version>` added to any `.csproj` (post-2026-08-25 MinVer convention preserved) — `SharedKernel.Cryptography.csproj`'s `Description`/`PackageTags` updated with the breaking-change migration note instead, and the 7-package `01.Core` dependency chain was packed to the local `nupkgs` feed and `SharedKernel.Consumer.Tests` re-verified 50/50 green (4 new tests: async round-trip, `CachedEncryptionKeyProvider` composition, `IEnvelopeEncryptionProvider` contract) — the MAJOR version bump itself is a `git tag` decision left to `devops-lead`. `SK.01.P446` now fully `●` (13/13); root Phase Backlog P-446 closed. Downstream breakage in `06.Persistence.EfCore`, `15.Integration.Webhooks`, `16.Testing`, `17.Workflows.Temporal`, `00.Governance`, and `02.Caching.FusionCache.Tests` is expected and intentionally out of this phase's jurisdiction — each awaits its own already-dispatched follow-on phase (P-448/P-450/etc.) (core-phase-implementer)
- [2026-09-02] P-443 implemented and closed (WO-067) — `SharedKernel.Validation` shipped as the eighth published package: 7 static dual-mode validators, pluggable `INationalIdValidatorRegistry` (TCKN default), `GuardValidationExtensions`, `AddSharedKernelValidation()`. Packages/Technology Stack/Interface Contracts/Implementation Rules/DI Registration/AOT/Test Rules sections updated from design-locked to shipped; package count now 8 published/4 design-locked. 129/129 `SharedKernel.Validation.Tests` passing (real published test vectors — IBAN/PAN/TCKN — verified by hand, not invented); `SharedKernel.Consumer.Tests` 54/54 passing through the packed dependency graph. `SK.01.P443` fully `●`; root Phase Backlog P-443 closed (core-phase-implementer)
- [2026-09-02] P-444 implemented and closed (WO-067) — `SharedKernel.Validation.FluentValidation` shipped as the ninth published package (this domain's only third-party-dependency package): `ValidationRuleBuilderExtensions` built on FluentValidation's `Custom(...)` (never `Must`+`.WithErrorCode()`) so `ValidationFailure.ErrorCode` always carries the exact `ValidationErrorCodes` constant produced by the underlying validator. Corrected two assumptions in the design narrative during implementation: the real `.Custom(...)` return type is `IRuleBuilderOptionsConditions<T,TProperty>`, not `IRuleBuilder`/`IRuleBuilderOptions`; and `05.Application.Behaviors.ValidationBehavior` projects `Error.Code` from FluentValidation's `PropertyName`, not `ErrorCode`, contrary to this phase's own prior narrative. Packages/Technology Stack/Interface Contracts/Implementation Rules/AOT/Test Rules sections updated from design-locked to shipped; package count now 9 published/3 design-locked. 26/26 `SharedKernel.Validation.FluentValidation.Tests` passing; `SharedKernel.Consumer.Tests` 56/56 passing through the packed dependency graph (up from 54/54). `SK.01.P444` fully `●`; root Phase Backlog P-444 closed (core-phase-implementer)
- [2026-09-03] P-447 implemented and closed (WO-068) — `SharedKernel.Cryptography.KeyVault.Azure` shipped as the tenth published package (this domain's second third-party-dependency package, `Azure.Security.KeyVault.Keys`+`Azure.Identity`): `AzureKeyVaultEncryptionKeyProvider` implements `IEncryptionKeyProvider` internally atop its own `IEnvelopeEncryptionProvider` implementation (never two divergent paths) — `GetCurrentKeyAsync` process-lifetime-caches one locally-generated (`ISecureRandomGenerator`) AES-256 data key wrapped server-side via `CryptographyClient.WrapKeyAsync`; `CryptographicKey.Id`/`EnvelopeDataKey.MasterKeyId` are self-decodable length-prefixed Base64 envelopes (mirrors `AesGcmEncryptionService.Pack`'s existing binary style) so the provider needs no persistent store. Real `Azure.Security.KeyVault.Keys` 4.7.0 API confirmed by reflection over the installed assembly before coding, not assumed (`KeyClient.GetCryptographyClient`, `CryptographyClient.WrapKeyAsync`/`UnwrapKeyAsync`, `KeyWrapAlgorithm.RsaOaep256`/`A256KW`, `KeyType`). Packages/Technology Stack/Interface Contracts/Implementation Rules/DI Registration sections updated from design-locked to shipped; package count now 10 published/2 design-locked. 33/33 `SharedKernel.Cryptography.KeyVault.Azure.Tests` passing — the successful wrap/unwrap round trip against a genuine Azure Key Vault is env-var-gated and honestly skipped in this sandbox (no reachable vault/credentials); real coverage instead proves every locally-decidable validation branch plus three genuine real-network fail-closed proofs against an actually-unreachable loopback vault (real Azure SDK clients, no mocks). `SharedKernel.Consumer.Tests` 61/61 passing (up from 56/56), including a direct `.nuspec` inspection proving `Azure.Security.KeyVault.Keys`/`Azure.Identity` never leak onto `SharedKernel.Cryptography`'s own dependency list. `SK.01.P447` fully `●`; root Phase Backlog P-447 closed (core-phase-implementer)
- [2026-09-03] P-451 implemented and closed (WO-069) — RFC 6238 TOTP / RFC 4226 HOTP generation, verification, and replay guard shipped as a pure additive extension to the existing `SharedKernel.Cryptography` package (no new package, package count unchanged at 10 published/2 design-locked): `Base32` (RFC 4648 unpadded), `IHotpGenerator`/`HotpGenerator`, `ITotpGenerator`/`TotpGenerator`+`TotpProvisioningUri`, `ITotpReplayGuard`+`TotpVerifier`, `RecoveryCodeGenerator`; `AddSharedKernelCryptography` additionally registers `IHotpGenerator`/`ITotpGenerator`/`TotpVerifier` as singletons (`ITotpGenerator` needs a separately-registered `IClock`, `TotpVerifier` needs a separately-registered `ITotpReplayGuard` — neither registered by this package). Every RFC 4226 Appendix D and RFC 6238 Appendix B published vector (fetched verbatim via WebFetch, not transcribed from memory — the SHA-256/SHA-512 seed-length trap and 8-digit-not-6 trap both called out in the phase spec) matched the implementation exactly, first run. Packages/Technology Stack/Interface Contracts/Implementation Rules/DI Registration/AOT/Test Rules sections updated from design-locked to shipped. 235/235 `SharedKernel.Cryptography.Tests` passing (up from 101/101), including a dedicated `ReadmeSampleCompileTests.cs`. Repacked to the local feed at the same pre-tag MinVer version (no git tag exists yet, content overwritten); `SharedKernel.Consumer.Tests` re-verified 61/61 after clearing the stale global-packages cache entry for this package. `SK.01.P451` fully `●`; root Phase Backlog P-451 closed (core-phase-implementer)
- [2026-09-03] P-474 implemented and closed (WO-076) — `SharedKernel.DataPrivacy` shipped as the eleventh published package (references `SharedKernel.Primitives` only, zero third-party NuGet dependency): `DataClassificationAttribute`/`DataClassification`, `SensitiveDataCategoryAttribute`/`SensitiveDataCategory` (pure metadata, never reflected over in production); `PiiMasking.Email`/`.Phone`/`.Pan`/`.Suppress`, with every threshold the design left ambiguous (exact digit-count reveal windows, no-`@`/multi-`@` email handling, sub-4-digit PAN behavior) resolved explicitly and locked by literal-value tests; `IDataSubjectRequestHandler`/`DataSubjectExportBundle`/`DataSubjectErasureReceipt` (no default implementation ships anywhere in the package). T-58's "no reflection anywhere" claim is backed by a compiled-assembly `System.Reflection.Metadata`/`PEReader` scan of the production DLL's `TypeReference` table rather than a source grep — a reusable pattern for future "no reflection" claims elsewhere in this domain. Packages/Technology Stack/Interface Contracts/Implementation Rules/DI Registration/Test Rules sections updated from design-locked to shipped; package count now 11 published/1 design-locked. 56/56 `SharedKernel.DataPrivacy.Tests` passing. Packed to the local feed; the produced `.nuspec` directly inspected and confirmed to declare exactly one dependency, `SharedKernel.Primitives` — the zero-third-party claim verified against real packed output. `SharedKernel.Consumer.Tests` extended 67/67 (up from 61/61), including a `.nuspec` dependency-count assertion. Every README code sample (both `01.Core/README.md`'s new section and the package's own `README.md`) compile-verified in a throwaway scratch file before publishing, catching one accessibility mismatch since fixed. `SK.01.P474` fully `●`; root Phase Backlog P-474 closed (core-phase-implementer)
- [2026-09-03] P-482 implemented and closed (WO-078) — `SharedKernel.Localization` shipped as the twelfth published package (references `SharedKernel.Primitives` + the first-party `Microsoft.Extensions.Localization.Abstractions`, this domain's third first-party/third-party-dependency exception): `ILocalizationCatalog.TryGetString(code, culture, out value)` keyed on the same `code` `Error` factories require, never throwing/blanking on a miss — the throw-site fallback stays `14.Presentation`'s job (P-484); `InMemoryLocalizationCatalog` resolves the design's one unspecified edge, parent-culture fallback (`tr-TR`→`tr`→`CultureInfo.InvariantCulture`, mirroring `ResourceManager`/`IStringLocalizer` semantics — a deliberate implementation-level decision, not an interface requirement); `StringLocalizerLocalizationCatalog` guards against blindly forwarding `LocalizedString.Value` on `ResourceNotFound` (would otherwise surface the raw error code as a fake translation) and temporarily swaps ambient `CultureInfo.CurrentUICulture` since `IStringLocalizer` carries no per-call culture parameter in this framework version (confirmed by reflecting over the installed 10.0.11 assembly — no `WithCulture` member exists); `LocalizationServiceCollectionExtensions`' two registrations are proven, via a real reflection-based assembly scan, to never collide with `13.ServiceDefaults`'s reserved `AddSharedKernelLocalization()` name (P-483). Packages/Technology Stack/Interface Contracts/Implementation Rules/DI Registration/AOT/Test Rules sections updated from design-locked to shipped; package count now 12 published/0 design-locked. 43/43 `SharedKernel.Localization.Tests` passing, including a `ReadmeSampleCompileTests.cs`. Packed to the local feed; the produced `.nuspec` directly inspected and confirmed to declare exactly two dependencies, `SharedKernel.Primitives` + `Microsoft.Extensions.Localization.Abstractions`. `SharedKernel.Consumer.Tests` extended 72/72 (up from 67/67), including a `.nuspec` dependency-count assertion. `SK.01.P482` fully `●`; root Phase Backlog P-482 closed (core-phase-implementer)
- [2026-09-03] SK.01.LoggingRangesNewDomains implemented and closed (cross-cutting, no work order) — `LoggingEventIdRanges` gained `Idempotency = 18000`, `Scheduling = 19000`, `Reporting = 20000`, matching the root folder map exactly; zero breaking impact, no existing base value changed. `LoggingEventIdRangesTests` extended to all 21 domains plus a dedicated byte-for-byte regression fact hardcoding the 18 pre-existing values independently of the shared theory table (catches an accidental transposition the pairwise-uniqueness/modulo checks alone would not). Interface Contracts and Implementation Rules sections updated from "00 through 17"/"design-locked, implementation pending" to shipped/"00 through 20". `01.Core/README.md`'s registry usage section updated to match. 146/146 `SharedKernel.Primitives.Tests` passing; `SharedKernel.Consumer.Tests` re-confirmed 72/72, zero regression. This was `01.Core`'s last open phase key — every phase key in `01.Core/state-map.md` is now `●`, the domain has no further queued work (core-phase-implementer)
