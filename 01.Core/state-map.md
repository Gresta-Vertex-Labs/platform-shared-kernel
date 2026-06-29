# 01.Core — State Map

> **What this file is:** Phase and task tracker for all work within `01.Core`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.01.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition | Root Backlog ID |
|-----------|-------------------|-------------------|-----------------|
| `SK.01.Design` | Design | All tasks in Phase: Design are `●` | — |
| `SK.01.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` | — |
| `SK.01.Core` | Core | All tasks in Phase: Core are `●` | — |
| `SK.01.Tests` | Tests | All tasks in Phase: Tests are `●` | — |
| `SK.01.Docs` | Docs | All tasks in Phase: Docs are `●` | — |
| `SK.01.Published` | Published | All tasks in Phase: Published are `●` | — |
| `SK.01.P042` | P-042 Error.BusinessRule Factory | All tasks in Phase: P-042 are `●` | P-042 |
| `SK.01.WO033Impl` | WO-033 Cryptography Implementation (P-207/P-208/P-209) | All tasks in Phase: WO-033 Implementation are `●` | P-207, P-208, P-209 |
| `SK.01.WO034` | WO-034 IOneWayHasher Rename (P-210/P-211/P-212/P-213) | All tasks in Phase: WO-034 are `●` | P-210, P-211, P-212, P-213 |

---

## Active Work

_Nothing in progress — all tasks complete._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement Result<T> type | SK.01.Core | SharedKernel.Primitives | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| C-10 FeatureManagement adapter | SK.01.Core | Waiting for Microsoft.FeatureManagement AOT verdict |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|--------------|:-----:|-------|
| `SharedKernel.Primitives` | — | `○` | Zero external dependencies |
| `SharedKernel.Core` | — | `○` | References Primitives |
| `SharedKernel.Configuration` | — | `○` | References Primitives |
| `SharedKernel.FeatureManagement` | — | `○` | References Primitives |
| `SharedKernel.Guards` | — | `○` | References Primitives + Core; two-path guard API |
| `SharedKernel.Cryptography` | Published | `●` | References Primitives + Configuration; zero third-party NuGet deps; WO-033 fully closed; WO-034 rename (`IPasswordHasher`→`IOneWayHasher`) fully closed — re-packed and published at `2.0.0` |

---

## Phase: Design <!-- phase-key: SK.01.Design -->

> Finalize all type shapes, interface contracts, and DI extension signatures before any implementation begins.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Define `Result<T>` and `Result` type shape (sealed class, implicit operators, access rules) | SharedKernel.Primitives | `●` |
| D-02 | Define `Error` sealed record shape, `ErrorType` enum variants, and factory method signatures | SharedKernel.Primitives | `●` |
| D-03 | Define `IClock` interface and `SystemClock` implementation contract | SharedKernel.Primitives | `●` |
| D-04 | Define `SmartEnum<TEnum, TValue>` abstract base shape — factory methods, List, AOT lookup strategy | SharedKernel.Primitives | `●` |
| D-05 | Define base exception hierarchy (SharedKernelException, DomainException, ValidationException, NotFoundException, ConflictException, UnauthorizedException) | SharedKernel.Core | `●` |
| D-06 | Define `Result<T>` railway extension method signatures (.Map, .MapError, .Bind, .Match, .Tap, async overloads) and void `Match` on non-generic `Result` | SharedKernel.Core | `●` |
| D-07 | Define BCL extension method surface (string, IEnumerable<T>, DateTimeOffset, Guid) | SharedKernel.Core | `●` |
| D-08 | Define `AddValidatedOptions<TOptions>` DI extension signature and startup-validation contract | SharedKernel.Configuration | `●` |
| D-09 | Define `IFeatureManager` interface and `FeatureDefinition` record shape | SharedKernel.FeatureManagement | `●` |
| D-10 | Confirm `Microsoft.FeatureManagement` NuGet version and AOT compatibility status | SharedKernel.FeatureManagement | `●` |
| D-11 | Define `ValidationResult` and `ValidationResult<T>` sealed record shapes — multi-error pair, distinct from `Result<T>` | SharedKernel.Primitives | `●` |
| D-12 | Define `ErrorCodes` static class structure — nested static category classes, well-known string constants | SharedKernel.Primitives | `●` |
| D-13 | Define `IGuardClause` marker interface and `DefaultGuardClause` private sealed implementation; define `Guard.Against` / `Guard.Throw` static entry-point shape | SharedKernel.Guards | `●` |
| D-14 | Define full guard extension method surface on `IGuardClause`: null/empty, string length, numeric, range, default, Guid, regex-format, collection, email, boolean-predicate, SmartEnum | SharedKernel.Guards | `●` |
| D-15 | Define `GuardDescriptions` internal static class — const string message templates, `{0}`/`{1}` placeholder convention | SharedKernel.Guards | `●` |
| D-16 | Define `Guard.Throw` nested static class — mirror all `Against.*` extensions as void methods throwing `DomainException` on non-null `Error` return | SharedKernel.Guards | `●` |
| D-20 | Define `IPasswordHasher` interface and `PasswordVerificationResult` enum shape — self-describing hash output, rehash-needed detection | SharedKernel.Cryptography | `●` |
| D-21 | Define `ISymmetricEncryptionService` interface, `EncryptedPayload` record shape, and `IEncryptionKeyProvider`/`CryptographicKey` contracts for AES-256-GCM with versioned key rotation | SharedKernel.Cryptography | `●` |
| D-22 | Define `IAsymmetricSignatureService` interface for RSA/ECDSA sign and verify | SharedKernel.Cryptography | `●` |
| D-23 | Define `IHmacSigner` interface for keyed-hash sign and verify with constant-time comparison | SharedKernel.Cryptography | `●` |
| D-24 | Define `ISecureRandomGenerator` interface for cryptographically secure byte/token generation | SharedKernel.Cryptography | `●` |
| D-25 | Define `CryptographyOptions` shape and `AddSharedKernelCryptography` DI extension signature | SharedKernel.Cryptography | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.01.Scaffold -->

