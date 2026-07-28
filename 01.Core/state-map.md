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
| `SK.01.P230` | P-230 IHasSuccessFlag + IResultOfT\<T\> Application Seams | All tasks in Phase: P-230 are `●` | P-230 |
| `SK.01.P236` | P-236 Reflection-Free Failure-Factory Contract | All tasks in Phase: P-236 are `●` | P-236 |
| `SK.01.P249` | P-249 Logging EventId Range Registry | All tasks in Phase: P-249 are `●` | P-249 |
| `SK.01.P259` | P-259 Well-Known Cross-Domain Propagation Constants | All tasks in Phase: P-259 are `●` | P-259 |
| `SK.01.P292` | P-292 Result Exception-Boundary and Error-Aggregation Extensions | All tasks in Phase: P-292 are `●` | P-292 |
| `SK.01.P293` | P-293 Sequential Identifier Generation Primitive | All tasks in Phase: P-293 are `●` | P-293 |
| `SK.01.P294` | P-294 WellKnownTagKeys OTel Semantic Attribute Registry | All tasks in Phase: P-294 are `●` | P-294 |
| `SK.01.P295` | P-295 TimeProvider-Backed IClock Interop | All tasks in Phase: P-295 are `●` | P-295 |
| `SK.01.P296` | P-296 Non-Secret Content Hashing (IContentHasher) | All tasks in Phase: P-296 are `●` | P-296 |
| `SK.01.P297` | P-297 New SharedKernel.Compression Package | All tasks in Phase: P-297 are `●` | P-297 |
| `SK.01.P298` | P-298 Feature Flag Variant / Experimentation Support | All tasks in Phase: P-298 are `●` | P-298 |

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
| `SharedKernel.Primitives` | — | `○` | Zero external dependencies; carries `IHasSuccessFlag`/`IResultOfT<T>`/`IFailureFactory<TSelf>` reflection-free application seams (P-230, P-236); `WellKnownHeaders`/`WellKnownBaggageKeys` propagation constants design-locked, implementation pending (P-259, WO-042); `WellKnownTagKeys` (P-294), `IIdGenerator`/`UuidV7IdGenerator` (P-293), `SystemClock` `TimeProvider`-backed rewrite (P-295), and `ErrorCodes.Unexpected.Default` (P-292) all design-locked, implementation pending (WO-049) |
| `SharedKernel.Core` | — | `○` | References Primitives; `ResultTry`/`ResultCombine` exception-boundary and multi-result-aggregation extensions design-locked, implementation pending (P-292, WO-049) |
| `SharedKernel.Configuration` | — | `○` | References Primitives |
| `SharedKernel.FeatureManagement` | — | `○` | References Primitives; weighted-variant evaluation (`GetVariantAsync`/`FeatureVariant`/`FeatureVariantDefinition`) design-locked, implementation pending (P-298, WO-049) |
| `SharedKernel.Guards` | — | `○` | References Primitives + Core; two-path guard API |
| `SharedKernel.Cryptography` | Published | `●` | References Primitives + Configuration; zero third-party NuGet deps; WO-033 fully closed; WO-034 rename (`IPasswordHasher`→`IOneWayHasher`) fully closed — re-packed and published at `2.0.0`; P-296 (`IContentHasher`) implemented — `AddSharedKernelCryptography` now registers six singletons |
| `SharedKernel.Compression` | Published | `●` | **New seventh package (P-297, WO-049), fully shipped.** References Primitives + Configuration; zero third-party NuGet deps; `IPayloadCompressor`/`BrotliPayloadCompressor`/`GZipPayloadCompressor`/`CompressionOptions`/`AddSharedKernelCompression` implemented; 46/46 `SharedKernel.Compression.Tests` passing; packed to `./nupkgs` at `1.0.0`; consumer dependency-graph verified (46/46 `SharedKernel.Consumer.Tests`) |

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

## Phase: P-230 — IHasSuccessFlag Marker and IResultOfT\<T\> Interface for Reflection-Free Application Seams <!-- phase-key: SK.01.P230 -->

> Additive extension to `SharedKernel.Primitives`: adds two lightweight contracts that enable `05.Application` pipeline behaviors to inspect `Result<T>` / `Result` outcomes at the generic constraint level — without reflection, `dynamic`, or `Expression` tree compilation. Both interfaces are AOT-clean by construction.
> Motivation: `LoggingBehavior` cannot distinguish failure from success on an unknown `TResponse` without either a shared marker interface or runtime type inspection; `FailureResponseFactory` compiles an `Expression<Func<Error, TResponse>>` at warm-up time, which carries `[RequiresUnreferencedCode]`. These two interfaces resolve both hazards at the primitive layer so `05.Application` can reference them.
> WO-038.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-26 | Define `IHasSuccessFlag` — zero-member marker interface; `Result<T>` and `Result` (non-generic) both implement it; no properties, no methods; purpose is type-safe identity check via `is IHasSuccessFlag` without reflection; carries no `[RequiresUnreferencedCode]` annotation | SharedKernel.Primitives | `●` |
| D-27 | Define `IResultOfT<T>` — typed interface implemented by `Result<T>` only; exposes `IsSuccess → bool`, `IsFailure → bool`, `Value → T`; enables `where TResponse : IResultOfT<TResponse>` generic constraint in pipeline behaviors as a reflection-free substitute for `Expression`-compiled factory delegates; carries no `[RequiresUnreferencedCode]` annotation | SharedKernel.Primitives | `●` |
| C-39 | Implement `IHasSuccessFlag` as a public interface in `SharedKernel.Primitives`; apply `IHasSuccessFlag` to `Result<T>` (sealed class) and `Result` (readonly struct) — additive only, no existing member signatures change | SharedKernel.Primitives | `●` |
| C-40 | Implement `IResultOfT<T>` as a public interface in `SharedKernel.Primitives`; apply to `Result<T>` — `IsSuccess`, `IsFailure`, and `Value` satisfy the interface explicitly or implicitly; `Result` (non-generic) does NOT implement `IResultOfT<T>` (it has no typed value payload) | SharedKernel.Primitives | `●` |
| T-29 | Unit: `IHasSuccessFlag` — verify `Result<T>` and `Result` are assignable to `IHasSuccessFlag`; verify the flag reflects `IsSuccess`/`IsFailure` correctly for both success and failure instances | SharedKernel.Primitives.Tests | `●` |
| T-30 | Unit: `IResultOfT<T>` — verify `Result<T>` is assignable to `IResultOfT<T>`; verify `IsSuccess`, `IsFailure`, `Value` surface through the interface; verify `Result` (non-generic) is NOT assignable to `IResultOfT<T>`; verify accessing `Value` on a failure `IResultOfT<T>` throws `InvalidOperationException` (consistent with the concrete `Result<T>` contract) | SharedKernel.Primitives.Tests | `●` |
| DO-12 | XML doc `IHasSuccessFlag` — state purpose (pipeline-behavior type-safe success check without reflection or dynamic), which types implement it, and the AOT-clean guarantee | SharedKernel.Primitives | `●` |
| DO-13 | XML doc `IResultOfT<T>` — state purpose (reflection-free `FailureResponseFactory`-style construction via generic constraint), which type implements it (`Result<T>` only), note `Result` (non-generic) is excluded, and the AOT-clean guarantee | SharedKernel.Primitives | `●` |

---

## Phase: P-236 — Reflection-Free Failure-Factory Contract for Generic `Result<T>` Construction <!-- phase-key: SK.01.P236 -->

