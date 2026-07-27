# 01.Core — Domain Brain

## What This Domain Is

The foundational building blocks domain. Every other domain in the shared kernel depends on this layer, so it must reference nothing from outside `01.Core`. It ships seven independent packages covering: functional primitives (`Result<T>`, `Error`), exception-boundary and multi-result-aggregation railway extensions (`ResultTry`, `ResultCombine`), system abstractions (`IClock` — internally `TimeProvider`-backed since P-295 — SmartEnums, base exceptions, BCL extensions), a time-ordered identifier generator (`IIdGenerator`, UUIDv7-backed), a two-path guard system (`Guard.Against` / `Guard.Throw`), Options-pattern validation, a Feature Flag abstraction (including weighted-variant/gradual-rollout evaluation), dependency-free cryptographic primitives (secret-agnostic one-way hashing, AES-GCM symmetric encryption, RSA/ECDSA + HMAC signing, secure random generation, and non-secret content fingerprinting via `IContentHasher`), a dependency-free payload compression primitive (`SharedKernel.Compression`, Brotli-default/GZip-keyed), the platform-wide `LoggingEventIdRanges` registry — a compile-time constant reserving each folder-map domain's `EventId` numbering block for the `[LoggerMessage]` logging convention enforced repo-wide — and the `WellKnownHeaders` / `WellKnownBaggageKeys` / `WellKnownTagKeys` registries, the platform-wide source of cross-service propagation identifier literals (HTTP/gRPC header names, `Activity` baggage keys, and `Activity` tag/attribute keys) that every domain touching correlation-id, tenant-id, or error-classification propagation must reference instead of redeclaring locally.

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
| `SharedKernel.Cryptography` | Secret-agnostic one-way hashing, AES-256-GCM symmetric encryption, RSA/ECDSA + HMAC signing, secure random/token generation, non-secret content fingerprinting (`IContentHasher`) | `SharedKernel.Primitives`, `SharedKernel.Configuration` |
| `SharedKernel.Compression` | Generic payload compression (`IPayloadCompressor`): Brotli default, GZip keyed alternate | `SharedKernel.Primitives`, `SharedKernel.Configuration` |

All seven target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

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
    .Code                                                  → string
    .Message                                               → string
    .Type                                                  → ErrorType

ErrorType  (enum)
    None | Unexpected | Validation | NotFound | Conflict | Unauthorized | BusinessRule
    — BusinessRule: domain invariant violation; maps to HTTP 422 Unprocessable Entity at presentation layer;
      semantically distinct from Validation (input format/presence) and Unexpected (system fault)

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
    — each domain constant = {two-digit folder-map number} * 1000; reserves a contiguous 1000-wide EventId block
      ({value}..{value}+999) exactly matching the root CLAUDE.md folder map (00 through 17)
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
    — variant methods added P-298/WO-049; bridge Microsoft.FeatureManagement's IVariantFeatureManager
      the same way IsEnabledAsync already bridges its plain boolean evaluation, without leaking any
      Microsoft.FeatureManagement type into this interface's public surface
    — the existing boolean IsEnabledAsync members and their behavior are completely unchanged (additive-only)

FeatureVariant  (sealed record — P-298/WO-049)
    .Name                                                  → string           (caller-defined variant identifier, e.g. "ControlGroup" / "VariantB")
    .Configuration                                         → string?          (raw configuration payload for this variant, if any; caller deserializes to its own strongly-typed shape)
    — a feature with no configured variants (or an unresolvable context) falls back to a deterministic,
      documented default variant rather than throwing

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
    → registers IFeatureManager backed by Microsoft.FeatureManagement; the variant path (GetVariantAsync)
      requires no additional configuration beyond what Microsoft.FeatureManagement's own variant/allocation
      configuration schema already needs
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
    Encrypt(byte[] plaintext)                                   → EncryptedPayload        (always encrypts with IEncryptionKeyProvider.GetCurrentKey())
    Decrypt(EncryptedPayload payload)                           → Result<byte[]>          (failure: Error.Unexpected — tamper, wrong key, or unknown KeyId; never throws CryptographicException directly)
    EncryptToString(string plaintext)                           → string                  (convenience: UTF-8 → Encrypt → single self-describing Base64 string, KeyId+Nonce+Ciphertext+Tag packed together)
    DecryptToString(string encoded)                             → Result<string>          (convenience inverse of EncryptToString)