> Wire up .csproj NuGet references, intra-domain project references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Add `Microsoft.Extensions.Options.DataAnnotations` NuGet ref to `SharedKernel.Configuration.csproj` | SharedKernel.Configuration | `●` |
| S-02 | Add `Microsoft.FeatureManagement` NuGet ref to `SharedKernel.FeatureManagement.csproj` | SharedKernel.FeatureManagement | `●` |
| S-03 | Add `SharedKernel.Primitives` project reference to `SharedKernel.Core.csproj` | SharedKernel.Core | `●` |
| S-04 | Add `SharedKernel.Primitives` project reference to `SharedKernel.Configuration.csproj` | SharedKernel.Configuration | `●` |
| S-05 | Add `SharedKernel.Primitives` project reference to `SharedKernel.FeatureManagement.csproj` | SharedKernel.FeatureManagement | `●` |
| S-06 | Create folder structure (`Results/`, `Errors/`, `Clocks/`, `Enums/`) in `SharedKernel.Primitives` | SharedKernel.Primitives | `●` |
| S-07 | Create folder structure (`Exceptions/`, `Extensions/`) in `SharedKernel.Core` | SharedKernel.Core | `●` |
| S-08 | Create folder structure (`Options/`, `Extensions/`) in `SharedKernel.Configuration` | SharedKernel.Configuration | `●` |
| S-09 | Create folder structure (`Abstractions/`, `Extensions/`) in `SharedKernel.FeatureManagement` | SharedKernel.FeatureManagement | `●` |
| S-10 | Register all four projects in `Platform.SharedKernel.slnx` under solution folder `01.Core` | All | `●` |
| S-11 | Stub empty `.Tests` projects with xUnit package reference for all four packages | All | `●` |
| S-12 | Create `01.Core/SharedKernel.Guards/SharedKernel.Guards.csproj` targeting `net10.0`; add project refs to `SharedKernel.Primitives` and `SharedKernel.Core` | SharedKernel.Guards | `●` |
| S-13 | Create folder structure (`Clauses/`, `Descriptions/`) inside `SharedKernel.Guards/` | SharedKernel.Guards | `●` |
| S-14 | Create `SharedKernel.Guards.Tests.csproj` nested inside `SharedKernel.Guards/` with xUnit reference | SharedKernel.Guards | `●` |
| S-15 | Register `SharedKernel.Guards` and `SharedKernel.Guards.Tests` in `Platform.SharedKernel.slnx` under solution folder `01.Core` | SharedKernel.Guards | `●` |
| S-16 | Create `01.Core/SharedKernel.Cryptography/SharedKernel.Cryptography.csproj` targeting `net10.0`; add project references to `SharedKernel.Primitives` and `SharedKernel.Configuration` | SharedKernel.Cryptography | `●` |
| S-17 | Create `SharedKernel.Cryptography.Tests.csproj` nested inside `SharedKernel.Cryptography/` with xUnit + NSubstitute references | SharedKernel.Cryptography | `●` |
| S-18 | Register `SharedKernel.Cryptography` and `SharedKernel.Cryptography.Tests` in `Platform.SharedKernel.slnx` under solution folder `01.Core` | SharedKernel.Cryptography | `●` |

---

## Phase: Core <!-- phase-key: SK.01.Core -->