> Additive extension to `SharedKernel.Primitives`: adds a self-referential (CRTP) contract, `IFailureFactory<TSelf>`, built on a C# static abstract interface member, so a caller who knows only an open generic `TResponse` — never the inner `T` — can construct a failure `Result<T>` via a `where TResponse : IFailureFactory<TResponse>` compile-time constraint and a direct `TResponse.Failure(error)` call. `Result<T>` implements `IFailureFactory<Result<T>>` through its existing `Failure(Error error)` static factory (P-001/C-01) — no new member is added to `Result<T>`, no existing signature changes.
> Motivation: `05.Application.Behaviors`'s `FailureResponseFactory`/`ResultOfTDispatcher<TResponse>` was supposed to become reflection-free once `IResultOfT<T>` (P-230) shipped — P-232's acceptance criteria claimed exactly that. Reading the shipped code shows the replacement still calls `Type.GetInterfaces()`, `Type.MakeGenericType()`, `Type.GetMethod()`, and `MethodBase.Invoke()` to locate and invoke `Result<T>.Failure` — genuine reflection, cached per closed `TResponse` but never eliminated, and invisible to `00.Governance`'s SK0012 rule (which matches only the exact IL call target `MakeGenericMethod`, not `Type.MakeGenericType`/`GetMethod`/`Invoke`) despite being exactly the shape SK0012 exists to eliminate, and carrying no `[RequiresUnreferencedCode]` annotation despite being trimming-unsafe in the general case. `IFailureFactory<TSelf>` gives `05.Application` (P-237) the primitive needed to genuinely close this gap.
> This is additive to `IHasSuccessFlag`/`IResultOfT<T>` (P-230), not a replacement — those answer "read the outcome/value of a known-shape response"; this answers "construct a failure of an unknown `Result<T>` shape from just `TResponse`," which `IResultOfT<T>` cannot do because it is parameterized on the inner value type, not on itself.
> WO-039.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-28 | Define `IFailureFactory<TSelf>` — self-referential (CRTP) interface using a C# static abstract interface member: `static abstract TSelf Failure(Error error)`, constrained `where TSelf : IFailureFactory<TSelf>`; `Result<T>` implements `IFailureFactory<Result<T>>` via its existing `Failure(Error error)` static factory (no new member introduced); `Result` (non-generic) explicitly excluded, consistent with `IResultOfT<T>`'s exclusion rationale — callers needing a non-generic `Result` failure keep using the `TResponse == typeof(Result)` fast path in the consuming dispatcher; carries no `[RequiresUnreferencedCode]` annotation | SharedKernel.Primitives | `●` |
| C-41 | Implement `IFailureFactory<TSelf>` as a public interface in `SharedKernel.Primitives`; declare `Result<T> : IFailureFactory<Result<T>>` (alongside its existing `IHasSuccessFlag, IResultOfT<T>`) — the existing `public static Result<T> Failure(Error error)` factory satisfies the interface implicitly; `Result` (non-generic readonly struct) does NOT implement `IFailureFactory<Result>` | SharedKernel.Primitives | `●` |
| T-31 | Unit: `IFailureFactory<TSelf>` — a generic helper constrained `where TResponse : IFailureFactory<TResponse>` calls `TResponse.Failure(error)` and produces a correct failure instance for at least two distinct closed `Result<T>` shapes (e.g., `Result<int>`, `Result<string>`); test fixture demonstrates the dispatch compiles and executes via the static-abstract-member constraint with zero `System.Reflection` calls in the code path | SharedKernel.Primitives.Tests | `●` |
| T-32 | Unit: `Result` (non-generic) is NOT assignable to `IFailureFactory<Result>` — proves the deliberate exclusion, mirroring the `IResultOfT<T>` non-generic-`Result` exclusion coverage in T-30 | SharedKernel.Primitives.Tests | `●` |
| DO-14 | XML doc `IFailureFactory<TSelf>` — state purpose (reflection-free construction of a failure instance of an unknown `Result<T>` shape via a self-referential generic constraint; additive to, not a replacement for, `IResultOfT<T>`), which type implements it (`Result<T>` only), note `Result` (non-generic) is excluded, and the AOT-clean/zero-reflection guarantee | SharedKernel.Primitives | `●` |

---

## Phase: P-249 — Logging EventId Range Registry <!-- phase-key: SK.01.P249 -->

> Additive extension to `SharedKernel.Primitives`: adds `LoggingEventIdRanges`, a dependency-free, compile-time `const int` registry reserving a contiguous `Microsoft.Extensions.Logging` `EventId` numeric range for every capability domain in the root folder map (00 through 17), computed directly from each domain's two-digit folder number (`{domain number} * 1000` through `+999`). Ships with `DomainRangeWidth`(1000) and `PackageSubBlockWidth`(100) documentation constants describing the 100-wide-per-package sub-block convention a multi-package domain layers on top of its own reserved 1000-wide block — this registry enforces only the domain-level boundary, never the intra-domain sub-block assignment.
> Motivation: `EventId` numbering across the platform today is ad hoc and has already produced a real, confirmed collision — `SharedKernel.Caching.Redis.Core` and `SharedKernel.Caching.Redis.PubSub` both independently use `EventId` 4001/4002 for unrelated events, and these two packages are documented to run in the same process together. `01.Core` is the only domain reachable from every domain that currently logs (02, 05, 07, 11, 13, 14, 15 may all reference `01.Core` per the root layering table), making it the correct — and only architecturally legal — home for a cross-domain constant registry every future `[LoggerMessage]`-authoring package can consume without a future clean-up work order.
> Additive only — no existing `Result<T>`, `Error`, `IClock`, `SmartEnum<TEnum,TValue>`, guard, options, feature-flag, or cryptography member, interface, or DI shape changes. Zero new NuGet dependencies (pure `const int` fields, no `Microsoft.Extensions.Logging` package reference needed — `EventId` is a BCL-adjacent struct consumers construct themselves from the `int`).
> WO-041.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-29 | Define `LoggingEventIdRanges` static class shape — one public `const int` field per root folder-map domain (00.Governance through 17.Workflows), each value = `{two-digit domain number} * 1000`; plus `DomainRangeWidth = 1000` and `PackageSubBlockWidth = 100` documentation constants; XML doc remarks state the domain-level 1000-wide block rule and the recommended 100-wide per-package sub-block convention, with a worked example (`02.Caching`'s Redis role-split) | SharedKernel.Primitives | `●` |
| C-42 | Implement `LoggingEventIdRanges` in `SharedKernel.Primitives/Logging/LoggingEventIdRanges.cs` — all 18 domain `const int` fields plus the two width constants, matching the D-29 design exactly; no reflection, no runtime computation | SharedKernel.Primitives | `●` |
| T-33 | Unit: `LoggingEventIdRanges` — assert all 18 domain base constants are pairwise unique; assert each is a multiple of `DomainRangeWidth` (1000); assert each equals exactly `{domain-folder-number} * 1000` matching the root `CLAUDE.md` folder map (00 through 17) by name-to-number table; regression-confirm existing `SharedKernel.Primitives.Tests` still pass unchanged | SharedKernel.Primitives.Tests | `●` |
| DO-15 | XML doc `LoggingEventIdRanges` and every domain constant field — state the domain-level 1000-wide block rule, the 100-wide per-package sub-block convention with a worked multi-package example, and that this registry enforces only the domain-level boundary (not intra-domain sub-block collisions); add a short "EventId registry" usage example to `01.Core/README.md` showing a downstream package computing its own `EventId` (e.g. `LoggingEventIdRanges.Application + 42`) | SharedKernel.Primitives | `●` |

---

## Phase: P-259 — Well-Known Cross-Domain Propagation Constants (Headers + Baggage Keys) <!-- phase-key: SK.01.P259 -->

> Additive extension to `SharedKernel.Primitives`: adds `WellKnownHeaders` (HTTP/gRPC-metadata header name constants: `CorrelationId` = `"X-Correlation-Id"`, `TenantId` = `"X-Tenant-Id"`) and `WellKnownBaggageKeys` (`Activity` baggage / distributed-trace propagation key constants: `CorrelationId` = `"correlation.id"`) — a dependency-free, compile-time constant registry mirroring the `LoggingEventIdRanges` precedent (WO-041, P-249): a small, `01.Core`-hosted registry every layer is already permitted to reference.
> Motivation: a live, confirmed defect proves the risk of today's pattern — `14.Presentation`'s `CorrelationIdMiddleware` writes `Activity` baggage under `"correlation.id"` while `13.ServiceDefaults`'s `BaggageLogRecordProcessor` test suite independently hardcodes the literal `"CorrelationId"` for the same concept (flagged in `14.Presentation/CLAUDE.md`'s WO-041 changelog DO-07 but left unfixed for lack of a shared source of truth). The identical duplication pattern exists for the tenant/correlation _header_ names: `TenantIdDelegatingHandler.HeaderName` (`11.Communication.Rest`), `TenantIdInterceptor.TenantIdKey` (`11.Communication.Grpc`), and `HeaderTenantResolutionStrategy.DefaultHeaderName` (`13.ServiceDefaults.MultiTenancy`) are three independent declarations of `"x-tenant-id"`, kept in sync only by manual vigilance. `04.Contracts` was considered and rejected: `SharedKernel.Communication.Grpc.csproj` carries a hard, mechanically-enforced rule (P-163, `GrpcNeverReferencesContracts`) forbidding a `04.Contracts` reference — routing these constants through `04.Contracts` would force reintroducing exactly the coupling that rule exists to remove. `01.Core` carries no such constraint: every consumer (`11.Communication`, `13.ServiceDefaults`, `14.Presentation`, `07.Messaging`) already references it unconditionally.
> Pure promotion, zero behavioral change — values match today's de facto standard exactly. Consuming domains (`11.Communication`, `13.ServiceDefaults`, `14.Presentation`) retrofit their own local literals to reference these constants in their own follow-on phases; that retrofit work is out of `01.Core`'s jurisdiction and is not tracked here.
> WO-042.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-30 | Define `WellKnownHeaders` static class shape — `public const string CorrelationId = "X-Correlation-Id";` and `public const string TenantId = "X-Tenant-Id";`; XML doc remarks name the consuming types (`11.Communication.Rest.TenantIdDelegatingHandler`, `11.Communication.Grpc.TenantIdInterceptor`, `13.ServiceDefaults.MultiTenancy.HeaderTenantResolutionStrategy`) each constant is intended to replace. Define `WellKnownBaggageKeys` static class shape — `public const string CorrelationId = "correlation.id";`; XML doc remarks name `14.Presentation.CorrelationIdMiddleware` (writer) and `13.ServiceDefaults.BaggageLogRecordProcessor` (reader) as the two sides of the contract this constant reconciles | SharedKernel.Primitives | `●` |
| C-43 | Implement `WellKnownHeaders` and `WellKnownBaggageKeys` in `SharedKernel.Primitives/Propagation/` — `public const string` fields only, no `static readonly`, no computed values; matches the D-30 design exactly; no reflection, no runtime computation | SharedKernel.Primitives | `●` |
| T-34 | Unit: `WellKnownHeaders.CorrelationId` equals `"X-Correlation-Id"`; `WellKnownHeaders.TenantId` equals `"X-Tenant-Id"`; `WellKnownBaggageKeys.CorrelationId` equals `"correlation.id"` — pins every literal value so a future edit cannot silently drift it; regression-confirm existing `SharedKernel.Primitives.Tests` still pass unchanged | SharedKernel.Primitives.Tests | `●` |
| DO-16 | XML doc `WellKnownHeaders` and `WellKnownBaggageKeys` and every constant field — state which domains/types consume each constant today, and that this registry is the single authoritative source for cross-service propagation identifier literals platform-wide; add a short "Well-Known Propagation Constants" usage section to `01.Core/README.md` showing a downstream typed-client/middleware reading `WellKnownHeaders.TenantId` / `WellKnownBaggageKeys.CorrelationId` instead of a local literal | SharedKernel.Primitives | `●` |