EncryptedPayload  (sealed record)
    .KeyId                                                      → string                  (which key version encrypted this payload)
    .Nonce                                                      → byte[]                  (96-bit, random per call — never reused)
    .Ciphertext                                                 → byte[]
    .Tag                                                        → byte[]                  (128-bit AES-GCM authentication tag)

AesGcmEncryptionService  (sealed class, implements ISymmetricEncryptionService)
    — AES-256-GCM via System.Security.Cryptography.AesGcm; authenticated (tamper-evident) encryption only —
      never an unauthenticated mode such as CBC/ECB

IEncryptionKeyProvider
    GetCurrentKey()                                             → CryptographicKey        (used for every new Encrypt call)
    GetKey(string keyId)                                        → CryptographicKey?       (used to Decrypt older payloads; null if the key was retired/unknown)
    — implemented by the consuming service (Key Vault, environment config, secret store); SharedKernel.Cryptography
      ships no default implementation and holds no key material itself

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

CryptographyOptions  (bound via IOptions<T>; validated via SharedKernel.Configuration's AddValidatedOptions)
    .Pbkdf2Iterations                                           → int                     (default 600_000)
    .DefaultSigningKeyId                                        → string?

AddSharedKernelCryptography(IConfiguration configuration)
    → registers CryptographyOptions (validated, ValidateOnStart), IOneWayHasher, ISymmetricEncryptionService,
      IAsymmetricSignatureService, IHmacSigner, ISecureRandomGenerator, and (as of P-296/WO-049)
      IContentHasher as singletons (all are stateless and thread-safe)
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
    Decompress(byte[] compressed)                                → Result<byte[]>          (Error.Unexpected on corrupt/truncated input — never throws InvalidDataException directly)
    Decompress(Stream input, Stream output)                      → Result                  (non-generic — the decompressed payload already landed in the caller's output stream, no typed value to carry)
    DecompressAsync(Stream input, Stream output, CancellationToken ct = default) → Task<Result>
    — generic, cross-cutting compress/decompress of an arbitrary byte payload or stream — the direct sibling
      of ISymmetricEncryptionService's "general-purpose encrypt/decrypt of arbitrary payloads" role: same
      shape, same zero-dependency BCL-only constraint, orthogonal concern
    — compression and encryption are frequently combined; the correct order is always compress-THEN-encrypt,
      never the reverse — compressing already-encrypted/high-entropy ciphertext wastes CPU for no size
      benefit, since ciphertext has no redundancy left to compress. This package never compresses a payload
      that has already passed through ISymmetricEncryptionService.Encrypt

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
- Adding a new folder-map domain (00–17 today) means adding exactly one new `const int` field to `LoggingEventIdRanges` — never renumbering or reassigning an existing domain's base value; doing so would silently invalidate every already-shipped `EventId` in that domain.
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
- `IFeatureManager`'s variant surface (`GetVariantAsync`) is additive-only — the existing boolean `IsEnabledAsync` members and their behavior must never change as a side effect of adding variant support.
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
- `IPayloadCompressor.Decompress` must return `Result<byte[]>` / `Result` (stream overload), never throw `InvalidDataException`/`CryptographicException`-style exceptions directly — a corrupt or truncated compressed payload is an expected failure mode for this contract, mirroring `ISymmetricEncryptionService.Decrypt`'s existing failure-handling shape.
- Compression must always happen **before** encryption when both are applied to the same payload, never the reverse — compressing already-encrypted/high-entropy ciphertext wastes CPU for no size benefit. This ordering rule must be stated in `IPayloadCompressor`'s XML docs, not just this brain.
- `SharedKernel.Compression` ships no `.Abstractions`/`.{Provider}` sibling-package split — a single package with a keyed-DI algorithm choice (`BrotliPayloadCompressor` unkeyed default + "Brotli"/"GZip"-keyed singletons), mirroring `SharedKernel.Cryptography`'s `IAsymmetricSignatureService` RSA/ECDSA keyed-singleton precedent rather than sibling `.Brotli`/`.GZip` packages — the algorithm set is small, closed, and purely-BCL, exactly the condition under which that precedent applies.
- No static mutable state anywhere in this domain.

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
// P-298) both resolve through the same IFeatureManager registration.
services.AddSharedKernelFeatureManagement(configuration);

// Cryptography — registers IOneWayHasher, ISymmetricEncryptionService, IAsymmetricSignatureService,
// IHmacSigner, ISecureRandomGenerator, and IContentHasher (P-296). The consuming service must separately
// register its own IEncryptionKeyProvider (and any signing key material) — this package ships no key material.
services.AddSharedKernelCryptography(configuration);
services.AddSingleton<IEncryptionKeyProvider, MyKeyVaultBackedKeyProvider>();

// Compression (P-297) — registers BrotliPayloadCompressor as both the unkeyed IPayloadCompressor default
// and the "Brotli"-keyed singleton, plus GZipPayloadCompressor as the "GZip"-keyed singleton only.
services.AddSharedKernelCompression(configuration);
var gzip = provider.GetRequiredKeyedService<IPayloadCompressor>(
    CompressionServiceCollectionExtensions.GZipPayloadCompressorKey);
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
- `LoggingEventIdRanges` is a static class of compile-time `const int` values — no reflection, no runtime computation, fully AOT-safe, and directly usable as a `[LoggerMessage(EventId = ...)]` attribute argument (which itself requires a compile-time constant expression).
- `WellKnownHeaders`, `WellKnownBaggageKeys`, and `WellKnownTagKeys` are static classes of compile-time `const string` values — no reflection, no runtime computation, fully AOT-safe; directly usable as header-name literals in `HttpRequestMessage.Headers`, gRPC `Metadata` entries, `Activity.AddBaggage(key, value)`, or `Activity.SetTag(key, value)` calls without any allocation beyond the string constant itself.
- `IIdGenerator`/`UuidV7IdGenerator` calls `Guid.CreateVersion7()` directly — a static BCL method, no reflection, fully AOT-safe.
- `SystemClock`'s `TimeProvider`-backed internals (P-295) call `TimeProvider.GetUtcNow()` directly — no reflection, fully AOT-safe; `TimeProvider` itself has shipped in the BCL since .NET 8 and requires no NuGet reference.
- `ResultTry` and `ResultCombine` are static classes — AOT-safe by default. `ResultTry.TryAsync`'s `async`/`await` body is ordinary compiler-generated async state-machine code, not a reflection-based construct, and remains fully AOT-safe.
- All railway extension methods are static — AOT-safe by default. Async overloads use `Task` continuation patterns to avoid AOT-hostile constructs.
- `Microsoft.Extensions.Options` is AOT-compatible as of .NET 8+ — verify on each upgrade.
- `Microsoft.FeatureManagement` — verify AOT status on each major upgrade, including the variant/allocation API surface `GetVariantAsync` (P-298) bridges; the `IFeatureManager` wrapper allows a swap if needed. Flag (do not block on) any AOT gap found in the variant API specifically.
- All BCL extension methods are static — AOT-safe by default.
- `IGuardClause` and all guard extension methods are static — AOT-safe. `DefaultGuardClause` is sealed, no virtual dispatch.
- `EqualityComparer<T>.Default` used in `Default<T>` guard is AOT-safe — it uses static dispatch via generic specialization in .NET 10.
- `InvalidFormat` / `Email` use `Regex` constructed with `RegexOptions.Compiled` in a static field — the compiled delegate is created once at type-initialization, which is AOT-compatible. `ConcurrentDictionary` is used only for pattern-keyed caching of caller-supplied patterns in `InvalidFormat`; the email regex is a fixed static field.
- `OutOfRange<T>` uses the `IComparable<T>` constraint — static generic dispatch, no boxing for value types, AOT-safe.
- `Pbkdf2OneWayHasher`, `AesGcmEncryptionService`, `RsaSignatureService`, `EcdsaSignatureService`, `HmacSha256Signer`, `CryptoRandomGenerator`, and `Sha256ContentHasher` (P-296) are sealed classes calling directly into BCL `System.Security.Cryptography` types (`Rfc2898DeriveBytes`, `AesGcm`, `RSA`, `ECDsa`, `HMACSHA256`, `RandomNumberGenerator`, `SHA256`) — no reflection, fully AOT-safe.
- `CryptographyOptions` binds via `Microsoft.Extensions.Options`, the same AOT-compatible (.NET 8+) path used by `SharedKernel.Configuration`.
- `BrotliPayloadCompressor` and `GZipPayloadCompressor` (P-297) are sealed classes calling directly into BCL `System.IO.Compression` types (`BrotliStream`, `GZipStream`) — no reflection, fully AOT-safe. `CompressionOptions` binds via the same `Microsoft.Extensions.Options` AOT-compatible path.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Primitives.Tests/` — Result, Error, IClock, SmartEnum, IHasSuccessFlag, IResultOfT\<T\>, IFailureFactory\<TSelf\> (generic-constraint dispatch producing a correct failure `Result<T>` for at least two distinct closed shapes, e.g. `Result<int>`/`Result<string>`, plus confirmation that `Result` (non-generic) is not assignable to `IFailureFactory<Result>`), `LoggingEventIdRanges` (all 18 domain base constants are pairwise unique, each is a multiple of 1000, and each equals exactly `{domain-folder-number} * 1000` matching the root CLAUDE.md folder map 00 through 17), `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys` (every constant's literal value is pinned exactly — `CorrelationId` header = `"X-Correlation-Id"`, `TenantId` header = `"X-Tenant-Id"`, `CorrelationId` baggage key = `"correlation.id"`, and every `WellKnownTagKeys` field — so a future edit cannot silently drift a cross-service propagation identifier), `IIdGenerator`/`UuidV7IdGenerator` (uniqueness across a large generation batch; values whose embedded millisecond timestamps differ compare as non-decreasing under the default `Guid` comparer — values sharing the same millisecond carry no such guarantee against each other and must not be asserted as strictly ordered), `SystemClock`'s `TimeProvider`-backed internals (parameterless `SystemClock()` reflects real time; `SystemClock(fakeTimeProvider)` reflects the injected provider's current instant, including after the fake advances time; `Today` derives correctly from the same instant)
- `SharedKernel.Core.Tests/` — exceptions, railway extensions, BCL extensions, `ResultTry`/`ResultTry.TryAsync` (delegate success path, thrown-exception-to-`Error.Unexpected` translation including a nested/flattened `AggregateException` case, custom exception-mapper overload, never-rethrows guarantee), `ResultCombine` (all-success non-generic and generic variants, single-failure and all-failure variants verifying every collected `Error` surfaces — not just the first — for both the non-generic `Result` and generic `Result<T>` overloads)
- `SharedKernel.Guards.Tests/` — guard functional path (Against.*), guard throw path (Throw.*), boundary theories
- `SharedKernel.Configuration.Tests/` — ValidatedOptions eager validation
- `SharedKernel.FeatureManagement.Tests/` — IFeatureManager enable/disable, context variant, `GetVariantAsync` deterministic variant assignment given a fixed context/seed, predictable fallback for an unconfigured feature, and a regression check that the existing boolean `IsEnabledAsync` surface is unchanged
- `SharedKernel.Cryptography.Tests/` — `IOneWayHasher` hash/verify roundtrip and rehash-needed detection across iteration-count changes, covering at least one password-shaped secret and one non-password-shaped secret (e.g., an API key string) to prove the contract is genuinely secret-agnostic; `ISymmetricEncryptionService` encrypt/decrypt roundtrip, tamper detection (flipped ciphertext/tag byte must fail `Decrypt`), and unknown/retired `KeyId` handling; `IAsymmetricSignatureService` sign/verify roundtrip for both RSA and ECDSA with wrong-key and tampered-data failure cases; `IHmacSigner` sign/verify roundtrip and tamper detection; `ISecureRandomGenerator` output length and non-repetition across calls; `IContentHasher` deterministic digest for identical input, differing digest for a single-byte change, streaming (`Stream`/async) vs. in-memory (`byte[]`) overloads producing identical output, and hex/Base64 encoding correctness via `ContentHasherExtensions`; DI registration sanity for `AddSharedKernelCryptography` (now covering six registered services)
- `SharedKernel.Compression.Tests/` — roundtrip for both `BrotliPayloadCompressor` and `GZipPayloadCompressor` (byte[] and stream overloads), tamper/truncation of compressed input surfaces as a `Result`/`Result<byte[]>` failure and never an unhandled exception, streaming vs. in-memory overloads produce equivalent decompressed output, DI registration sanity for `AddSharedKernelCompression` (unkeyed Brotli default + both keyed singletons resolve)
- Railway-extension chains must be covered: map → bind → match over both success and failure paths.
- `SmartEnum` must cover: FromValue hit, FromValue miss (throws), TryFromValue, List completeness.
- Validated options test must assert that a misconfigured `TOptions` throws at `IHost.StartAsync()`.
- Guard tests must cover **both paths independently**: functional `Against.*` (assert returned `Error?`) and throw `Throw.*` (assert `DomainException` thrown on violation, no exception on pass).
- Numeric and string-length guard tests must use `[Theory]` with `[InlineData]` for boundary conditions (exactly at limit, one below, one above).
- Collection guard tests must verify single enumeration — use a counting stub/wrapper `IEnumerable<T>` that increments a counter on `GetEnumerator()` calls.

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