> Full implementation of all types, interfaces, extensions, and DI registrations.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `Result<T>` (sealed class) and `Result` (non-generic) with Success/Failure factories and implicit operators | SharedKernel.Primitives | `●` |
| C-02 | Implement `Error` sealed record with `ErrorType` enum and all factory methods | SharedKernel.Primitives | `●` |
| C-03 | Implement `IClock` interface and `SystemClock` (wraps `DateTimeOffset.UtcNow`) | SharedKernel.Primitives | `●` |
| C-04 | Implement `SmartEnum<TEnum, TValue>` abstract base with AOT-safe static list and value lookup | SharedKernel.Primitives | `●` |
| C-05 | Implement `services.AddClock()` DI extension wiring `SystemClock` as singleton | SharedKernel.Primitives | `●` |
| C-06 | Implement base exception hierarchy (all derive from `SharedKernelException`, carry `Error` payload; `ValidationException` accepts `IReadOnlyList<Error>`) | SharedKernel.Core | `●` |
| C-07 | Implement `Result<T>` railway extension methods (.Map, .MapError, .Bind, .Match, .Tap) and void `Match` on non-generic `Result` | SharedKernel.Core | `●` |
| C-08 | Implement async `Task<Result<T>>` railway extension overloads — avoid unnecessary state machines on outer extension | SharedKernel.Core | `●` |
| C-09 | Implement BCL extensions: string (ToSnakeCase, ToCamelCase, ToPascalCase, IsNullOrWhiteSpace), IEnumerable<T> (ToBatches, IsNullOrEmpty, WhereNotNull), DateTimeOffset (ToUnixMilliseconds, StartOfDay, EndOfDay), Guid (IsEmpty) | SharedKernel.Core | `●` |
| C-10 | Implement `AddValidatedOptions<TOptions>` DI extension with `.ValidateDataAnnotations().ValidateOnStart()` | SharedKernel.Configuration | `●` |
| C-11 | Implement `IFeatureManager` abstraction interface and `FeatureDefinition` sealed record | SharedKernel.FeatureManagement | `●` |
| C-12 | Implement `MicrosoftFeatureManagerAdapter` wrapping `Microsoft.FeatureManagement.IFeatureManager` | SharedKernel.FeatureManagement | `●` |
| C-13 | Implement `AddSharedKernelFeatureManagement` DI extension | SharedKernel.FeatureManagement | `●` |
| C-14 | Implement `ValidationResult` (non-generic, `IsValid` + `IReadOnlyList<Error>`) and `ValidationResult<T>` (adds `Value`) sealed records | SharedKernel.Primitives | `●` |
| C-15 | Implement `ErrorCodes` static class with nested category constants (e.g., `ErrorCodes.Validation.Required`, `ErrorCodes.NotFound.Default`) | SharedKernel.Primitives | `●` |
| C-16 | Implement `IGuardClause` marker interface and private sealed `DefaultGuardClause` with static `Guard.Against` factory returning the marker | SharedKernel.Guards | `●` |
| C-17 | Implement null/empty guard extensions: `Null<T>`, `NullOrEmpty`, `NullOrWhiteSpace` — all return `Error?` | SharedKernel.Guards | `●` |
| C-18 | Implement string length guard extensions: `ShorterThan(string, int minLength)`, `LongerThan(string, int maxLength)` | SharedKernel.Guards | `●` |
| C-19 | Implement numeric guard extensions for `int`, `decimal`, `long`: `NegativeOrZero`, `Negative`, `NotPositive` | SharedKernel.Guards | `●` |
| C-20 | Implement `OutOfRange<T>(T value, T min, T max)` constrained to `IComparable<T>` | SharedKernel.Guards | `●` |
| C-21 | Implement `Default<T>(T value)` using `EqualityComparer<T>.Default` — no reflection | SharedKernel.Guards | `●` |
| C-22 | Implement `InvalidGuid(Guid value)` catching `Guid.Empty` | SharedKernel.Guards | `●` |
| C-23 | Implement `InvalidFormat(string value, string pattern)` with compiled/cached `Regex` (static field, bounded timeout) — zero new `Regex` per call | SharedKernel.Guards | `●` |
| C-24 | Implement `Email(string? value)` using same compiled/cached regex strategy as `InvalidFormat` — no third-party NuGet | SharedKernel.Guards | `●` |
| C-25 | Implement collection guards: `Empty<T>(IEnumerable<T>)`, `MaxCount<T>(IEnumerable<T>, int)`, `MinCount<T>(IEnumerable<T>, int)` — enumerate once via `Count()` or single materialisation | SharedKernel.Guards | `●` |
| C-26 | Implement boolean predicate guards: `True(bool condition, Error error)`, `False(bool condition, Error error)` — caller-supplied error, no allocation on pass | SharedKernel.Guards | `●` |
| C-27 | Implement `InvalidSmartEnum<TEnum, TValue>(TValue id)` constrained to `TEnum : SmartEnum<TEnum,TValue>` using `SmartEnum<TEnum,TValue>.TryFromValue` — zero reflection | SharedKernel.Guards | `●` |
| C-28 | Implement `GuardDescriptions` internal static class with all const string message templates (not public API) | SharedKernel.Guards | `●` |
| C-29 | Implement `Guard.Throw` nested static class — mirrors all `Against.*` extensions as void methods; throws `DomainException(error)` on non-null `Error` return | SharedKernel.Guards | `●` |
| C-33 | Implement `IPasswordHasher` via PBKDF2-HMACSHA256 (`Rfc2898DeriveBytes.Pbkdf2`) with self-describing versioned output and configurable iteration count (default 600,000) | SharedKernel.Cryptography | `●` |
| C-34 | Implement `ISymmetricEncryptionService` via AES-256-GCM (`System.Security.Cryptography.AesGcm`) with random 96-bit nonce per call and `IEncryptionKeyProvider`-resolved versioned keys; `Decrypt` returns `Result<byte[]>`, never throws `CryptographicException` directly | SharedKernel.Cryptography | `●` |
| C-35 | Implement `IAsymmetricSignatureService` via RSA (2048-bit, PSS/SHA-256) and ECDSA (P-256/SHA-256) | SharedKernel.Cryptography | `●` |
| C-36 | Implement `IHmacSigner` via HMACSHA256 with `CryptographicOperations.FixedTimeEquals` constant-time verification | SharedKernel.Cryptography | `●` |
| C-37 | Implement `ISecureRandomGenerator` via `RandomNumberGenerator` — byte array and URL-safe Base64 token generation | SharedKernel.Cryptography | `●` |
| C-38 | Implement `CryptographyOptions` validation and `AddSharedKernelCryptography` DI extension registering all five services as singletons via `AddValidatedOptions` | SharedKernel.Cryptography | `●` |

---

## Phase: Tests <!-- phase-key: SK.01.Tests -->