---

## Phase: P-292 — Result Exception-Boundary and Error-Aggregation Railway Extensions <!-- phase-key: SK.01.P292 -->

> Additive extension to `SharedKernel.Core`'s existing `Result<T>` railway-extension surface (`Map`/`MapError`/`Bind`/`Match`/`Tap`): two new static classes, `ResultTry` (exception-boundary wrapping — sync `Try` and async `TryAsync`, converting any thrown exception, including a flattened `AggregateException`, into `Result<T>.Failure(Error.Unexpected(...))` rather than letting it propagate) and `ResultCombine` (`Combine` overloads for `params Result[]`/`IEnumerable<Result>` and a generic value-collecting `Result<T>` variant, folding a batch of independent outcomes into a single aggregate success or a `ValidationResult`/`ValidationResult<IReadOnlyList<T>>` carrying every collected `Error`, never short-circuiting on the first failure). A small additive `ErrorCodes.Unexpected.Default = "unexpected.exception"` constant is added to `SharedKernel.Primitives` as the default code `ResultTry` uses when the caller supplies no custom exception mapper.
> Motivation: every domain in the platform standardizes on `Result<T>`, but two everyday patterns still push developers toward hand-rolled code — wrapping a throwing third-party/BCL call, and combining several independent validation checks into one aggregate outcome. Both are solved once, centrally, in every mature Result/Railway-oriented library; their absence here is a real, repeated tax on every consuming service. `ResultTry.TryAsync` is the one documented, deliberate exception to this domain's existing "avoid async/await when only awaiting the input" railway rule (C-08's rule): catching an exception thrown during an awaited delegate requires the `try`/`catch` to wrap the `await` itself, which is impossible without a genuine async state machine.
> Pure additive — no change to `Result<T>`, `Result`, `ValidationResult`, `ValidationResult<T>`, `IHasSuccessFlag`, `IResultOfT<T>`, or `IFailureFactory<TSelf>`. No new NuGet dependency.
> WO-049.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-31 | Define `ResultTry` static class shape — `Try<T>(Func<T> operation)` and `Try<T>(Func<T> operation, Func<Exception, Error> onException)` returning `Result<T>`; `TryAsync<T>(Func<Task<T>> operation)` and the matching custom-mapper overload returning `Task<Result<T>>`; default exception-to-`Error` mapping uses `ErrorCodes.Unexpected.Default` and a message of the form `"{ExceptionType}: {ExceptionMessage}"`; an `AggregateException` is `.Flatten()`-ed before message construction so every inner exception is represented; never rethrows; never adds an `Exception` reference to `Error`'s equality-participating members | SharedKernel.Core | `●` |
| D-32 | Define `ResultCombine` static class shape — `Combine(params Result[] results)`, `Combine(IEnumerable<Result> results)` returning `ValidationResult`; `Combine<T>(params Result<T>[] results)`, `Combine<T>(IEnumerable<Result<T>> results)` returning `ValidationResult<IReadOnlyList<T>>`; evaluates every input (no short-circuit); all-success → `ValidationResult.Success()` / `ValidationResult<IReadOnlyList<T>>.Success(valuesInInputOrder)`; any failure → `.Failure(allFailingErrors)` carrying every failing `Error`, not just the first | SharedKernel.Core | `●` |
| D-33 | Define `ErrorCodes.Unexpected` nested static class addition — `Default = "unexpected.exception"` constant, consumed by `ResultTry`'s default (no-custom-mapper) exception-to-`Error` translation | SharedKernel.Primitives | `●` |
| C-44 | Implement `ResultTry.Try`/`TryAsync` (both overloads each) exactly per the D-31 design; `TryAsync` uses a genuine `async`/`await` body (the documented exception to the async-avoidance rule) | SharedKernel.Core | `●` |
| C-45 | Implement `ResultCombine.Combine` (all four overloads) exactly per the D-32 design | SharedKernel.Core | `●` |
| C-46 | Implement `ErrorCodes.Unexpected.Default = "unexpected.exception"` constant | SharedKernel.Primitives | `●` |
| T-35 | Unit: `ResultTry`/`ResultTry.TryAsync` — delegate success path returns `Result<T>.Success`; a thrown exception (plain, and a nested/flattened `AggregateException` from a `Task.Wait()`-style call) is translated to `Result<T>.Failure` with every inner exception represented in `Error.Message`; custom `onException` mapper overload is honored when supplied; the method never rethrows under any tested exception type | SharedKernel.Core.Tests | `●` |
| T-36 | Unit: `ResultCombine.Combine` — all-success (non-generic and generic) produces a successful `ValidationResult`/`ValidationResult<IReadOnlyList<T>>` with values preserved in input order; a single failure among mixed inputs surfaces exactly that one `Error`; an all-failure batch surfaces every failing `Error`, not just the first, for both the non-generic `Result` and generic `Result<T>` overloads | SharedKernel.Core.Tests | `●` |
| DO-17 | XML doc `ResultTry`, `ResultCombine`, and `ErrorCodes.Unexpected.Default`; add a "Result exception boundary and multi-result aggregation" usage section to `01.Core/README.md` showing a `ResultTry.Try`-wrapped third-party SDK call and a `ResultCombine.Combine` batch-validation example | SharedKernel.Core, SharedKernel.Primitives | `●` |

---

## Phase: P-293 — Sequential Identifier Generation Primitive <!-- phase-key: SK.01.P293 -->

> New `SharedKernel.Primitives` abstraction: `IIdGenerator` (`NewId() → Guid`) with a default sealed implementation, `UuidV7IdGenerator`, backed by `Guid.CreateVersion7()` (RFC 9562 UUID version 7) instead of the fully-random `Guid.NewGuid()` (UUID v4) that aggregate factories and other identifier call sites use today. Zero third-party dependency — pure BCL.
> Motivation: the platform is Postgres-first, and a fully-random v4 GUID as a clustered/primary-key index value is a well-documented Postgres/SQL Server performance anti-pattern (random B-tree insert points → page splits and fragmentation at scale). UUID v7 embeds a millisecond timestamp in its high bits, so values generated close together sort close together, restoring sequential-insert locality while keeping distributed, no-central-coordinator generation. Purely additive and opt-in — no existing identifier call site is forced to change.
> Ships with **no package-owned DI extension method** — `SharedKernel.Primitives` never references `Microsoft.Extensions.DependencyInjection.Abstractions` (zero-NuGet-dependency rule) and ships no DI extensions at all (existing rule, mirrors `IClock`). A consuming service registers `IIdGenerator` with a plain `services.AddSingleton<IIdGenerator, UuidV7IdGenerator>()` call at its own composition root — this is this phase's deliberate, jurisdiction-preserving reading of the "DI registration extension" requirement.
> Cross-referencing `03.Domain`'s `IAggregateFactory` guidance to mention this option is explicitly **out of `01.Core`'s jurisdiction** — flagged here for a future `domain-arch-planner` follow-up phase, not tracked as a task in this file. `03.Domain/CLAUDE.md` and `IAggregateFactory`'s own shipped contract are unchanged by this phase.
> WO-049.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-34 | Define `IIdGenerator` interface (`NewId() → Guid`) and `UuidV7IdGenerator` sealed implementation contract (backed by `Guid.CreateVersion7()`, stateless/thread-safe, no mutable state); document the plain-`AddSingleton` DI-registration pattern (no package-owned extension method) and the out-of-jurisdiction cross-reference note for `03.Domain`'s `IAggregateFactory` guidance | SharedKernel.Primitives | `●` |
| C-47 | Implement `IIdGenerator` and `UuidV7IdGenerator` exactly per the D-34 design | SharedKernel.Primitives | `●` |
| T-37 | Unit: `UuidV7IdGenerator` — uniqueness across a large generation batch (e.g. 10,000+ values, zero collisions); values generated in strict sequence compare as non-decreasing under the default `Guid` comparer (`CompareTo`/`<`); confirm `IIdGenerator` resolves via a plain `services.AddSingleton<IIdGenerator, UuidV7IdGenerator>()` registration with no package-owned extension needed | SharedKernel.Primitives.Tests | `●` |
| DO-18 | XML doc `IIdGenerator`/`UuidV7IdGenerator` explaining the Postgres clustered-index-locality rationale (why UUID v7 over v4) and the deliberate no-DI-extension design choice; add an "IIdGenerator — Time-Ordered Identifiers" usage section to `01.Core/README.md`; note (in this brain only, not in `03.Domain`) that a `03.Domain`/`domain-arch-planner` follow-up is expected to cross-reference this from `IAggregateFactory`'s guidance | SharedKernel.Primitives | `●` |