> Unit test coverage for all packages. No integration tests needed — this domain has no external dependencies.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Unit: `Result<T>` — success path, failure path, implicit operators, accessor throws on wrong state | SharedKernel.Primitives.Tests | `●` |
| T-02 | Unit: `Error` — factory methods, record equality, ErrorType discrimination, `Error.None` sentinel | SharedKernel.Primitives.Tests | `●` |
| T-03 | Unit: `SmartEnum` — FromValue hit, FromValue miss (throws), TryFromValue, FromName, List completeness | SharedKernel.Primitives.Tests | `●` |
| T-04 | Unit: `IClock` / `SystemClock` — returns current UTC; verify `FakeClock` usable in tests | SharedKernel.Primitives.Tests | `●` |
| T-05 | Unit: Base exceptions — carry correct `Error`, message propagates, hierarchy verified | SharedKernel.Core.Tests | `●` |
| T-06 | Unit: Railway extensions — Map, MapError, Bind, Match, Tap chains over success and failure paths; void Match on non-generic Result; async variants | SharedKernel.Core.Tests | `●` |
| T-07 | Unit: BCL extensions — string conversions, IEnumerable batching and nullability, DateTimeOffset helpers, Guid.IsEmpty | SharedKernel.Core.Tests | `●` |
| T-08 | Unit: `AddValidatedOptions` — valid config registers without throw; invalid config throws at `IHost.StartAsync()` | SharedKernel.Configuration.Tests | `●` |
| T-09 | Unit: `IFeatureManager` adapter — enabled flag returns true, disabled returns false, context-aware variant | SharedKernel.FeatureManagement.Tests | `●` |
| T-10 | Unit: `ValidationResult` / `ValidationResult<T>` — multi-error collection, `IsValid` semantics, generic `Value` access, distinction from `Result<T>` | SharedKernel.Primitives.Tests | `●` |
| T-11 | Unit: Guard functional path (Against.*) — null/empty/whitespace: returns null on pass, non-null `Error` on violation | SharedKernel.Guards.Tests | `●` |
| T-12 | Unit: Guard functional path — string length: boundary theory tests (exact min, exact max, one below, one above) | SharedKernel.Guards.Tests | `●` |
| T-13 | Unit: Guard functional path — numeric guards (`int`, `decimal`, `long`): negative, negativeOrZero, notPositive; boundary theories | SharedKernel.Guards.Tests | `●` |
| T-14 | Unit: Guard functional path — OutOfRange: pass at bounds, fail outside bounds; Default; InvalidGuid | SharedKernel.Guards.Tests | `●` |
| T-15 | Unit: Guard functional path — InvalidFormat and Email: valid inputs pass (null on return), invalid inputs return non-null Error; confirm no new Regex per call | SharedKernel.Guards.Tests | `●` |
| T-16 | Unit: Guard functional path — collection guards: Empty, MaxCount, MinCount; verify single enumeration via stub | SharedKernel.Guards.Tests | `●` |
| T-17 | Unit: Guard functional path — True/False boolean predicate guards; InvalidSmartEnum with known/unknown value | SharedKernel.Guards.Tests | `●` |
| T-18 | Unit: Guard throw path (Throw.*) — assert `DomainException` thrown on violation; assert no exception on pass for all guard categories | SharedKernel.Guards.Tests | `●` |
| T-23 | Unit: `IPasswordHasher` — hash/verify roundtrip, wrong password fails, rehash-needed detection across iteration-count changes | SharedKernel.Cryptography.Tests | `●` |
| T-24 | Unit: `ISymmetricEncryptionService` — encrypt/decrypt roundtrip, tamper detection (flipped ciphertext/tag byte fails), unknown key id fails, multi-key-version decrypt | SharedKernel.Cryptography.Tests | `●` |
| T-25 | Unit: `IAsymmetricSignatureService` — sign/verify roundtrip for RSA and ECDSA, verification fails with wrong key or tampered data | SharedKernel.Cryptography.Tests | `●` |
| T-26 | Unit: `IHmacSigner` — sign/verify roundtrip, tamper detection, constant-time comparison behavior | SharedKernel.Cryptography.Tests | `●` |
| T-27 | Unit: `ISecureRandomGenerator` — output length correctness, statistical non-repetition across calls, never delegates to `System.Random` | SharedKernel.Cryptography.Tests | `●` |
| T-28 | Unit: `AddSharedKernelCryptography` DI registration sanity — all five services resolve; invalid `CryptographyOptions` throws at `IHost.StartAsync()` | SharedKernel.Cryptography.Tests | `●` |

---

## Phase: Docs <!-- phase-key: SK.01.Docs -->

> XML doc comments on all public APIs, README with usage examples.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML doc all public types and interfaces across all four packages | All | `●` |
| DO-02 | Write `01.Core/README.md` with usage examples for Result, Error, IClock, SmartEnum | All | `●` |
| DO-03 | Document `Result<T>` railway pattern and error-propagation guide with code samples | SharedKernel.Primitives, SharedKernel.Core | `●` |
| DO-04 | Document `AddValidatedOptions` startup-validation pattern with annotated example | SharedKernel.Configuration | `●` |
| DO-05 | XML doc all public types, extension methods, and parameters in `SharedKernel.Guards` | SharedKernel.Guards | `●` |
| DO-06 | Add Guards usage examples to `01.Core/README.md` — functional `Against.*` path and imperative `Throw.*` path with annotated samples | SharedKernel.Guards | `●` |
| DO-10 | XML doc all public types in `SharedKernel.Cryptography` | SharedKernel.Cryptography | `●` |
| DO-11 | Add Cryptography usage examples to `01.Core/README.md` — password hashing, encrypt/decrypt, signing, secure token generation | SharedKernel.Cryptography | `●` |

---

## Phase: Published <!-- phase-key: SK.01.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Add NuGet metadata to all four `.csproj` files (authors, description, version, license) | All | `●` |
| P-02 | Pack and publish `SharedKernel.Primitives` to feed | SharedKernel.Primitives | `●` |
| P-03 | Pack and publish `SharedKernel.Core` to feed | SharedKernel.Core | `●` |
| P-04 | Pack and publish `SharedKernel.Configuration` to feed | SharedKernel.Configuration | `●` |
| P-05 | Pack and publish `SharedKernel.FeatureManagement` to feed | SharedKernel.FeatureManagement | `●` |
| P-06 | Verify dependency graph in a consumer test project (Primitives → Core → Configuration chain) | All | `●` |
| P-07 | Add NuGet metadata to `SharedKernel.Guards.csproj` (authors, description, version, license) | SharedKernel.Guards | `●` |
| P-08 | Pack and publish `SharedKernel.Guards` to feed | SharedKernel.Guards | `●` |
| P-09 | Verify `SharedKernel.Guards` dependency graph in consumer: confirms Primitives + Core transitive refs resolve correctly | SharedKernel.Guards | `●` |
| P-10 | Add NuGet metadata to `SharedKernel.Cryptography.csproj` (authors, description, version, license, tags) | SharedKernel.Cryptography | `●` |
| P-11 | Pack and publish `SharedKernel.Cryptography` to feed | SharedKernel.Cryptography | `●` |
| P-12 | Verify `SharedKernel.Cryptography` dependency graph in a consumer test project (Primitives + Configuration transitive refs resolve) | SharedKernel.Cryptography | `●` |

---

## Phase: P-042 — Error.BusinessRule Factory <!-- phase-key: SK.01.P042 -->

> Additive extension to `SharedKernel.Primitives`: adds `ErrorType.BusinessRule` enum member, `Error.BusinessRule(string code, string message)` factory method, and `ErrorCodes.Domain` nested static class with `RuleViolated` constant.
> Motivation: `BusinessRuleViolationException` in `03.Domain` currently misclassifies domain rule violations as `ErrorType.Unexpected`, which causes wrong HTTP status mapping and false-positive alerts. This change provides the semantically correct error type.
> WO-010.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-17 | Add `BusinessRule` member to `ErrorType` enum; XML doc stating HTTP 422 mapping and domain-invariant-violation semantics | SharedKernel.Primitives | `●` |
| D-18 | Define `Error.BusinessRule(string code, string message)` factory method signature — consistent with existing factory pattern; returns `new Error(code, message, ErrorType.BusinessRule)` | SharedKernel.Primitives | `●` |
| D-19 | Define `ErrorCodes.Domain` nested static class inside `ErrorCodes`; constant `RuleViolated = "domain.rule.violated"` — prevents magic strings migrating between packages | SharedKernel.Primitives | `●` |
| C-30 | Implement `ErrorType.BusinessRule` enum member with XML doc | SharedKernel.Primitives | `●` |
| C-31 | Implement `Error.BusinessRule(string code, string message)` factory method on `Error` sealed record | SharedKernel.Primitives | `●` |
| C-32 | Implement `ErrorCodes.Domain` nested static class with `RuleViolated = "domain.rule.violated"` constant | SharedKernel.Primitives | `●` |
| T-19 | Unit: `Error.BusinessRule(...)` returns an `Error` with `Type == ErrorType.BusinessRule` | SharedKernel.Primitives.Tests | `●` |
| T-20 | Unit: `Error.BusinessRule(...)` is distinct from `Error.Unexpected` and `Error.Validation` by `ErrorType` | SharedKernel.Primitives.Tests | `●` |
| T-21 | Unit: `ErrorCodes.Domain.RuleViolated` is non-null and non-empty; value equals `"domain.rule.violated"` | SharedKernel.Primitives.Tests | `●` |
| T-22 | Regression: all existing `SharedKernel.Primitives` and `SharedKernel.Core` tests continue to pass after additive change | SharedKernel.Primitives.Tests, SharedKernel.Core.Tests | `●` |
| DO-07 | XML doc `ErrorType.BusinessRule` — state HTTP 422 mapping, domain-invariant-violation semantics, distinction from `Validation` and `Unexpected` | SharedKernel.Primitives | `●` |
| DO-08 | XML doc `Error.BusinessRule(string code, string message)` factory method | SharedKernel.Primitives | `●` |
| DO-09 | XML doc `ErrorCodes.Domain` nested class and `RuleViolated` constant | SharedKernel.Primitives | `●` |

---

## Phase: WO-033 Implementation — Cryptography Core/Tests/Docs/Published <!-- phase-key: SK.01.WO033Impl -->

> Execution-only phase: implements the `SharedKernel.Cryptography` contracts already locked in `01.Core/CLAUDE.md` by P-205/P-206 (Design + Scaffold). No new types, interfaces, or DI shapes are introduced here — this phase tracks P-207/P-208/P-209 against the pre-existing `Phase: Core` / `Phase: Tests` / `Phase: Docs` / `Phase: Published` task rows (C-33→C-38, T-23→T-28, DO-10→DO-11, P-10→P-12) so WO-033 progress is visible as a single work-order-scoped block.
> WO-033.