---

## Phase: P-294 — WellKnownTagKeys OTel Semantic Attribute Registry <!-- phase-key: SK.01.P294 -->

> A third compile-time `const string` registry in `SharedKernel.Primitives/Propagation/`, alongside `WellKnownHeaders` and `WellKnownBaggageKeys`: `WellKnownTagKeys`, covering `System.Diagnostics.Activity.SetTag(...)` attribute-key names expected to appear identically across every domain emitting OpenTelemetry spans — `TenantId` = `"tenant.id"`, `CorrelationId` = `"correlation.id"`, `ErrorType` = `"error.type"`, `ErrorCode` = `"error.code"`.
> Motivation: 00.Governance's SK0022 analyzer already recognizes `Activity.SetTag(...)` as a regulated magic-string call-site shape, but no shared tag-key registry exists for it to point at — unlike headers and baggage keys, which got their registries in WO-042 (P-259) only *after* a confirmed live mismatch had already occurred. Every domain emitting spans today (05.Application, 07.Messaging, 11.Communication, 13.ServiceDefaults, 14.Presentation, 17.Workflows) either re-declares its own tag-key literals or has none yet — the next domain to add span tagging is the next candidate for the identical drift `WellKnownBaggageKeys` was created to fix reactively. This phase closes the gap pre-emptively.
> Pure promotion/introduction — no existing domain's shipped `Activity.SetTag` call sites are changed by this phase; retrofitting consumers is each consuming domain's own follow-up responsibility, exactly as documented for `WellKnownHeaders`/`WellKnownBaggageKeys`. Root `CLAUDE.md`'s Magic String convention section update is **out of `01.Core`'s jurisdiction** (arch-lead/`sync-brain`) — not tracked as a task here.
> WO-049.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-35 | Define `WellKnownTagKeys` static class shape — `public const string TenantId = "tenant.id";`, `CorrelationId = "correlation.id";`, `ErrorType = "error.type";`, `ErrorCode = "error.code";`; XML doc remarks note this is a distinct call-site shape from `WellKnownBaggageKeys` (`Activity.SetTag` vs. `Activity.SetBaggage`/`AddBaggage`) even where a literal value coincides (`CorrelationId`); dotted-lowercase values chosen to match OpenTelemetry semantic-convention style | SharedKernel.Primitives | `●` |
| C-48 | Implement `WellKnownTagKeys` in `SharedKernel.Primitives/Propagation/` — `public const string` fields only, no `static readonly`, no computed values; matches the D-35 design exactly | SharedKernel.Primitives | `●` |
| T-38 | Unit: `WellKnownTagKeys.TenantId` equals `"tenant.id"`; `.CorrelationId` equals `"correlation.id"`; `.ErrorType` equals `"error.type"`; `.ErrorCode` equals `"error.code"` — pins every literal value; regression-confirm existing `SharedKernel.Primitives.Tests` still pass unchanged | SharedKernel.Primitives.Tests | `●` |
| DO-19 | XML doc `WellKnownTagKeys` and every constant field — state that this registry pre-emptively covers `Activity.SetTag(...)` call sites platform-wide and that no domain has adopted it yet; extend the "Well-Known Propagation Constants" usage section in `01.Core/README.md` to include a `WellKnownTagKeys` example; note (in this brain only) that the root `CLAUDE.md` Magic String convention section update is out of `01.Core`'s jurisdiction | SharedKernel.Primitives | `●` |

---

## Phase: P-295 — TimeProvider-Backed IClock Interop <!-- phase-key: SK.01.P295 -->