| ID | Task | Maps to | Package(s) | State |
|----|------|---------|-----------|:-----:|
| P-207 | Implement `Pbkdf2PasswordHasher`, `AesGcmEncryptionService`, `RsaSignatureService`, `EcdsaSignatureService`, `HmacSha256Signer`, `CryptoRandomGenerator`, `CryptographyOptions` validation, and `AddSharedKernelCryptography` DI extension — all five services registered as stateless thread-safe singletons | C-33, C-34, C-35, C-36, C-37, C-38 | SharedKernel.Cryptography | `●` |
| P-208 | Full unit-test coverage: hash/verify roundtrips, tamper/wrong-key detection, RSA+ECDSA signature roundtrips, HMAC tamper detection, secure-random output correctness, DI registration sanity + startup-validation failure | T-23, T-24, T-25, T-26, T-27, T-28 | SharedKernel.Cryptography.Tests | `●` |
| P-209 | XML docs on every public type, README usage examples, NuGet packaging metadata, `dotnet pack` zero-warning verification, consumer dependency-graph check, Package Board updated to `Published`/`●` | DO-10, DO-11, P-10, P-11, P-12 | SharedKernel.Cryptography | `●` |

---

## Phase: WO-034 — Generalize `IPasswordHasher` into a Secret-Agnostic One-Way Hashing Contract <!-- phase-key: SK.01.WO034 -->

> Rename-and-clarify of an existing, already-implemented capability — no new algorithm, no new DI shape, no new dependency. `IPasswordHasher` → `IOneWayHasher`, `Pbkdf2PasswordHasher` → `Pbkdf2OneWayHasher`, `PasswordVerificationResult` → `HashVerificationResult`; `Hash(string password)`/`Verify(string hash, string password)` → `Hash(string secret)`/`Verify(string hash, string secret)`. Same PBKDF2-HMACSHA256 mechanism, self-describing output, rehash-needed detection. Motivation: `01.Core` primitives must stay free of any single consuming-domain's vocabulary — an interface named `IPasswordHasher` leaked auth-domain vocabulary into a `01.Core` primitive, undermining the same design intent that kept Cryptography out of `12.Security` in WO-033. Caught before any consumer outside `SharedKernel.Cryptography` itself adopted the packed `1.0.0` build — a clean rename, not a deprecation cycle.
> WO-034.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-210 | Design: lock renamed contract — `IOneWayHasher` interface (`Hash(string secret)` / `Verify(string hash, string secret)`), `HashVerificationResult` enum (`Failed`/`Success`/`SuccessRehashNeeded`), `Pbkdf2OneWayHasher` implementation name; XML doc remarks state "password" is one example consumer among others (API key, recovery code); `AddSharedKernelCryptography` registration updated to renamed types; version bump to `2.0.0` planned | SharedKernel.Cryptography | `●` |
| P-211 | Scaffold: mechanically apply the P-210 rename across all production `.cs` files — rename `IPasswordHasher.cs`→`IOneWayHasher.cs`, `Pbkdf2PasswordHasher.cs`→`Pbkdf2OneWayHasher.cs`, `PasswordVerificationResult.cs`→`HashVerificationResult.cs`; update `CryptographyServiceCollectionExtensions` registration call and all `<see cref>` XML doc cross-references; `dotnet build` succeeds with 0 warnings / 0 errors; zero remaining references to the old names in production source | SharedKernel.Cryptography | `●` |
| P-212 | Tests: update `SharedKernel.Cryptography.Tests` hash/verify roundtrip, tamper, rehash-needed, and DI-registration tests to the renamed contract (all existing behavior preserved); add at least one new test hashing/verifying a non-password secret (e.g., an API key string) proving genuine generalization; update `SharedKernel.Consumer.Tests/ConsumerDependencyGraphTests.cs` to the renamed type; full `SharedKernel.Cryptography.Tests` suite passes with 0 failures | SharedKernel.Cryptography.Tests, SharedKernel.Consumer.Tests | `●` |
| P-213 | Docs + Published: update `01.Core/README.md` hashing example to the renamed contract, showing a password usage and a non-password (API key) usage side by side; re-pack `SharedKernel.Cryptography` at `2.0.0` with zero warnings; re-run consumer-verify to confirm `Primitives`+`Configuration` transitive chain still resolves; `01.Core/state-map.md` Package Board and root `state-map.md` Domain Summary Board updated to reflect the closed rename | SharedKernel.Cryptography | `●` |

---

## Cross-Domain Dependencies

_No active cross-domain dependencies. `01.Core` references nothing._

<!--
Format when active:
| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
-->

---

## Overall Progress

> Counts updated whenever a task state changes. Total tasks: 133 (126 base tasks + 3 WO-033 work-order tracking rows + 4 WO-034 work-order tracking rows; both work-order phases cross-reference/extend rather than duplicate existing C-/T-/DO-/P- task rows).

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.01.Design` | Design | 22 | 22 | 0 | `●` |
| `SK.01.Scaffold` | Scaffold | 18 | 18 | 0 | `●` |
| `SK.01.Core` | Core | 35 | 35 | 0 | `●` |
| `SK.01.Tests` | Tests | 24 | 24 | 0 | `●` |
| `SK.01.Docs` | Docs | 8 | 8 | 0 | `●` |
| `SK.01.Published` | Published | 12 | 12 | 0 | `●` |
| `SK.01.P042` | P-042 Error.BusinessRule Factory | 13 | 13 | 0 | `●` |
| `SK.01.WO033Impl` | WO-033 Cryptography Implementation (P-207/P-208/P-209) | 3 | 3 | 0 | `●` |
| `SK.01.WO034` | WO-034 IOneWayHasher Rename (P-210/P-211/P-212/P-213) | 4 | 4 | 0 | `●` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-14] Sub state-map initialized — phase key registry, all 6 phases scaffolded at ○ (49 tasks total)
- [2026-05-14] P-001 and P-002 processed — added D-11, D-12 (ValidationResult pair and ErrorCodes design tasks), C-14, C-15 (implementation tasks), T-10 (ValidationResult tests); updated D-06 and C-07 to reflect MapError and void Match on non-generic Result; total now 54 tasks
- [2026-05-14] D-01→D-12 → ● in SK.01.Design — all type shapes, interfaces, and DI signatures defined and implemented with 126 passing tests (state-map-phase)
- [2026-05-14] S-01→S-11 → ● in SK.01.Scaffold — NuGet refs, project refs, folder structure, slnx registration, and test stubs all confirmed complete (state-map-phase)
- [2026-05-14] C-01→C-15 → ● in SK.01.Core — all 15 implementation tasks complete, 128 tests passing across all four packages (state-map-phase)
- [2026-05-14] T-01→T-10 → ● in SK.01.Tests — 128 tests passing across all four packages, full coverage verified (state-map-phase)
- [2026-05-14] DO-01→DO-04 → ● in SK.01.Docs — XML docs complete on all public APIs; 01.Core/README.md written with railway, IClock, SmartEnum, options examples (state-map-phase)
- [2026-05-14] P-01→P-06 → ● in SK.01.Published — NuGet metadata added, all four packages packed to local feed, consumer verification project confirms dependency graph (state-map-phase)
- [2026-05-14] P-003 processed — added SharedKernel.Guards package: D-13→D-16 (design), S-12→S-15 (scaffold), C-16→C-29 (implementation), T-11→T-18 (tests), DO-05→DO-06 (docs), P-07→P-09 (publish); total now 87 tasks across 6 phases; all new tasks at ○
- [2026-05-15] D-13→D-16 → ● in SK.01.Design — IGuardClause, guard extensions, GuardDescriptions, Guard.Throw all defined; 145 tests passing (state-map-phase)
- [2026-05-15] S-12→S-15 → ● in SK.01.Scaffold — SharedKernel.Guards csproj, folders, test project, and slnx registration all complete (state-map-phase)
- [2026-05-15] C-16→C-29 → ● in SK.01.Core — SharedKernel.Guards fully implemented: IGuardClause, all guard extensions, GuardDescriptions, Guard.Throw; 145 tests passing (state-map-phase)
- [2026-05-15] T-11→T-18 → ● in SK.01.Tests — all Guards unit tests complete; 145 tests passing across functional and throw paths (state-map-phase)
- [2026-05-15] DO-05→DO-06 → ● in SK.01.Docs — XML docs complete on SharedKernel.Guards; Guards usage examples added to README (state-map-phase)
- [2026-05-15] P-07→P-09 → ● in SK.01.Published — SharedKernel.Guards NuGet metadata added, packed to local feed, consumer verification confirms transitive deps resolve (state-map-phase)
- [2026-05-27] P-042 added — Error.BusinessRule factory, ErrorType.BusinessRule enum member, ErrorCodes.Domain nested class; 13 tasks (D-17→D-19, C-30→C-32, T-19→T-22, DO-07→DO-09) added at ○; total 100 tasks (WO-010)
- [2026-05-27] D-17→D-19, C-30→C-32, T-19→T-22, DO-07→DO-09 → ● in SK.01.P042 — all 13 tasks complete; ErrorType.BusinessRule, Error.BusinessRule factory, ErrorCodes.Domain.RuleViolated implemented and tested (state-map-phase)
- [2026-06-26] WO-033 processed — added sixth package `SharedKernel.Cryptography` (dependency-free hashing/encryption/signing/secure-random primitives, decoupled from `12.Security`'s identity concerns): D-20→D-25 (design), S-16→S-18 (scaffold), C-33→C-38 (implementation, pending), T-23→T-28 (tests, pending), DO-10→DO-11 (docs, pending), P-10→P-12 (publish, pending); total now 126 tasks across 7 phases (arch-lead)
- [2026-06-26] D-20→D-25 → ● in SK.01.Design — `IPasswordHasher`/`PasswordVerificationResult`, `ISymmetricEncryptionService`/`EncryptedPayload`/`IEncryptionKeyProvider`/`CryptographicKey`, `IAsymmetricSignatureService`, `IHmacSigner`, `ISecureRandomGenerator`, and `CryptographyOptions`/`AddSharedKernelCryptography` contracts all locked in `01.Core/CLAUDE.md` (state-map-phase)
- [2026-06-26] S-16→S-18 → ● in SK.01.Scaffold — `SharedKernel.Cryptography.csproj` (references `SharedKernel.Primitives` + `SharedKernel.Configuration`) and `SharedKernel.Cryptography.Tests.csproj` created and registered in `Platform.SharedKernel.slnx`; both build clean with 0 warnings / 0 errors. `SK.01.Core`, `SK.01.Tests`, `SK.01.Docs`, and `SK.01.Published` regress from `●` to `◐` — each now carries pending Cryptography implementation tasks (state-map-phase)
- [2026-06-26] P-207/P-208/P-209 processed (WO-033 execution phase) — added `SK.01.WO033Impl` phase key cross-referencing existing C-33→C-38 (implement five services + DI extension), T-23→T-28 (full unit-test coverage), DO-10→DO-11 + P-10→P-12 (XML docs/README/NuGet packaging/consumer verification/Package Board closeout); no new types or contracts introduced — design was already locked by P-205/P-206; Package Board entry for `SharedKernel.Cryptography` updated from `Scaffold`/`◐` to `Core`/`◐`; total tracked rows now 129 (core-arch-planner)
- [2026-06-26] C-33→C-38 → ● in SK.01.Core — implemented `Pbkdf2PasswordHasher`, `AesGcmEncryptionService` (+`EncryptedPayload`/`IEncryptionKeyProvider`/`CryptographicKey`), `RsaSignatureService`/`EcdsaSignatureService` (+`IAsymmetricKeyProvider`), `HmacSha256Signer`, `CryptoRandomGenerator`, and `AddSharedKernelCryptography` DI extension (RSA default + keyed RSA/ECDSA registrations); SK.01.Core now fully `●` (35/35); P-207 → ● in SK.01.WO033Impl; 51 new Cryptography unit tests passing as part of implementation due diligence (T-23→T-28 remain officially owned by Phase: Tests / P-208) (core-phase-implementer)
- [2026-06-26] T-23→T-28 → ● in SK.01.Tests — verified existing 51 Cryptography tests already covered hash/verify roundtrip, tamper detection, unknown key id, multi-key-version decrypt, RSA/ECDSA roundtrip+wrong-key+tamper, HMAC roundtrip+tamper, DI registration sanity, and startup-validation failure; added 7 tests closing precise gaps (statistical non-repetition for `ISecureRandomGenerator` across 500 samples, never-all-zero check, explicit constant-time-comparison-shape tests for `IHmacSigner.Verify`, null-argument guards); 58/58 Cryptography tests passing; SK.01.Tests now fully `●` (24/24); P-208 → ● in SK.01.WO033Impl (core-phase-implementer)
- [2026-06-26] DO-10/DO-11 → ● in SK.01.Docs — verified all `SharedKernel.Cryptography` public types already carried complete XML docs from the Core implementation pass; confirmed via `dotnet build` with `GenerateDocumentationFile` newly enabled producing 0 warnings/0 errors (fixed one ambiguous-cref CS0419 on `Pbkdf2PasswordHasher`); added full Cryptography usage section to `01.Core/README.md` (password hashing, AES-256-GCM encrypt/decrypt, RSA/ECDSA signing via keyed services, HMAC signing, secure random/token generation); SK.01.Docs now fully `●` (8/8)
- [2026-06-26] P-10→P-12 → ● in SK.01.Published — added NuGet packaging metadata to `SharedKernel.Cryptography.csproj` (matching the Guards package convention); packed `SharedKernel.Cryptography.1.0.0.nupkg`/`.snupkg` into the root local feed (`./nupkgs/`); added `SharedKernel.Cryptography` package reference plus 5 new consumer-verification tests (DI registration sanity, password hash/verify roundtrip, HMAC roundtrip, secure random generation, AES-GCM encrypt/decrypt roundtrip) to `01.Core/SharedKernel.Consumer.Tests/`; all 42 consumer tests pass confirming Primitives + Configuration transitive chain resolves correctly; SK.01.Published now fully `●` (12/12)
- [2026-06-26] P-209 → ● in SK.01.WO033Impl — all sub-tasks (DO-10, DO-11, P-10, P-11, P-12) complete; WO-033 fully closed (P-207 ●, P-208 ●, P-209 ●); Package Board entry for `SharedKernel.Cryptography` updated from `Docs`/`◐` to `Published`/`●` (core-phase-implementer)
- [2026-06-26] WO-034 processed — added `SK.01.WO034` phase key and four new tasks (P-210→P-213) renaming `IPasswordHasher`/`Pbkdf2PasswordHasher`/`PasswordVerificationResult` to a secret-agnostic `IOneWayHasher`/`Pbkdf2OneWayHasher`/`HashVerificationResult` contract, with `Hash(string password)`/`Verify(string hash, string password)` generalized to `Hash(string secret)`/`Verify(string hash, string secret)`; rename-only — same PBKDF2-HMACSHA256 mechanism, output format, and rehash-needed detection; P-212 adds a non-password (API key) test case to prove genuine generalization, not cosmetic find-and-replace; P-213 re-packs at `2.0.0` (breaking public interface rename). Package Board entry for `SharedKernel.Cryptography` regressed from `Published`/`●` to `Published`/`◐` pending the rename; total tracked rows now 133 across 9 phase keys (core-arch-planner, WO-034)
- [2026-06-29] P-210→P-213 → ● in SK.01.WO034 — mechanical rename applied across all production `.cs` files (`IOneWayHasher`, `HashVerificationResult`, `Pbkdf2OneWayHasher`); 0 warnings/0 errors build; `SharedKernel.Cryptography.Tests` updated (59/59 passing, including new `Hash_ThenVerify_WithApiKeySecret_ReturnsSuccess` proving genuine secret-agnostic generalization); `ConsumerDependencyGraphTests` updated to renamed types; `01.Core/README.md` hashing section rewritten with password + API-key usage side by side; re-packed `SharedKernel.Cryptography.2.0.0` to local feed; consumer-verify re-run (42/42 passing) confirming Primitives+Configuration transitive chain still resolves; `SK.01.WO034` now fully `●` (4/4); Package Board entry for `SharedKernel.Cryptography` restored to `Published`/`●` at `2.0.0` (core-phase-implementer)