> `SystemClock`'s internal implementation is changed to source its `UtcNow` value from an injected `System.TimeProvider` (defaulting to `TimeProvider.System`) instead of calling `DateTimeOffset.UtcNow` directly, via a new `SystemClock(TimeProvider timeProvider)` constructor overload alongside the existing parameterless `SystemClock()`. `IClock`'s public contract (`UtcNow`, `Today`) does not change, and no code outside `SharedKernel.Primitives`'s own `SystemClock` implementation is given license to call `TimeProvider` directly.
> Motivation: `IClock` remains — and must remain — the only permitted source of time for domain/application logic (SK0001, unchanged). But `TimeProvider` is the BCL's own modern time abstraction, already preferred by Polly v8 resilience pipelines (`11.Communication`), `System.Threading.RateLimiting`, and `Task.Delay`/`CancellationTokenSource`. Today a service faking `IClock` for domain tests and separately faking time for a Polly retry-delay test has two independent, unsynchronized time sources. Making `SystemClock` a thin `TimeProvider` adapter lets one `TimeProvider`-based fake deterministically control both worlds in the same test, with zero change to the "IClock only" hard rule.
> Ships with **no package-owned DI extension method**, consistent with the `IIdGenerator` (P-293) precedent set in this same work order — a consuming service supplies its own `TimeProvider` via a plain `services.AddSingleton<TimeProvider>(...)` + `services.AddSingleton<IClock>(sp => new SystemClock(sp.GetRequiredService<TimeProvider>()))` pair at its own composition root.
> `16.Testing`'s `FakeClock` gaining a matching `TimeProvider`-exposing update, and any `00.Governance`/SK0001 analyzer touch-up, are explicitly **out of `01.Core`'s jurisdiction** — flagged here for `testing-arch-planner`/`governance-arch-planner` follow-up, not tracked as tasks in this file.
> Zero new NuGet dependency — `TimeProvider` has shipped in the BCL since .NET 8.
> WO-049.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-36 | Design `SystemClock`'s `TimeProvider`-backed internals — `SystemClock()` defaults to `TimeProvider.System`; `SystemClock(TimeProvider timeProvider)` accepts a caller-supplied provider; `UtcNow => _timeProvider.GetUtcNow()`; `Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime)`; `IClock`'s own interface members (`UtcNow`, `Today`) are unchanged; document the plain-`AddSingleton` DI pattern for a shared custom `TimeProvider`; note SK0001 behavior is unaffected (internal implementation detail, not an alternative time source for domain/application call sites) | SharedKernel.Primitives | `●` |
| C-49 | Implement `SystemClock`'s `TimeProvider`-backed internals exactly per the D-36 design | SharedKernel.Primitives | `●` |
| T-39 | Unit: parameterless `SystemClock()` reflects real wall-clock time (approximately `TimeProvider.System`'s current instant); `SystemClock(fakeTimeProvider)` reflects the injected fake's current instant, including correctly after the fake advances time; `Today` derives correctly from the same `TimeProvider`-sourced instant in both cases | SharedKernel.Primitives.Tests | `●` |
| DO-20 | XML doc `SystemClock`'s `TimeProvider`-adapter behavior and both constructors; add a "SystemClock — TimeProvider Interop" usage section to `01.Core/README.md` showing both the default and shared-custom-`TimeProvider` registration shapes; note (in this brain only) that a `16.Testing` `FakeClock` update and any SK0001 analyzer touch-up are out of `01.Core`'s jurisdiction | SharedKernel.Primitives | `●` |

---

## Phase: P-296 — Non-Secret Content Hashing (IContentHasher) in SharedKernel.Cryptography <!-- phase-key: SK.01.P296 -->

> New contract in `SharedKernel.Cryptography`, `IContentHasher`, distinct from `IOneWayHasher`: a fast, non-salted, non-iterated `SHA256`-backed digest over an arbitrary byte payload or stream (`ComputeHash(byte[])`, `ComputeHash(Stream)`, `ComputeHashAsync(Stream, CancellationToken)`), for content-fingerprinting use cases — object-storage ETags/checksums, content-addressable dedup keys, cache-key derivation from a payload body. `ContentHasherExtensions` adds convenience hex/Base64 string-encoding on top of the minimal interface.
> Motivation: `IOneWayHasher` is deliberately slow (600,000 PBKDF2 iterations) to resist brute-force attacks on secrets — exactly the wrong tool, both performance-wise and semantically, for hashing a 50MB upload to compute its ETag. Today there is no sanctioned path for that: a consuming service either misuses `IOneWayHasher` or hand-rolls `SHA256.HashData(...)` directly, which is exactly the "hand-rolled cryptography" pattern this domain already prohibits for secrets but has never addressed for the equally common non-secret case. `08.Storage`'s checksum/ETag needs and any future `09.Search`/`10.Intelligence` content-dedup work are the concrete, named consumers this closes a real gap for.
> `AddSharedKernelCryptography` now registers **six** singletons instead of five. The pre-existing hard rule "no raw SHA256/MD5 secret hashing" is amended to scope it to secret hashing only and cross-reference `IContentHasher` as the sanctioned non-secret path — already applied to `01.Core/CLAUDE.md` in this pass.
> Zero third-party NuGet dependency; references only the existing `SharedKernel.Cryptography` dependency shape (`SharedKernel.Primitives` + `SharedKernel.Configuration`).
> WO-049.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-37 | Define `IContentHasher` interface (`ComputeHash(byte[])`, `ComputeHash(Stream)`, `ComputeHashAsync(Stream, CancellationToken = default)` all returning raw digest bytes/`ValueTask<byte[]>`); define `ContentHasherExtensions` static class (`ComputeHashHex`, `ComputeHashBase64` extension methods built on `ComputeHash(byte[])`); define `Sha256ContentHasher` sealed implementation contract backed by `SHA256.HashData`/`.HashDataAsync`; XML-doc-distinguish from `IOneWayHasher` explicitly (never for secrets) | SharedKernel.Cryptography | `●` |
| C-50 | Implement `Sha256ContentHasher` and `ContentHasherExtensions` exactly per the D-37 design; register `IContentHasher` in `AddSharedKernelCryptography` as the sixth singleton (stateless, thread-safe) | SharedKernel.Cryptography | `●` |
| T-40 | Unit: `IContentHasher` — deterministic digest for identical input across repeated calls; a single-byte change in input produces a different digest; `ComputeHash(Stream)`/`ComputeHashAsync(Stream,...)` produce byte-identical output to `ComputeHash(byte[])` for the same content; `ComputeHashHex`/`ComputeHashBase64` produce correctly-encoded strings of the underlying digest; DI registration sanity confirms `IContentHasher` resolves alongside the other five `AddSharedKernelCryptography` services | SharedKernel.Cryptography.Tests | `●` |
| DO-21 | XML doc `IContentHasher`/`Sha256ContentHasher`/`ContentHasherExtensions` with the explicit "never for secrets, use IOneWayHasher instead" distinction; add an "IContentHasher — Non-Secret Content Fingerprinting" usage section to `01.Core/README.md` (ETag/checksum example) placed alongside the existing hashing section for contrast with `IOneWayHasher` | SharedKernel.Cryptography | `●` |

---

## Phase: P-297 — New SharedKernel.Compression Package <!-- phase-key: SK.01.P297 -->

> A **seventh `01.Core` package**, `SharedKernel.Compression`, mirroring `SharedKernel.Cryptography`'s established shape exactly: a single package (no `.Abstractions`/`.{Provider}` split — the same precedent already applied to Cryptography's RSA/ECDSA split via keyed DI rather than sibling packages) exposing `IPayloadCompressor` for generic compress/decompress of an arbitrary byte payload or stream, backed by `System.IO.Compression.BrotliStream` (unkeyed default + "Brotli"-keyed singleton) and `GZipStream` ("GZip"-keyed singleton only). `Decompress` returns `Result<byte[]>` (byte[] overload) / `Result` (stream overload) rather than throwing on a corrupt/truncated input, mirroring `ISymmetricEncryptionService.Decrypt`'s existing failure-handling shape. A minimal `CompressionOptions.Level` (default `CompressionLevel.Optimal`), validated via `SharedKernel.Configuration`'s `AddValidatedOptions`, mirrors `CryptographyOptions`'s single-global-knob shape.
> Motivation: the direct sibling of the already-established "general-purpose encrypt/decrypt of arbitrary payloads... distinct from EF Core column-level encryption" pattern — same shape, same zero-dependency BCL-only constraint, orthogonal concern. Every infrastructure-facing domain needs this (`07.Messaging` shrinking large message bodies before publish, `08.Storage` compressing before upload, `11.Communication` compressing large request/response bodies, `10.Intelligence`/`09.Search` compressing large document payloads before indexing) and none of them should each reinvent it. Compression and encryption are frequently combined — the correct order is always compress-THEN-encrypt, never the reverse (compressing already-encrypted/high-entropy ciphertext wastes CPU for no size benefit) — and `IPayloadCompressor`'s XML docs must say so explicitly.
> Full package lifecycle tracked in this one phase section (Design + Scaffold + Core + Tests + Docs + Published), mirroring how WO-033 first introduced `SharedKernel.Cryptography`. References only `SharedKernel.Primitives` + `SharedKernel.Configuration`, zero third-party NuGet dependencies. Packages table, Technology Stack table, and Package Board already updated to seven packages in this pass.
> WO-049.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-38 | Define `IPayloadCompressor` interface — `Compress(byte[] data) → byte[]`; `Compress(Stream input, Stream output) → void`; `CompressAsync(Stream input, Stream output, CancellationToken = default) → Task`; `Decompress(byte[] compressed) → Result<byte[]>`; `Decompress(Stream input, Stream output) → Result`; `DecompressAsync(Stream input, Stream output, CancellationToken = default) → Task<Result>`; XML doc states the compress-then-encrypt ordering rule and warns against compressing already-encrypted/already-compressed payloads | SharedKernel.Compression | `●` |
| D-39 | Define `BrotliPayloadCompressor` (default) and `GZipPayloadCompressor` (keyed alternate) implementation contracts; define the keyed-DI pattern mirroring `RsaSignatureServiceKey`/`EcdsaSignatureServiceKey` — `CompressionServiceCollectionExtensions.BrotliPayloadCompressorKey` ("Brotli"), `GZipPayloadCompressorKey` ("GZip"); Brotli registered both unkeyed-default and "Brotli"-keyed, GZip keyed-only (mirrors Ecdsa's keyed-only registration) | SharedKernel.Compression | `●` |
| D-40 | Define `CompressionOptions` shape (`.Level` → `System.IO.Compression.CompressionLevel`, default `Optimal`) and `AddSharedKernelCompression(IConfiguration configuration)` DI extension signature | SharedKernel.Compression | `●` |
| S-19 | Create `01.Core/SharedKernel.Compression/SharedKernel.Compression.csproj` targeting `net10.0`; add project references to `SharedKernel.Primitives` and `SharedKernel.Configuration`; create nested `SharedKernel.Compression.Tests.csproj` with xUnit reference; register both in `Platform.SharedKernel.slnx` under solution folder `01.Core` | SharedKernel.Compression | `●` |
| C-51 | Implement `IPayloadCompressor`, `BrotliPayloadCompressor`, and `GZipPayloadCompressor` exactly per the D-38/D-39 design — corrupt/truncated input caught (`InvalidDataException`) and mapped to `Result`/`Result<byte[]>` failure, never thrown directly | SharedKernel.Compression | `●` |
| C-52 | Implement `CompressionOptions` validation and `AddSharedKernelCompression` DI extension — registers `CompressionOptions` (validated, `ValidateOnStart`), `BrotliPayloadCompressor` as both unkeyed default and "Brotli"-keyed singleton, `GZipPayloadCompressor` as "GZip"-keyed singleton only | SharedKernel.Compression | `●` |
| T-41 | Unit: roundtrip for both `BrotliPayloadCompressor` and `GZipPayloadCompressor` (byte[] and stream overloads, sync and async); a corrupted/truncated compressed input surfaces as a `Result`/`Result<byte[]>` failure, never an unhandled exception; streaming vs. in-memory overloads produce byte-identical decompressed output; DI registration sanity for `AddSharedKernelCompression` (unkeyed Brotli default resolves, both keyed singletons resolve, invalid `CompressionOptions` throws at `IHost.StartAsync()`) | SharedKernel.Compression.Tests | `●` |
| DO-22 | XML doc all public `SharedKernel.Compression` types, explicitly stating the compress-then-encrypt ordering rule and warning against double-compression; write `01.Core/README.md` Compression usage section (Brotli default + explicit GZip-keyed-resolution example) | SharedKernel.Compression | `●` |
| P-13 | Add NuGet metadata to `SharedKernel.Compression.csproj` (authors, description, version, license, tags) matching the existing six-package convention | SharedKernel.Compression | `●` |
| P-14 | Pack and publish `SharedKernel.Compression` to the local feed | SharedKernel.Compression | `●` |
| P-15 | Verify `SharedKernel.Compression` dependency graph in a consumer test project (Primitives + Configuration transitive refs resolve) | SharedKernel.Compression | `●` |

---

## Phase: P-298 — Feature Flag Variant / Experimentation Support <!-- phase-key: SK.01.P298 -->

> Extends `SharedKernel.FeatureManagement`'s `IFeatureManager` abstraction with weighted, named feature *variants* — not just on/off flags — bridging `Microsoft.FeatureManagement`'s existing variant/allocation support the same way `IsEnabledAsync` already bridges plain boolean evaluation. `GetVariantAsync`/`GetVariantAsync<TContext>` return a neutral `FeatureVariant` record (`.Name`, `.Configuration`); a new `FeatureVariantDefinition` sibling record (`.Name`, `.Weight`, `.Configuration`) extends the definitions API for weighted-allocation modeling.
> Motivation: at platform scale, boolean feature flags stop being enough — teams need percentage-based gradual rollouts and A/B experimentation, and `Microsoft.FeatureManagement` (already wrapped by this domain) has shipped first-class variant/allocation support for several major versions. Today a service wanting variant-based rollout must punch through the abstraction and depend on `Microsoft.FeatureManagement` directly — precisely the escape hatch this domain's existing hard rule ("`IFeatureManager` is the only permitted feature-flag interface... never inject `Microsoft.FeatureManagement.IFeatureManager` directly") forbids but previously could not avoid for this one legitimate use case.
> Purely additive — the existing boolean `IsEnabledAsync` surface and behavior are completely unchanged. `AddSharedKernelFeatureManagement` wiring covers the variant path with no additional required configuration beyond what `Microsoft.FeatureManagement` itself needs. Any AOT gap found in the variant API surface specifically is to be flagged, not treated as a blocker (mirrors this domain's existing "verify AOT status on each major upgrade" posture for `Microsoft.FeatureManagement`).
> WO-049.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-41 | Define `IFeatureManager.GetVariantAsync(string feature, CancellationToken ct)` / `GetVariantAsync<TContext>(string feature, TContext ctx, CancellationToken ct)` returning `FeatureVariant`; define `FeatureVariant` sealed record (`.Name → string`, `.Configuration → string?`) with a documented deterministic fallback for an unconfigured/unresolvable feature; define `FeatureVariantDefinition` sealed record (`.Name → string`, `.Weight → int`, `.Configuration → string?`) as a sibling to `FeatureDefinition`; no `Microsoft.FeatureManagement` type may appear on `IFeatureManager`'s public surface | SharedKernel.FeatureManagement | `●` |
| C-53 | Implement the variant evaluation adapter (wraps `Microsoft.FeatureManagement`'s `IVariantFeatureManager`), `FeatureVariant`, and `FeatureVariantDefinition` exactly per the D-41 design | SharedKernel.FeatureManagement | `●` |
| C-54 | Update `AddSharedKernelFeatureManagement` wiring to register/resolve the variant path — no additional required configuration beyond `Microsoft.FeatureManagement`'s own variant/allocation schema | SharedKernel.FeatureManagement | `●` |
| T-42 | Unit: `GetVariantAsync` — deterministic variant assignment given a fixed context/seed (repeated calls with the same context return the same variant); an unconfigured feature falls back predictably (documented default, never throws); regression test confirms the existing boolean `IsEnabledAsync` surface and behavior are byte-for-byte unchanged | SharedKernel.FeatureManagement.Tests | `●` |
| DO-23 | XML doc `GetVariantAsync`, `FeatureVariant`, `FeatureVariantDefinition`; add a "Feature Variants — Gradual Rollout" usage section to `01.Core/README.md` showing a percentage-based enablement scenario across tenants; note any discovered `Microsoft.FeatureManagement` variant-API AOT gap as a flagged (non-blocking) caveat | SharedKernel.FeatureManagement | `●` |

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

> Counts updated whenever a task state changes. Total tasks: 195 (154 prior tasks + 41 new WO-049 tasks across P-292/P-293/P-294/P-295/P-296/P-297/P-298: D-31→D-41, S-19, C-44→C-54, T-35→T-42, DO-17→DO-23, P-13→P-15).

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
| `SK.01.P230` | P-230 IHasSuccessFlag + IResultOfT\<T\> Application Seams | 8 | 8 | 0 | `●` |
| `SK.01.P236` | P-236 Reflection-Free Failure-Factory Contract | 5 | 5 | 0 | `●` |
| `SK.01.P249` | P-249 Logging EventId Range Registry | 4 | 4 | 0 | `●` |
| `SK.01.P259` | P-259 Well-Known Cross-Domain Propagation Constants | 4 | 4 | 0 | `●` |
| `SK.01.P292` | P-292 Result Exception-Boundary and Error-Aggregation Extensions | 9 | 9 | 0 | `●` |
| `SK.01.P293` | P-293 Sequential Identifier Generation Primitive | 4 | 4 | 0 | `●` |
| `SK.01.P294` | P-294 WellKnownTagKeys OTel Semantic Attribute Registry | 4 | 4 | 0 | `●` |
| `SK.01.P295` | P-295 TimeProvider-Backed IClock Interop | 4 | 4 | 0 | `●` |
| `SK.01.P296` | P-296 Non-Secret Content Hashing (IContentHasher) | 4 | 4 | 0 | `●` |
| `SK.01.P297` | P-297 New SharedKernel.Compression Package | 11 | 11 | 0 | `●` |
| `SK.01.P298` | P-298 Feature Flag Variant / Experimentation Support | 5 | 5 | 0 | `●` |

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
- [2026-07-01] P-230 processed (WO-038) — added `SK.01.P230` phase key and 8 new tasks (D-26/D-27 design, C-39/C-40 implementation, T-29/T-30 tests, DO-12/DO-13 docs) for `IHasSuccessFlag` zero-member marker interface (implemented by `Result<T>` and `Result`) and `IResultOfT<T>` typed interface (implemented by `Result<T>` only); both contracts are AOT-clean by construction — no `[RequiresUnreferencedCode]`, no reflection, no Expression trees; purpose: unblock `05.Application.Behaviors` from reflection-based outcome detection in `LoggingBehavior` and `Expression`-compiled `FailureResponseFactory`; additive-only, no existing `Result<T>` or `Result` member signatures change; total tasks now 141 (core-arch-planner, WO-038)
- [2026-07-02] D-26/D-27/C-39/C-40/T-29/T-30/DO-12/DO-13 → ● in SK.01.P230 — IHasSuccessFlag and IResultOfT<T> implemented, tested, and documented; 56/56 tests passing (state-map-phase)
- [2026-07-03] P-236 processed (WO-039) — added `SK.01.P236` phase key and 5 new tasks (D-28 design, C-41 implementation, T-31/T-32 tests, DO-14 docs) for `IFailureFactory<TSelf>`, a self-referential (CRTP) contract built on a C# static abstract interface member (`static abstract TSelf Failure(Error error)`, constrained `where TSelf : IFailureFactory<TSelf>`); `Result<T>` implements `IFailureFactory<Result<T>>` through its existing `Failure(Error error)` static factory — no new member, no signature change; `Result` (non-generic) deliberately excluded, consistent with `IResultOfT<T>`'s exclusion. Motivation: `05.Application`'s `FailureResponseFactory`/`ResultOfTDispatcher<TResponse>` (P-232, WO-038) claimed to have eliminated reflection via `IResultOfT<T>` but still calls `Type.GetInterfaces()`/`MakeGenericType()`/`GetMethod()`/`Invoke()` — invisible to governance's SK0012 rule but exactly the shape it exists to eliminate; this phase gives `05.Application` (P-237) the primitive needed to genuinely close the gap. Additive only — `IHasSuccessFlag`, `IResultOfT<T>`, and every existing `Result<T>`/`Result` member signature unchanged. Total tasks now 146 (core-arch-planner, WO-039)
- [2026-07-03] D-28/C-41/T-31/T-32/DO-14 → ● in SK.01.P236 — implemented `IFailureFactory<TSelf>` (`SharedKernel.Primitives/Results/IFailureFactory.cs`); `Result<T>` now declares `IFailureFactory<Result<T>>` alongside `IHasSuccessFlag, IResultOfT<T>`, satisfied implicitly by its pre-existing `Failure(Error)` factory; `Result` (non-generic) confirmed excluded — `IFailureFactory<Result>` is unnameable at compile time since `Result` doesn't satisfy the interface's own `where TSelf : IFailureFactory<TSelf>` constraint, proven via reflection over `Result`'s declared interfaces rather than a direct `is` check. Added 8 tests to `ResultInterfaceTests.cs` (dispatch for `Result<int>`/`Result<string>`, assignability, exclusion proof). While wiring these in, found and fixed a pre-existing broken test (`IResultOfT_GenericConstraintPattern_WorksWithoutReflection`, committed in 5e9ed3a) that used an unsatisfiable self-referential constraint (`where TResponse : IResultOfT<TResponse>`) against `Result<T> : IResultOfT<T>`, which was blocking `SharedKernel.Primitives.Tests` from compiling at all; corrected to the two-type-parameter form (`where TResponse : IResultOfT<TValue>`) matching `IResultOfT<T>`'s actual (non-self-referential) shape. 74/74 `SharedKernel.Primitives.Tests` passing; `SK.01.P236` now fully `●` (5/5); propagated to root `state-map.md` (core-phase-implementer)
- [2026-07-08] P-249 processed (WO-041) — added `SK.01.P249` phase key and 4 new tasks (D-29 design, C-42 implementation, T-33 tests, DO-15 docs) for `LoggingEventIdRanges`, a dependency-free `const int` registry reserving one 1000-wide `EventId` block per root folder-map domain (00.Governance=0 through 17.Workflows=17000, each = `{domain number} * 1000`), plus `DomainRangeWidth`(1000)/`PackageSubBlockWidth`(100) constants documenting the 100-wide per-package sub-block convention multi-package domains layer on top. Motivation: a confirmed live `EventId` collision (`Caching.Redis.Core` vs `Redis.PubSub`, both using 4001/4002) plus the complete absence of a platform-wide `EventId` coordination mechanism; `01.Core` is the only domain reachable from every domain that logs today (02, 05, 07, 11, 13, 14, 15 per the root layering table), making `SharedKernel.Primitives` the correct — and only architecturally legal — home. Additive only — no existing `Result<T>`/`Error`/`IClock`/`SmartEnum`/guard/options/feature-flag/cryptography member or DI shape changes; zero new NuGet dependencies. Total tasks now 150 (core-arch-planner, WO-041)
- [2026-07-09] D-29/C-42/T-33/DO-15 → ● in SK.01.P249 — implemented `LoggingEventIdRanges` (`SharedKernel.Primitives/Logging/LoggingEventIdRanges.cs`) with all 18 domain `const int` fields plus `DomainRangeWidth`/`PackageSubBlockWidth`; added `LoggingEventIdRangesTests` (uniqueness, multiple-of-1000, name-to-folder-number theory across all 18 domains); added README "LoggingEventIdRanges — EventId Registry" section with a `LoggingEventIdRanges.Application + 42` usage example. 114/114 `SharedKernel.Primitives.Tests` passing, 0 build warnings; `SK.01.P249` now fully `●` (4/4); propagated to root `state-map.md` (core-phase-implementer)
- [2026-07-14] P-259 processed (WO-042) — added `SK.01.P259` phase key and 4 new tasks (D-30 design, C-43 implementation, T-34 tests, DO-16 docs) for `WellKnownHeaders` (`CorrelationId`="X-Correlation-Id", `TenantId`="X-Tenant-Id") and `WellKnownBaggageKeys` (`CorrelationId`="correlation.id") — dependency-free `const string` registries mirroring the `LoggingEventIdRanges` precedent. Motivation: a confirmed live defect (`14.Presentation.CorrelationIdMiddleware` writes `Activity` baggage under `"correlation.id"` while `13.ServiceDefaults.BaggageLogRecordProcessor`'s test suite independently hardcodes `"CorrelationId"` for the same concept — flagged in WO-041 DO-07, left unfixed for lack of a shared source of truth) plus three independent redeclarations of the `"x-tenant-id"` header across `11.Communication.Rest`, `11.Communication.Grpc`, and `13.ServiceDefaults.MultiTenancy`; `04.Contracts` was considered and rejected as the home because `SharedKernel.Communication.Grpc` carries a hard governance rule (P-163) forbidding a `04.Contracts` reference. Pure promotion of existing de facto literal values — zero behavioral change, zero new NuGet dependencies. Consuming-domain retrofits (pointing existing literals at these constants) are out of `01.Core`'s jurisdiction and tracked by each consuming domain's own planner. D-30 design locked in `01.Core/CLAUDE.md` this pass; C-43/T-34/DO-16 remain `○` pending implementation. Total tasks now 154 (core-arch-planner, WO-042)
- [2026-07-14] C-43/T-34/DO-16 → ● in SK.01.P259 — implemented `WellKnownHeaders`/`WellKnownBaggageKeys` (`SharedKernel.Primitives/Propagation/`); added `WellKnownPropagationConstantsTests` pinning all 3 literals; added README "Well-Known Propagation Constants" usage section. 117/117 `SharedKernel.Primitives.Tests` passing; `SK.01.P259` now fully `●` (4/4); propagated to root `state-map.md` (core-phase-implementer)
- [2026-07-27] WO-049 processed — seven new phases added in a single pass, all against pre-existing `01.Core` capability seams, none touching outside `01.Core`: P-292 (`ResultTry`/`ResultCombine` railway extensions + `ErrorCodes.Unexpected.Default`, `SharedKernel.Core`/`SharedKernel.Primitives`), P-293 (`IIdGenerator`/`UuidV7IdGenerator`, `SharedKernel.Primitives`), P-294 (`WellKnownTagKeys`, `SharedKernel.Primitives`), P-295 (`SystemClock`'s `TimeProvider`-backed internals, `SharedKernel.Primitives`), P-296 (`IContentHasher`, `SharedKernel.Cryptography`), P-297 (new seventh package `SharedKernel.Compression`), P-298 (feature-flag variant/experimentation support, `SharedKernel.FeatureManagement`). Design (`D-31`→`D-41`, 11 tasks) locked in `01.Core/CLAUDE.md` this pass and marked `●`; all Scaffold/Core/Tests/Docs/Published tasks for these seven phases (`S-19`; `C-44`→`C-54`; `T-35`→`T-42`; `DO-17`→`DO-23`; `P-13`→`P-15`; 30 tasks) remain `○` pending a future implementation pass. Two deliberate jurisdiction boundaries held throughout: (1) `IIdGenerator` (P-293) and `SystemClock`'s `TimeProvider` overload (P-295) ship with **no package-owned DI extension** — both are registered via a plain `services.AddSingleton<...>()` call at the consumer's own composition root, preserving `SharedKernel.Primitives`' zero-NuGet-dependency and no-DI-extension rules rather than adding a `Microsoft.Extensions.DependencyInjection.Abstractions` reference; (2) cross-references into `03.Domain` (`IAggregateFactory`), `16.Testing` (`FakeClock`), `00.Governance` (SK0001/SK0022), and the root `CLAUDE.md` Magic String section are explicitly flagged as other agents' follow-up work, never edited here. Phase Key Registry gained 7 new keys (`SK.01.P292`→`SK.01.P298`); Package Board gained a new `SharedKernel.Compression` row at `Design`/`○` and annotated the `Primitives`/`Core`/`FeatureManagement`/`Cryptography` rows with their new design-locked seams; total tasks now 195 (154 + 41) (core-arch-planner, WO-049)
- [2026-07-27] C-44/C-45/C-46/T-35/T-36/DO-17 → ● in SK.01.P292 — implemented `ResultTry` (`Try`/`TryAsync`, each with default and custom-mapper overloads; `TryAsync` a genuine `async`/`await` body per the documented railway-rule exception) and `ResultCombine` (`Combine` — params + `IEnumerable` for both non-generic `Result` and generic `Result<T>`, no short-circuit, every failing `Error` surfaced) in `SharedKernel.Core/Extensions/`; changed `ErrorCodes.Unexpected.Default` from `"unexpected.default"` to `"unexpected.exception"` per the D-33 design (confirmed no shipped `.cs` file elsewhere in the repo referenced the old literal). Added 25 new tests (`ResultTryTests`, `ResultCombineTests`) plus one pinned-value regression test for the changed `ErrorCodes.Unexpected.Default` literal. Added a "Result Exception Boundary and Multi-Result Aggregation" usage section to `01.Core/README.md` and synced `SharedKernel.Core/README.md`'s Included Types list. `SharedKernel.Primitives.Tests` 118/118, `SharedKernel.Core.Tests` 95/95, both Release config, 0 regressions; `SK.01.P292` now fully `●` (9/9); propagated to root `state-map.md` (core-phase-implementer)
- [2026-07-27] C-47/T-37/DO-18 → ● in SK.01.P293 — implemented `IIdGenerator`/`UuidV7IdGenerator` (`SharedKernel.Primitives/Identifiers/`), backed by `Guid.CreateVersion7()`, no package-owned DI extension (plain `services.AddSingleton<IIdGenerator, UuidV7IdGenerator>()` at the consumer's composition root). Added `UuidV7IdGeneratorTests` — 10,000-value collision-free batch, RFC 9562 version-nibble check, plain-`AddSingleton` DI resolution/singleton-identity, and a monotonicity test asserting non-decreasing `CompareTo`/`<` ordering _across distinct embedded millisecond timestamps_ (an initial tight-loop-of-1000 version of this test failed and was corrected: `Guid.CreateVersion7()` fills everything below the 48-bit timestamp with cryptographically random bits, per RFC 9562's "random" sub-method, not "monotonic random" — two values sharing the same millisecond carry no ordering guarantee against each other; XML docs and README updated to state this precisely rather than overclaim strict tight-loop ordering). Added an "IIdGenerator — Time-Ordered Identifiers" section to `01.Core/README.md`. `SharedKernel.Primitives.Tests` 125/125 passing, 0 build warnings; `SK.01.P293` now fully `●` (4/4); propagated to root `state-map.md` (core-phase-implementer)
- [2026-07-27] C-48/T-38/DO-19 → ● in SK.01.P294 — implemented `WellKnownTagKeys` (`SharedKernel.Primitives/Propagation/`) matching the D-35 locked design exactly (`TenantId`="tenant.id", `CorrelationId`="correlation.id", `ErrorType`="error.type", `ErrorCode`="error.code"), XML-documented as a distinct call-site shape from `WellKnownBaggageKeys` (`Activity.SetTag` vs. `Activity.SetBaggage`/`AddBaggage`) even where the `CorrelationId` literal coincides. Extended `WellKnownPropagationConstantsTests` with 4 new pinning tests for every constant. Extended `01.Core/README.md`'s "Well-Known Propagation Constants" section with a `WellKnownTagKeys` usage example (span tagging via `Activity.SetTag`) and updated its heading/summary to cover all three registries. `SharedKernel.Primitives.Tests` 129/129 passing, 0 regressions; `SK.01.P294` now fully `●` (4/4); propagated to root `state-map.md` (core-phase-implementer)
- [2026-07-27] C-49/T-39/DO-20 → ● in SK.01.P295 — implemented `SystemClock`'s `TimeProvider`-backed internals matching the D-36 locked design exactly: new `SystemClock(TimeProvider timeProvider)` constructor (null-guarded), parameterless `SystemClock()` now delegates to it with `TimeProvider.System`, `UtcNow`/`Today` both read from `_timeProvider.GetUtcNow()`. `IClock`'s own contract unchanged — added a corroborating remark to `IClock.cs` only. Added `SystemClockTimeProviderTests.cs` with a minimal hand-rolled `TimeProvider` fake (no new NuGet dependency): parameterless-ctor real-wall-clock bounds check, fixed-instant injection, advancing-instant reflection, `Today` derivation from the same provider-sourced instant in both cases, and a null-`TimeProvider` guard test. Added a "SystemClock — TimeProvider Interop" section to `01.Core/README.md` showing both the default (`TimeProvider.System`) and shared-custom-`TimeProvider` registration shapes. `SharedKernel.Primitives.Tests` 135/135 passing, 0 build warnings; `SK.01.P295` now fully `●` (4/4); propagated to root `state-map.md` (core-phase-implementer)
- [2026-07-27] C-50/T-40/DO-21 → ● in SK.01.P296 — implemented `IContentHasher`/`Sha256ContentHasher`/`ContentHasherExtensions` (`SharedKernel.Cryptography/Hashing/`) matching the D-37 locked design exactly: `ComputeHash(byte[])`/`ComputeHash(Stream)`/`ComputeHashAsync(Stream, CancellationToken)` backed by `SHA256.HashData`/`HashDataAsync`; `ComputeHashHex`/`ComputeHashBase64` built on the `byte[]` overload via `Convert.ToHexStringLower`/`ToBase64String`. Registered `IContentHasher` as `AddSharedKernelCryptography`'s sixth singleton. Added `Sha256ContentHasherTests` (determinism, single-byte-change divergence, stream/async-stream parity with the `byte[]` overload, null-argument guards, cancellation, hex/Base64 encoding correctness, and a known-answer test against the well-known empty-input SHA-256 digest) plus two new DI sanity tests in `CryptographyServiceCollectionExtensionsTests` (`IContentHasher` resolves; all six services resolve together). Added an "IContentHasher — Non-Secret Content Fingerprinting" section to `01.Core/README.md` placed directly after "One-Way Hashing" for contrast with `IOneWayHasher`. `SharedKernel.Cryptography.Tests` 73/73 passing, 0 build warnings, `SharedKernel.Consumer.Tests` unaffected (additive-only); `SK.01.P296` now fully `●` (4/4); propagated to root `state-map.md` (core-phase-implementer)
- [2026-07-27] S-19/C-51/C-52/T-41/DO-22/P-13/P-14/P-15 → ● in SK.01.P297 — shipped the seventh `01.Core` package, `SharedKernel.Compression`, exactly per the D-38/D-39/D-40 locked design: `IPayloadCompressor` (byte[]/stream/async overloads), `BrotliPayloadCompressor` (unkeyed default + "Brotli"-keyed singleton) and `GZipPayloadCompressor` ("GZip"-keyed singleton only), `CompressionOptions` (`.Level`, `[EnumDataType]`-validated), `AddSharedKernelCompression`. Empirically discovered and corrected a design-doc inaccuracy during implementation: `Decompress` must catch `InvalidOperationException` in addition to `InvalidDataException` — `BrotliStream`'s decoder throws the former ("Decoder ran into invalid data"), not `InvalidDataException`, for corrupt input. Also empirically discovered (verified against this BCL version, not assumed) that neither `BrotliStream` nor `GZipStream` reliably detects a _suffix_-truncated compressed stream as an error — `GZipStream` does not validate its trailing CRC32/ISIZE footer on read, and `BrotliStream` has no fixed magic-number header the way gzip does, so truncation detection is asserted per-algorithm in tests (gzip: prefix-truncation reliably fails via its magic number; Brotli: only the "never throws unhandled" guarantee is asserted) rather than as a single shared contract test — documented in both XML docs and `01.Core/README.md`'s new "A note on truncation detection" subsection, recommending `IContentHasher`/a known length for services that must guarantee end-to-end truncation detection. 46/46 `SharedKernel.Compression.Tests` passing, 0 build warnings under `GenerateDocumentationFile`; packed to `./nupkgs` as `SharedKernel.Compression.1.0.0`; `SharedKernel.Consumer.Tests` extended with Compression DI/roundtrip/corruption checks, 46/46 passing (Primitives + Configuration transitive chain confirmed). `SK.01.P297` now fully `●` (11/11); propagated to root `state-map.md` (core-phase-implementer)
- [2026-07-28] C-53/C-54/T-42/DO-23 → ● in SK.01.P298 — implemented `FeatureVariant` (with `Unassigned` deterministic-fallback sentinel), `FeatureVariantDefinition`, and `IFeatureManager.GetVariantAsync`/`GetVariantAsync<TContext>` in `SharedKernel.FeatureManagement`, exactly per the locked D-41 design, via a `MicrosoftFeatureManagerAdapter` rewrite onto `Microsoft.FeatureManagement.IVariantFeatureManager` (confirmed by reflection to be a superset covering both boolean and variant evaluation, so one injected dependency now serves the whole adapter). Empirically discovered and fixed a real pre-existing defect, not assumed from prose: `AddSharedKernelFeatureManagement` passed `configuration.GetSection("FeatureManagement")` into `Microsoft.FeatureManagement`'s own `AddFeatureManagement`, silently making the variant/allocation schema (`feature_management:feature_flags`, an unscoped, differently-named root key) completely unreachable — confirmed via a throwaway console harness against the real `4.5.0` package before writing adapter code; fixed by passing the root `IConfiguration` instead, which matches this method's pre-existing public signature (no consumer-visible change) and leaves plain boolean `FeatureManagement:*` flags resolving identically. `GetVariantAsync<TContext>` bridges Microsoft's variant API, which (confirmed via reflection) is fixed to a concrete `ITargetingContext` (UserId+Groups) with no generic per-`TContext` equivalent to `IsEnabledAsync<TContext>`, by deriving the targeting identity from `context?.ToString()` — documented explicitly, deterministic by construction. The two pre-existing `IsEnabledAsync` members keep discarding `ct` exactly as before (the newly-injected interface technically accepts it now, but forwarding it was deliberately declined to guarantee byte-for-byte-unchanged behavior per this phase's hard rule). Added `FeatureVariantTests.cs` (29 total `SharedKernel.FeatureManagement.Tests`: 9 pre-existing + 20 new, including a reflection-based test asserting `IFeatureManager`'s public surface never exposes a `Microsoft.FeatureManagement` type). `01.Core/README.md` gained a "Feature Variants — Gradual Rollout" section (dual-schema configuration requirement, percentage-based tenant example, determinism caveat for non-string contexts, and the flagged non-blocking pre-existing AOT-manifest gap, not worsened by this phase). 29/29 `SharedKernel.FeatureManagement.Tests` passing, 0 build warnings; `SK.01.P298` now fully `●` (5/5) — every WO-049 phase inside `01.Core`'s own jurisdiction (P-292→P-298) is complete; propagated to root `state-map.md` (core-phase-implementer)
