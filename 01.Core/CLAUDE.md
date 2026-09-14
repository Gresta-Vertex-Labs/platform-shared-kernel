# 01.Core — Domain Brain

## What This Domain Is

The foundational building blocks domain. Every other domain in the shared kernel depends on this layer, so it must reference nothing from outside `01.Core`. It ships twelve **published** packages today — thirteen minus one, since `SharedKernel.Guards` was merged into `SharedKernel.Core` (P-505/WO-082, shipped, breaking — package retirement only, the guards live in the single `SharedKernel.Guards` namespace inside `SharedKernel.Core`; the former `.Clauses`/`.Descriptions` sub-namespaces were folded into it before the first publish) — covering: functional primitives (`Result<T>`, `Error`), exception-boundary and multi-result-aggregation railway extensions (`ResultTry`, `ResultCombine`), system abstractions (`IClock` — internally `TimeProvider`-backed since P-295 — SmartEnums, base exceptions, BCL extensions), a time-ordered identifier generator (`IIdGenerator`, UUIDv7-backed), a two-path guard system (`Guard.Against` / `Guard.Throw`, now shipped inside `SharedKernel.Core` — its own `InvalidFormat`/`Email` compiled-`Regex` cache bounded at 256 distinct patterns with FIFO eviction, P-522/WO-083, shipped), Options-pattern validation, a Feature Flag abstraction (including weighted-variant/gradual-rollout evaluation), dependency-free cryptographic primitives (secret-agnostic one-way hashing, AES-GCM symmetric encryption — both sync and async, with an additive envelope-encryption seam via `IEnvelopeEncryptionProvider` and a bounded-TTL caching decorator via `CachedEncryptionKeyProvider` (P-446/WO-068, shipped) — RSA/ECDSA + HMAC signing, secure random generation, non-secret content fingerprinting via `IContentHasher`, and RFC 6238/4226 TOTP/HOTP second-factor primitives (`Base32`, `IHotpGenerator`, `ITotpGenerator`, `TotpProvisioningUri`, `ITotpReplayGuard`/`TotpVerifier`, `RecoveryCodeGenerator` — P-451/WO-069, shipped), a dependency-free payload compression primitive (`SharedKernel.Compression`, Brotli-default/GZip-keyed), culture-independent financial/identity format validation (`SharedKernel.Validation` — IBAN/BIC/PAN/ISO 4217/ISO 3166/E.164/VAT + a pluggable national-ID registry, P-443/WO-067, shipped) plus its `FluentValidation` rule adapter (`SharedKernel.Validation.FluentValidation` — a third-party NuGet dependency, P-444/WO-067, shipped), a vendor-backed KMS key provider (`SharedKernel.Cryptography.KeyVault.Azure` — Azure Key Vault Keys, a package with a third-party NuGet dependency, P-447/WO-068, shipped), the platform-wide `LoggingEventIdRanges` registry — a compile-time constant reserving each folder-map domain's `EventId` numbering block for the `[LoggerMessage]` logging convention enforced repo-wide, now spanning 00 through 20 (`Idempotency`/`Scheduling`/`Reporting` bases added via `SK.01.LoggingRangesNewDomains`, shipped) — and the `WellKnownHeaders` / `WellKnownBaggageKeys` / `WellKnownTagKeys` registries, the platform-wide source of cross-service propagation identifier literals (HTTP/gRPC header names, `Activity` baggage keys, and `Activity` tag/attribute keys) that every domain touching correlation-id, tenant-id, or error-classification propagation must reference instead of redeclaring locally, PII/data-classification taxonomy and masking (`SharedKernel.DataPrivacy` — `DataClassification`/`SensitiveDataCategory` marker attributes, `PiiMasking.*` deterministic helpers, `IDataSubjectRequestHandler`, P-474/WO-076, shipped), and a culture-keyed error-message catalog seam (`SharedKernel.Localization` — `ILocalizationCatalog`/`InMemoryLocalizationCatalog`/`StringLocalizerLocalizationCatalog`, a package with a first-party Microsoft NuGet dependency, P-482/WO-078, shipped). WO-081 (P-491→P-496) is a coordinated, `01.Core`-first breaking wave that seven other domains' own planners dispatch against once each phase ships: required associated-data (AAD) on every `ISymmetricEncryptionService` member (P-491, **shipped, breaking**); a `ISynchronousEncryptionKeyProvider` capability marker (plus `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous` and `CachedEncryptionKeyProvider.Inner`) replacing the retained sync `Encrypt`/`Decrypt`/`EncryptToString`/`DecryptToString` members' silent thread-blocking hazard with a structural `NotSupportedException` (P-492, **shipped, breaking behavior change — not a compile-time API break**); `IAsymmetricKeyProvider`/`IAsymmetricSignatureService` going async (`GetRsaKeyAsync`/`GetEcdsaKeyAsync` replacing the removed sync members, `SignAsync`/`VerifyAsync` added), an analogous `ISynchronousAsymmetricKeyProvider`/`AsymmetricKeyProviderCapabilities` gate on the retained sync `Sign`/`Verify`, a key-disposal-ownership fix (the provider-returned `RSA`/`ECDsa` instance is no longer disposed by the signing service), and `Verify`/`Sign` minimum-key-size parity — RSA's existing 2048-bit check extended from `Sign`-only to both members, ECDSA gaining a wholly new 256-bit check on both members where none existed before (P-493, **shipped, breaking**); an Azure Key Vault Keys remote-signing `IAsymmetricKeyProvider` implementation (P-494, shipped — `AzureKeyVaultAsymmetricKeyProvider`, `KeyVaultRsaKey`/`KeyVaultEcdsaKey`; corrected post-implementation from the design-lock pass's assumed `SignData`/`VerifyData` override shape to the real BCL extension points, `SignHash`/`VerifyHash`); a thirteenth package, `SharedKernel.Cryptography.Argon2` (`Argon2idOneWayHasher`, a keyed OWASP-preferred alternative to the unkeyed PBKDF2 default, P-495, **shipped** — Konscious.Security.Cryptography.Argon2 as the pure-managed third-party dependency, a real (not nominal) `[Range]` floor on every `Argon2CryptographyOptions` property, and the real PHC string format); and Azure Key Vault provider hardening — per-key-name `CryptographyClient` connection reuse, short durable rotating key-version tags backed by a new Key-Vault-Secrets registry, and an explicitly-callable `MintNewVersionAsync` rotation story (P-496, design-locked, implementation pending). Of WO-083's remaining correctness/hygiene phases (P-515→P-522, P-524→P-526 — `P-523` is `00.Governance`'s own, `P-527`/`P-528` are `16.Testing`'s/`12.Security`'s own, all three already shipped or tracked elsewhere), `P-517`, `P-520`, and `P-522` have **shipped** — `P-522` (the `InvalidFormat`/`Email` compiled-`Regex` cache bounded at 256 distinct patterns with FIFO eviction) shipped in the same pass as WO-082 below, since it depended on `P-505` landing the Guards surface inside `SharedKernel.Core` first. See `01.Core/state-map.md`'s Overall Progress table for the authoritative per-phase status of every other WO-083 phase — this narrative paragraph is not kept in lockstep with every one of them.

**WO-082 (P-505/P-506/P-507, shipped end to end):** `SharedKernel.Guards` merged into `SharedKernel.Core` — see the package-count note in this file's opening sentence above. `P-506` re-pointed `SharedKernel.Validation.csproj`'s `ProjectReference` from the retired `SharedKernel.Guards.csproj` to `SharedKernel.Core.csproj` with zero source change to `GuardValidationExtensions.cs`. `P-507` updated `SharedKernel.Consumer.Tests` — removed its `SharedKernel.Guards` `PackageReference`, updated its dependency-chain comments, and added a direct `.nuspec` inspection proving the merge introduced no new transitive dependency into `SharedKernel.Core` (still `SharedKernel.Primitives` only). `00.Governance`'s `P-508` and `03.Domain`'s `P-509` (re-pointing `SharedKernel.ArchitectureTests`/`SharedKernel.Domain` off the same retired project) are each other domains' own phases, dispatched separately — until they land, a whole-solution build stays red by design.

Philosophy: **Zero external dependencies for Primitives. Pure C#. AOT-first. Railway-oriented.**

> **Why cryptography lives here, not in `12.Security`:** `12.Security` owns identity/authentication concerns (`IUserContext`, `ITenantProvider`, JWT/OIDC). Generic crypto primitives — hashing, encryption, signing, secure random — are a separate concern needed by services that have no identity stack at all (background workers, batch jobs, internal tools). Bundling them into `12.Security` would force every consumer to pull in OIDC/JWT machinery just to hash a secret or encrypt a payload. `SharedKernel.Cryptography` lives in `01.Core` because, like `SharedKernel.Primitives`, it references nothing else in the platform — `12.Security.Oidc` may depend on it for token-signing primitives, never the reverse. This is also why `IOneWayHasher` is named and shaped the way it is (WO-034): a `01.Core` primitive must stay free of any single consuming-domain's vocabulary, including the auth domain's own "password" terminology.

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Primitives` | `Result<T>`, `Error`, `ErrorType`, `ErrorCodes`, `IClock`, `IIdGenerator`, `SmartEnum<TEnum,TValue>`, `SmartEnumJsonConverter<TEnum,TValue>`, `LoggingEventIdRanges`, `WellKnownHeaders`, `WellKnownBaggageKeys`, `WellKnownTagKeys` | nothing |
| `SharedKernel.Core` | Base exceptions (incl. `ForbiddenException`, `error.ToException()`), BCL extension methods, railway extensions for `Result`/`Result<T>` (`Map`/`MapError`/`Bind`/`Ensure`/`Match`/`Tap`/`TapError`, sync/`Task`/`ValueTask`), `ResultTry`/`ResultCombine`, and the two-path guard system: `Guard.Against.*` (functional) + `Guard.Throw.*` (imperative) in the single `SharedKernel.Guards` namespace — merged from the former `SharedKernel.Guards` package (P-505/WO-082); public API tracked by `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`, with the `InvalidFormat`/`Email` compiled-`Regex` cache bounded at 256 distinct patterns with FIFO eviction (P-522/WO-083, shipped, additive) | `SharedKernel.Primitives` |
| `SharedKernel.Configuration` | Options-pattern validation: four `AddValidatedOptions` overloads (explicit section or `ISectionBoundOptions`-declared path; DataAnnotations, a caller-supplied `IValidateOptions<T>`, or both; named instances), all `.ValidateOnStart()`-backed | — (none; the `SharedKernel.Primitives` reference was dead and was removed, P-530/C-134) |
| `SharedKernel.FeatureManagement` | `IFeatureManager` abstraction (boolean + weighted-variant evaluation) + `Microsoft.FeatureManagement` adapter | `SharedKernel.Primitives` |
| `SharedKernel.Cryptography` | Secret-agnostic one-way hashing, AES-256-GCM symmetric encryption (sync + async `*Async` overloads), async KMS-capable `IEncryptionKeyProvider`, additive `IEnvelopeEncryptionProvider`/`CachedEncryptionKeyProvider` (P-446/WO-068, shipped, breaking), RSA/ECDSA + HMAC signing, secure random/token generation, non-secret content fingerprinting (`IContentHasher`), RFC 6238/4226 TOTP/HOTP + `Base32`/`TotpProvisioningUri`/`ITotpReplayGuard`/`TotpVerifier`/`RecoveryCodeGenerator` (P-451/WO-069, shipped, additive), opt-in `IEncryptionKeyProviderProbe`/`EncryptionKeyProviderHealth` readiness-probe primitive (P-487/WO-080, shipped, additive), required associated-data (AAD) on every `ISymmetricEncryptionService` member (P-491/WO-081, **shipped, breaking**), a `ISynchronousEncryptionKeyProvider`/`EncryptionKeyProviderCapabilities` capability-marker gate on the retained sync `Encrypt`/`Decrypt`/`EncryptToString`/`DecryptToString` members (P-492/WO-081, **shipped, breaking behavior change**), and `IAsymmetricKeyProvider`/`IAsymmetricSignatureService` going async (`GetRsaKeyAsync`/`GetEcdsaKeyAsync`, `SignAsync`/`VerifyAsync`) + an analogous `ISynchronousAsymmetricKeyProvider`/`AsymmetricKeyProviderCapabilities` gate + a key-disposal-ownership fix + `Verify`/`Sign` minimum-key-size parity (P-493/WO-081, **shipped, breaking**), and `ITotpReplayGuard`'s two-step `HasBeenUsedAsync`/`MarkUsedAsync` collapsed into one atomic `TryMarkUsedAsync` closing a genuine replay TOCTOU, `TotpVerifier.VerifyAsync` gaining optional `digits`/`stepSeconds`/`driftWindow`/`algorithm` parameters so its replay window matches what was actually validated, and a new standalone `ITotpAttemptThrottle` seam (P-514/WO-083, **shipped, breaking**) | `SharedKernel.Primitives`, `SharedKernel.Configuration` |
| `SharedKernel.Compression` | Generic payload compression (`IPayloadCompressor`): Brotli default, GZip keyed alternate | `SharedKernel.Primitives`, `SharedKernel.Configuration` |
| `SharedKernel.Validation` *(shipped, P-443/WO-067)* | Culture-independent format validators: IBAN, BIC, PAN (Luhn + network detection), ISO 4217, ISO 3166, E.164, VAT baseline, pluggable per-country `INationalIdValidator` registry (TCKN default); dual-mode standalone `Result`/bool + `Guard.Against.*` extensions | `SharedKernel.Primitives`, `SharedKernel.Core` (re-pointed from the retired `SharedKernel.Guards`, P-506/WO-082) |
| `SharedKernel.Validation.FluentValidation` *(shipped, P-444/WO-067)* | `IRuleBuilder<T,string>` rule adapter for every `SharedKernel.Validation` validator — the domain's only package with a third-party NuGet dependency | `SharedKernel.Validation`, `FluentValidation` (NuGet) |
| `SharedKernel.Cryptography.KeyVault.Azure` *(shipped, P-447/WO-068; probe added P-487/WO-080; remote signing added P-494/WO-081; connection-reuse/durable-version-registry/rotation hardening added P-496/WO-081)* | Azure Key Vault Keys implementation of `IEncryptionKeyProvider` + `IEnvelopeEncryptionProvider` + `IEncryptionKeyProviderProbe`, now backed by a durable, cross-replica-shared Key-Vault-Secrets version registry with a real `MintNewVersionAsync` rotation entry point and backward-read compatibility for the pre-P-496 shape (P-496/WO-081, **shipped, additive**) — plus a remote-signing `IAsymmetricKeyProvider` implementation (`AzureKeyVaultAsymmetricKeyProvider`, `KeyVaultRsaKey`/`KeyVaultEcdsaKey`, P-494/WO-081, **shipped**, a distinct singleton from `AzureKeyVaultEncryptionKeyProvider`) | `SharedKernel.Cryptography`, `SharedKernel.Configuration`, `Azure.Security.KeyVault.Keys`, `Azure.Security.KeyVault.Secrets`, `Azure.Identity` (NuGet) |
| `SharedKernel.DataPrivacy` *(shipped, P-474/WO-076)* | `DataClassification`/`SensitiveDataCategory` marker attributes, `PiiMasking.*` pure helpers, `IDataSubjectRequestHandler` | `SharedKernel.Primitives` |
| `SharedKernel.Localization` *(shipped, P-482/WO-078)* | `ILocalizationCatalog` keyed by `(code, CultureInfo)`; `InMemoryLocalizationCatalog` default (with parent-culture-chain fallback down to `CultureInfo.InvariantCulture`) + `StringLocalizerLocalizationCatalog` resx-composition path | `SharedKernel.Primitives`, `Microsoft.Extensions.Localization.Abstractions` (NuGet) |
| `SharedKernel.Cryptography.Argon2` *(shipped, P-495/WO-081)* | `Argon2idOneWayHasher` — a `"Argon2id"`-keyed `IOneWayHasher` alternative to the unkeyed PBKDF2 default, self-describing PHC-string output | `SharedKernel.Cryptography`, `SharedKernel.Configuration`, `Konscious.Security.Cryptography.Argon2` (NuGet) |

All twelve packages listed above are published — thirteen minus `SharedKernel.Guards`, merged into `SharedKernel.Core` (P-505/WO-082, shipped). `SharedKernel.Validation` (P-443/WO-067), `SharedKernel.Validation.FluentValidation` (P-444/WO-067), `SharedKernel.Cryptography.KeyVault.Azure` (P-447/WO-068), `SharedKernel.DataPrivacy` (P-474/WO-076), `SharedKernel.Localization` (P-482/WO-078), and `SharedKernel.Cryptography.Argon2` (P-495/WO-081) shipped as the eighth, ninth, tenth, eleventh, twelfth, and thirteenth published packages AT THE TIME EACH SHIPPED (a historical record of shipping order, unaffected by the later Guards merge — same convention the root `CLAUDE.md` uses for per-package version numbers) — see `SK.01.P443`/`SK.01.P444`/`SK.01.P447`/`SK.01.P474`/`SK.01.P482`/`SK.01.P495`. See WO-081 below for the full six-phase batch (P-491→P-496) touching `SharedKernel.Cryptography` and `SharedKernel.Cryptography.KeyVault.Azure` as well — `SK.01.P496` (Azure Key Vault provider hardening) has shipped in source (code/tests/docs), but its `Azure.Security.KeyVault.Secrets` NuGet reference needs a root-owned `Directory.Packages.props` `PackageVersion` pin (`Azure.Security.KeyVault.Secrets` `4.7.0`, matching the already-pinned `Azure.Security.KeyVault.Keys`) before this package restores/builds/packs again in this repo — see the WO-081 changelog entry for this phase. All twelve target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Functional primitives | Pure C# 13 — no NuGet dependencies |
| System abstractions | Pure C# 13 — no NuGet dependencies |
| Guard clauses | Pure C# 13 — no NuGet dependencies; compiled/cached `System.Text.RegularExpressions.Regex` for format/email guards |
| Options validation | `Microsoft.Extensions.Options.DataAnnotations` (default path); additive source-generator path *(P-519/WO-083, shipped)* via the in-box `[OptionsValidator]` generator, zero reflection at validation time |
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
| Argon2id one-way hashing *(P-495/WO-081, shipped)* | `Konscious.Security.Cryptography.Argon2` 1.3.1 (NuGet) — pure-managed, no native/P-Invoke binding; confined to `SharedKernel.Cryptography.Argon2`; never a transitive dependency of `SharedKernel.Cryptography` core |
| Key Vault Secrets (rotation registry) *(P-496/WO-081, design-locked)* | `Azure.Security.KeyVault.Secrets` (NuGet) — confined to `SharedKernel.Cryptography.KeyVault.Azure`, a second Azure SDK family alongside the existing `Azure.Security.KeyVault.Keys` |

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
    — (P-515/WO-083, design-locked, implementation pending) reaching FromValue/TryFromValue/FromName/List
      through an INHERITED static call alone does not reliably trigger TEnum's own static constructor (the
      one whose field initializers populate the member list); fixed via a private static field on
      SmartEnum<TEnum,TValue> itself, forcing TEnum's cctor exactly once via
      RuntimeHelpers.RunClassConstructor(typeof(TEnum).TypeHandle) during SmartEnum<TEnum,TValue>'s own
      static initialization — zero added cost on the hot lookup path, since the force-call never runs
      per-call

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
Base exceptions  (SharedKernel.Core.Exceptions; all derive from SharedKernelException; string-only constructors are forbidden)
    SharedKernelException (abstract)  (Error error, Exception? inner = null) / (string message, Error error, Exception? inner = null)
    DomainException(Error [, Exception])             — not sealed; Guard.Throw throws it
    ValidationException(Error) / (IReadOnlyList<Error>) — copies the list; rejects an empty list or null entries
    NotFoundException / ConflictException / UnauthorizedException (401) / ForbiddenException (403)   (Error [, Exception])
    — every constructor throws ArgumentNullException for a null Error
    — the HTTP status comes from Error.Type (14.Presentation maps by type), never from the exception subclass
    error.ToException() → SharedKernelException  (ErrorExceptionExtensions)
        Validation→ValidationException, NotFound→NotFoundException, Conflict→ConflictException,
        Unauthorized→UnauthorizedException, Forbidden→ForbiddenException, any other type→DomainException;
        Error.None → ArgumentException

Railway extensions  (SharedKernel.Core.Extensions.ResultExtensions — partial class across three files)
    Result<T> : Map, MapError, Bind (→Result<TOut> or →Result), Match, Tap, TapError,
                Ensure(predicate, Error | Func<T, Error>), GetValueOrThrow()
    Result    : Map, MapError, Bind (→Result or →Result<TOut>), Match (value or void), Tap, TapError,
                Ensure(predicate, Error), ThrowIfFailure()
    Async shapes — a continuation's awaitable type always matches its source's:
        Task<Result…> source       : sync or Task-returning continuations
        ValueTask<Result…> source  : sync or ValueTask-returning continuations
        plain Result… source       : Task-returning continuations only
      Offering both Task and ValueTask continuations on one source makes every async lambda ambiguous (CS0121).
    — genuine async state machines (arguments validated synchronously, then a static local async Core): a faulted
      source rethrows its original exception and a cancelled one throws OperationCanceledException. The earlier
      ContinueWith(t => t.Result) implementation wrapped both in AggregateException.
    — a Task-returning continuation that returns null throws InvalidOperationException
    — a lambda whose body only throws has no return type, so it is ambiguous between the sync and async overloads;
      give it an explicit return type: `Result () => throw …`
    — ResultExtensions.Task.cs and ResultExtensions.ValueTask.cs mirror each other operation for operation;
      change both together

ResultTry  (static class — exception boundary)
    Try<T>(Func<T> [, onException])                                          → Result<T>
    Try(Action [, onException])                                              → Result
    TryAsync<T>(Func<Task<T>> [, onException])                               → Task<Result<T>>
    TryAsync(Func<Task> [, onException])                                     → Task<Result>
    TryAsync<T>(Func<CancellationToken, Task<T>> [, onException], CancellationToken) → Task<Result<T>>
    TryAsync(Func<CancellationToken, Task> [, onException], CancellationToken)       → Task<Result>
    — default mapping: Error.Unexpected(ErrorCodes.Unexpected.Default, ResultTry.DefaultUnexpectedMessage). The
      exception's type and message never reach Error.Message (they can leak connection strings or host names into
      an HTTP response); the exception is recorded with Activity.Current?.AddException, once per flattened
      AggregateException inner exception. With no current Activity it is recorded nowhere, a documented
      limitation; pass onException to capture it yourself (P-510/WO-083)
    — OperationCanceledException is never caught, with or without a mapper; a token overload throws it before
      invoking the delegate when the token is already cancelled
    — no ValueTask overloads, by design: Func<Task<T>> beside Func<ValueTask<T>> makes async lambdas ambiguous

ResultCombine  (static class — multi-result aggregation, P-292/WO-049)
    Combine(params Result[] | IEnumerable<Result>)             → ValidationResult
    Combine<T>(params Result<T>[] | IEnumerable<Result<T>>)    → ValidationResult<IReadOnlyList<T>>
    — evaluates every input, no short-circuit; a failure carries every failing Error in input order

BCL extension methods  (no reflection)
    string         : ToSnakeCase, ToKebabCase, ToCamelCase, ToPascalCase
                     — one word splitter (separators _ - whitespace; lower or digit → Upper; acronym → Word),
                       invariant casing, ArgumentNullException on null. "HTMLParser" → html_parser / htmlParser / HtmlParser
    IEnumerable<T> : IsNullOrEmpty ([NotNullWhen(false)], reads at most one element), WhereNotNull (reference and Nullable<T>)
    DateTimeOffset : StartOfDay (keeps the offset)
    — deliberately absent because the BCL already has them: Enumerable.Chunk (was ToBatches),
      string.IsNullOrWhiteSpace (was an extension), DateTimeOffset.ToUnixTimeMilliseconds (was ToUnixMilliseconds,
      which also rounded pre-1970 values differently), a Guid.Empty comparison (was Guid.IsEmpty)
    — EndOfDay was removed: an inclusive 23:59:59.999 end drops sub-millisecond ticks; query
      [StartOfDay, StartOfDay.AddDays(1)) instead
```

### `SharedKernel.Configuration` — public surface

```
ISectionBoundOptions                                          (P-530/C-130, shipped)
    static abstract string SectionName { get; }
    → the options type declares its own configuration section path, so no call site retypes it.
      Opt-in per options type; every explicit-section overload below ignores it entirely.
      Adoption note: declare `public static string SectionName => "..."`, NOT a const field —
      a const field cannot satisfy a static abstract property. Reached through a generic type
      parameter, so it compiles to a direct static call: no reflection, nothing for a trimmer to miss.

AddValidatedOptions<TOptions>(IConfigurationSection section, string? name = null)
    → registers IOptions<TOptions>, IOptionsSnapshot<TOptions>, IOptionsMonitor<TOptions>;
      binds the section and applies DataAnnotations validation, armed with .ValidateOnStart()

AddValidatedOptions<TOptions>(IConfiguration configuration, string? name = null)
    where TOptions : class, ISectionBoundOptions                (P-530/C-130, shipped)
    → identical, except the section comes from TOptions.SectionName. Prefer this one: it is the
      only form in which a caller cannot pass the wrong section, because there is no section argument

AddValidatedOptions<TOptions, TValidator>(IConfigurationSection section,
                                          bool validateDataAnnotations = false,
                                          string? name = null)
    where TValidator : class, IValidateOptions<TOptions>
    → binds the section and registers TValidator via
      TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>()) —
      NEVER TryAddSingleton (P-530/C-128; see Implementation Rules). TValidator is typically a
      caller-authored [OptionsValidator]-generated partial class (in-box BCL source generator), so
      VALIDATION performs no reflection; BINDING still does — see AOT Compatibility.
      validateDataAnnotations: true additionally applies DataAnnotations, for a hand-written
      TValidator that checks only cross-property rules; false (default) for a generated one,
      which already covers the attributes and would otherwise double-report every failure

AddValidatedOptions<TOptions, TValidator>(IConfiguration configuration,
                                          bool validateDataAnnotations = false,
                                          string? name = null)
    where TOptions : class, ISectionBoundOptions                (P-530/C-130, shipped)
    where TValidator : class, IValidateOptions<TOptions>
    → the same, with the section path taken from TOptions.SectionName
```

> **Corrected P-530:** this block previously listed a `[ValidateOptions]` marker attribute. No such
> type exists, in this package or anywhere in the repo — verified by grep. It was never shipped.
>
> **Overload resolution:** an `IConfigurationSection` argument always beats the `IConfiguration` one
> (a section IS an `IConfiguration`, and the section-typed overload is more specific), so a type
> implementing `ISectionBoundOptions` can still be pointed at a different section deliberately. Both
> forms coexist unambiguously; pinned by a test.

### `SharedKernel.Core` — Guard Clause System public surface (namespace `SharedKernel.Guards`)

```
One namespace: `using SharedKernel.Guards;` brings Guard, IGuardClause, every Against.* extension and
GuardErrorExtensions into scope. The former SharedKernel.Guards.Clauses / .Descriptions namespaces were folded in
before the first publish: the extension methods lived in .Clauses, so `using SharedKernel.Guards;` alone did not
make Guard.Against.* compile. Analyzer SK0006 and GuardPurityRules match SharedKernel.Guards.IGuardClause.

IGuardClause  (public marker interface) — extend the functional path with extension methods of your own on it
Guard  (static class)
    .Against                                           → IGuardClause
    .Collect(params ReadOnlySpan<Error?> errors)       → ValidationResult  (every non-null error, in order)
Guard.Throw  (nested static class) — mirrors every Against.* guard name for name (a reflection test enforces it) and
    throws DomainException(error); reference, string and collection parameters carry [NotNull], True/False carry
    [DoesNotReturnIf]
GuardErrorExtensions
    Error?.ToResult()               → Result
    Error?.ToResult<T>(T value)     → Result<T>
    Error?.ToResult<T>(Func<T>)     → Result<T>   (the factory runs only when every guard passed)

Guard contract (GuardClauseExtensions)
    — returns null on pass and a Validation Error on violation, and never throws; a null input is a violation
      (ErrorCodes.Validation.Required), including for the length, format, range and collection guards
    — every guard ends with `[CallerArgumentExpression] string? paramName = null`, so nameof is optional
    — messages use CompositeFormat with CultureInfo.InvariantCulture, identical on every server; translate by Error.Code

    Null/empty     Null<T> (class and Nullable<T> overloads), NullOrEmpty, NullOrWhiteSpace              → Required
    String length  ShorterThan → MinLength, LongerThan → MaxLength
    Numeric        Negative<T>, NegativeOrZero<T> where T : INumber<T> (every numeric type; NaN is a violation) → OutOfRange
                   — NotPositive was removed: it was NegativeOrZero with a different message
    Comparison     OutOfRange<T>(value, min, max), LessThan<T>(value, min), GreaterThan<T>(value, max)
                   (T : IComparable<T>, inclusive bounds)                                               → OutOfRange
    Value          Default<T>, InvalidGuid → Required; InvalidEnumValue<TEnum> (Enum.IsDefined) → OutOfRange;
                   NotUtc(DateTimeOffset: Offset != 0 | DateTime: Kind != Utc) → InvalidFormat
    Format         InvalidFormat(value, [StringSyntax(Regex)] pattern) → InvalidFormat; the pattern is NOT in the message
                   — per-pattern compiled Regex cache, 250 ms timeout, bounded at 256 patterns, FIFO eviction (P-522/WO-083)
                   Email → InvalidFormat ([GeneratedRegex], loose local@domain.tld check); null or blank → Required
    Collections    Empty (reads ≤ 1), MinCount (reads ≤ min), MaxCount (reads ≤ max + 1); TryGetNonEnumeratedCount first
    Boolean        True(condition, Error), False(condition, Error) — the caller supplies the Error
    SmartEnum      InvalidSmartEnum<TEnum, TValue>(value)                                               → OutOfRange

GuardDescriptions  (internal) — CompositeFormat message templates
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
    — VERIFY-TIME CEILING (P-512/WO-083, shipped): Verify no longer trusts an unbounded storedIterations/
      subkey-length pair read out of the hash blob. MaxVerifiableIterations = 2_000_000 — a FIXED constant,
      deliberately independent of CryptographyOptions.Pbkdf2Iterations's currently-configured value (never
      derived from it, so a future legitimate config increase never forces a simultaneous ceiling bump) — is
      checked BEFORE Rfc2898DeriveBytes.Pbkdf2 is ever called; a stored iterations value above this ceiling, at
      or below zero, or a decoded subkey whose length is not EXACTLY SubkeySize (32) returns
      HashVerificationResult.Failed without running the expensive derive call at all. Closes a real DoS vector:
      the self-describing-format design means a stored hash's iteration count AND requested-derived-key-length
      are both attacker-influenceable by anyone who can write a hash row — Verify previously honored either
      unconditionally. The new ceiling is NEVER enforced retroactively — an existing hash stored at any
      iteration count at or below the ceiling (including a legacy value below the NEW floor below) still
      verifies correctly, preserving the "raising Pbkdf2Iterations never invalidates already-stored hashes"
      guarantee

ISymmetricEncryptionService  (BREAKING as of P-491/WO-081, shipped — every member gains a required associatedData parameter; BREAKING BEHAVIOR CHANGE as of P-492/WO-081, shipped — the sync members' bridge is now gated)
    Encrypt(byte[] plaintext, byte[] associatedData)            → EncryptedPayload        (always encrypts with the provider's current key; as of P-492/WO-081, SHIPPED, throws NotSupportedException — directing the caller to EncryptAsync — when the registered IEncryptionKeyProvider is not confirmed genuinely synchronous via EncryptionKeyProviderCapabilities.IsGenuinelySynchronous, computed once at AesGcmEncryptionService construction; otherwise bridges via .GetAwaiter().GetResult() exactly as before, unchanged from P-446/WO-068; see EncryptAsync for the never-gated path)
    EncryptAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct = default) → ValueTask<EncryptedPayload>   (P-446/WO-068, shipped — never blocks a thread; always usable regardless of the P-492 gate; prefer this on hot/high-throughput paths)
    Decrypt(EncryptedPayload payload, byte[] associatedData)    → Result<byte[]>          (failure: Error.Unexpected — tamper, wrong key, unknown KeyId, OR mismatched associatedData; never throws CryptographicException directly; same P-492/WO-081 sync-gating as Encrypt, SHIPPED)
    DecryptAsync(EncryptedPayload payload, byte[] associatedData, CancellationToken ct = default) → ValueTask<Result<byte[]>>   (P-446/WO-068, shipped — async counterpart of Decrypt)
    EncryptToString(string plaintext, byte[] associatedData)    → string                  (convenience: UTF-8 → Encrypt → single self-describing Base64 string, KeyId+Nonce+Ciphertext+Tag packed together — associatedData is NEVER packed into this string)
    EncryptToStringAsync(string plaintext, byte[] associatedData, CancellationToken ct = default) → ValueTask<string>   (P-446/WO-068, shipped)
    DecryptToString(string encoded, byte[] associatedData)      → Result<string>          (convenience inverse of EncryptToString)
    DecryptToStringAsync(string encoded, byte[] associatedData, CancellationToken ct = default) → ValueTask<Result<string>>   (P-446/WO-068, shipped)
    — associatedData (P-491/WO-081, shipped, BREAKING): authenticated-but-never-encrypted data bound into
      the AES-GCM tag, passed straight through to AesGcm.Encrypt/.Decrypt's own associatedData parameter — a
      BCL AEAD capability already present, simply unused until this phase. NEVER persisted inside
      EncryptedPayload (no new field) and NEVER defaulted — every call site supplies it explicitly;
      `Array.Empty<byte>()` is an acceptable explicit no-context-binding value, an implicit default is not.
      The caller must be able to reproduce byte-identical associatedData at decrypt time from context already
      available then (a row's own primary key, a cache key, a message type, a subscription id) — mismatched
      associatedData fails authentication exactly like a flipped ciphertext/tag byte, surfacing as the same
      Result<byte[]>.Failure(Error.Unexpected(...)) shape, no new ErrorCodes constant. Six cascading
      consuming domains (02.Caching's CacheEncryptionSerializer, 06.Persistence's EncryptedValueConverter<T>
      — the hardest case, no direct row-PK access inside a vanilla EF Core ValueConverter — 07.Messaging's
      payload-transform trio, 15.Integration's webhook encryption, 17.Workflows's EncryptionPayloadCodec,
      16.Testing's AddFakeCryptography fake) each carry their own WO-081 follow-on phase to supply a real
      AAD derivation; the inventory itself lives in `01.Core/state-map.md`'s SK.01.P491 phase notes.
    — the four sync members are RETAINED (not removed). As of P-446/WO-068 they bridged onto the async
      `IEncryptionKeyProvider` via `.GetAwaiter().GetResult()` unconditionally; as of P-492/WO-081, SHIPPED,
      that bridge is GATED behind `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(provider)` —
      computed once at `AesGcmEncryptionService` construction and cached in a private `bool` field —
      throwing a structural `NotSupportedException` (naming the correct `*Async` counterpart) directing the
      caller to the `*Async` overloads instead of ever silently blocking a thread when the registered
      provider is not asserted synchronous-safe. Behavior is byte-for-byte unchanged for the common
      config-backed default, which implements the new `ISynchronousEncryptionKeyProvider` marker. This is a
      real, narrow BREAKING BEHAVIOR CHANGE — not a compile-time API break, since no signature changed — for
      any existing custom `IEncryptionKeyProvider` implementer that relied on the old bridge silently
      blocking against a network-bound provider; it now observes `NotSupportedException` at the same call
      sites instead. `AesGcmEncryptionService` shares one pure, synchronous `EncryptCore`/`DecryptCore` pair
      between the sync and async members so both call shapes are guaranteed byte-identical for the same input

EncryptedPayload  (sealed record)
    .KeyId                                                      → string                  (which key version encrypted this payload)
    .Nonce                                                      → byte[]                  (96-bit, random per call — never reused)
    .Ciphertext                                                 → byte[]
    .Tag                                                        → byte[]                  (128-bit AES-GCM authentication tag)

AesGcmEncryptionService  (sealed class, implements ISymmetricEncryptionService)
    — AES-256-GCM via System.Security.Cryptography.AesGcm; authenticated (tamper-evident) encryption only —
      never an unauthenticated mode such as CBC/ECB
    — KEY-SIZE VALIDATION (P-513/WO-083, shipped): EnsureKeySize(CryptographicKey) is called at the start
      of both EncryptCore and DecryptCore — the two pure cores shared by all four sync+async public members —
      BEFORE any AesGcm construction, throwing CryptographicException (naming expected 32 vs. actual length) for
      a key whose Material is not EXACTLY 32 bytes. Closes a real silent-downgrade gap: AesGcm's own constructor
      accepts any BCL-legal AES key size (16/24/32 bytes) without complaint, so a misconfigured 16-byte key
      previously produced AES-128-GCM with no signal that the real security margin was half of what every
      doc/README/NuGet description on this package promises

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

CachedEncryptionKeyProvider  (sealed class, implements IEncryptionKeyProvider — P-446/WO-068, shipped, additive; cancellation-token leak fixed as of P-511/WO-083, shipped)
    ctor(IEncryptionKeyProvider inner, TimeProvider timeProvider, TimeSpan ttl)
    .Inner                                                       → IEncryptionKeyProvider  (P-492/WO-081, shipped, additive — public read-only, exposes the wrapped instance)
    — bounded-TTL decorator over any IEncryptionKeyProvider: a cache hit inside the TTL window never calls
      the inner provider; an expired/missing entry always re-fetches. Single-flight per cache key (the
      current key, or one specific keyId): N concurrent callers past expiry trigger exactly one inner-provider
      call — implemented via a ConcurrentDictionary compare-and-swap race combined with a Lazy<Task<T>> whose
      factory itself executes at most once even under contention. A failed refresh propagates the thrown
      exception to every caller awaiting that single-flight resolution — NEVER a stale fallback — and the
      failed slot is discarded so the next call retries rather than staying permanently poisoned. Ships with
      no package-owned DI extension — mirrors the IIdGenerator/SystemClock(TimeProvider) no-extension
      precedent; composed explicitly at the consumer's own composition root
    — CANCELLATION-TOKEN LEAK FIX (P-511/WO-083, shipped): as originally shipped, the shared Lazy<Task<T>>
      factory closure captured the CancellationToken of whichever caller's GetOrAdd/TryUpdate race happened to
      WIN construction of the cache slot — every OTHER concurrent caller then awaited that exact same shared
      Task, so the winning caller's own cancellation could fault or cancel every other caller's still-legitimate,
      still-in-flight request. Fixed via a dedicated per-slot CancellationTokenSource (NEVER derived from any
      individual caller's token) driving the shared inner factory call, with each caller instead awaiting the
      shared Task via Task.WaitAsync(callerCt) (.NET 6+) — surfacing OperationCanceledException to THAT CALLER
      ONLY — and a per-slot reference count of currently-awaiting callers that cancels the dedicated source only
      once it reaches zero, so the inner call is genuinely aborted once abandoned by every caller, never while
      at least one caller is still waiting. The SAME defect shape (and the SAME fix) also applies to
      SharedKernel.Cryptography.KeyVault.Azure's AzureKeyVaultEncryptionKeyProvider (two cache sites) and
      AzureKeyVaultAsymmetricKeyProvider (one cache site) — see that package's own section below
    — .Inner (P-492/WO-081, shipped) exists specifically so EncryptionKeyProviderCapabilities.IsGenuinelySynchronous
      can see through this decorator to the true leaf provider — CachedEncryptionKeyProvider itself NEVER
      directly implements ISynchronousEncryptionKeyProvider, because a cache hit is always fast regardless of
      what it wraps but a cache miss re-enters the inner provider, and if that inner genuinely blocks on
      network I/O, so does this decorator on that path. Naively treating this type as always-synchronous
      because it caches would be exactly the "decorator's best-case behavior" the capability check must
      never trust

ISynchronousEncryptionKeyProvider : IEncryptionKeyProvider  (zero-member marker interface — SHIPPED, P-492/WO-081)
    — implemented ONLY by a provider that can genuinely guarantee GetCurrentKeyAsync/GetKeyAsync never
      perform a blocking network/IPC round trip (the config/environment-backed default). A KMS/HSM-backed
      provider must NEVER implement this marker — implementing it is an explicit, author-asserted safety
      claim, never inferred by this package. This is the pattern P-493 (SHIPPED) reuses for
      IAsymmetricSignatureService's own retained sync Sign/Verify, via an analogous
      ISynchronousAsymmetricKeyProvider/AsymmetricKeyProviderCapabilities pair

EncryptionKeyProviderCapabilities  (static class — SHIPPED, P-492/WO-081)
    IsGenuinelySynchronous(IEncryptionKeyProvider provider)      → bool
      — true when provider implements ISynchronousEncryptionKeyProvider directly, OR provider is a
        CachedEncryptionKeyProvider whose .Inner also (recursively) satisfies this same check; false for
        every other case, including any unrecognized third-party decorator type — the check fails toward
        requiring the *Async overloads, never toward silently blocking. This is a STATIC PROVIDER-IDENTITY
        CHECK, never a per-call cache-warmth test — a CachedEncryptionKeyProvider wrapping a KMS-backed
        inner provider always returns false, even on a call that would in fact hit a warm cache entry, since
        the very next call could just as easily miss. Computed once, at AesGcmEncryptionService construction
        time, and cached (the registered provider instance is immutable for the service's lifetime) — never
        re-evaluated per call

IEncryptionKeyProviderProbe  (P-487/WO-080, shipped — additive, opt-in, distinct from IEncryptionKeyProvider/IEnvelopeEncryptionProvider)
    ProbeAsync(CancellationToken ct = default)                  → Task<EncryptionKeyProviderHealth>
    — mirrors 07.Messaging's IMessageBusProbe/MessageBusHealth shape exactly (plain Task<THealth>, never
      ValueTask, never Result<T>) — chosen over 17.Workflows's Task<Result<WorkflowServiceHealth>> and
      19.Scheduling's zero-I/O bare Task<T> because this probe is genuinely I/O-bound and must never throw
      for an ordinary reachability failure. Implemented only by a provider with a real external dependency
      worth checking (e.g. a KMS) — a config-based/null provider has nothing to probe and is never required
      to implement this. Ships no default implementation — same "consumer implements" shape as
      IEncryptionKeyProvider. The implementation MUST NOT perform a cryptographic operation (wrap/unwrap/
      sign/verify) — those register as real key usage in a KMS's own audit trail — a cheap read-only
      metadata call is the correct shape. Unlike IEncryptionKeyProvider/IEnvelopeEncryptionProvider, an
      ordinary reachability failure must NOT propagate as a thrown exception — it is reported as
      EncryptionKeyProviderHealth.IsHealthy = false instead, mirroring every other readiness-probe primitive
      on this platform. 01.Core ships this probe primitive only, never an IHealthCheck — wiring into
      AddHealthChecks() is 13.ServiceDefaults's concern (root Phase Backlog P-449)

EncryptionKeyProviderHealth  (sealed record — P-487/WO-080, shipped)
    .IsHealthy                                                  → bool
    .Description                                                → string?                 (null when healthy)

CryptographicKey  (sealed record)
    .Id                                                         → string
    .Material                                                   → byte[]                  (32 bytes for AES-256)

IAsymmetricSignatureService  (BREAKING as of P-493/WO-081, SHIPPED — gained async members, retained sync members newly gated)
    Sign(byte[] data, string keyId)                             → byte[]                  (RETAINED; gated by AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous — throws NotSupportedException, directing to SignAsync, when the registered IAsymmetricKeyProvider is not marked ISynchronousAsymmetricKeyProvider. BLOCKS A REAL THREAD via .GetAwaiter().GetResult() when the check passes but the provider is genuinely network-bound — this can only happen if a provider author incorrectly self-asserts the marker)
    Verify(byte[] data, byte[] signature, string keyId)         → bool                    (RETAINED; same gating as Sign; RsaSignatureService now applies the same EnsureMinimumKeySize check Sign already applied to Sign-only — P-493 closes a prior silent accept-anything gap on the more security-sensitive operation. EcdsaSignatureService gained a wholly NEW minimum-key-size check on both Sign and Verify — it had none at all before this phase, see EcdsaSignatureService below)
    SignAsync(byte[] data, string keyId, CancellationToken ct = default)           → ValueTask<byte[]>   (P-493/WO-081, SHIPPED, additive)
    VerifyAsync(byte[] data, byte[] signature, string keyId, CancellationToken ct = default) → ValueTask<bool>   (P-493/WO-081, SHIPPED, additive)
    — a genuine BCL limitation, recorded explicitly rather than glossed over: System.Security.Cryptography.
      RSA/ECDsa's SignData/VerifyData have NO async overload anywhere in the BCL — so even inside SignAsync/
      VerifyAsync, once the (now-async) key resolution completes, the actual cryptographic sign/verify call
      against the returned RSA/ECDsa instance is inherently synchronous. For a remote-KMS-backed key
      (SharedKernel.Cryptography.KeyVault.Azure, P-494), that means the sign/verify call itself still performs
      a real, unavoidable blocking network round trip internally — P-493 makes KEY RESOLUTION async, never the
      signing primitive itself. Do not assume SignAsync/VerifyAsync are fully non-blocking end to end for
      every provider

RsaSignatureService  (sealed class, implements IAsymmetricSignatureService)
    — RSA, 2048-bit minimum, PSS padding, SHA-256
    — P-493/WO-081 (SHIPPED): stops disposing the RSA instance IAsymmetricKeyProvider hands back — the
      `using` around it is removed; the instance is NOT caller-owned. Fixes a latent defect (a using-scoped
      RSA disposes a key instance the provider may still hold and reuse — an immediate ObjectDisposedException
      on the very next call under any sensible caching/pooling provider) that was invisible until this phase
      because no IAsymmetricKeyProvider implementation shipped anywhere in the platform before it
    — the existing 2048-bit EnsureMinimumKeySize check (previously Sign-only) now also runs inside Verify
      (P-493/WO-081, SHIPPED) — parity fix, no size-threshold change

EcdsaSignatureService  (sealed class, implements IAsymmetricSignatureService)
    — ECDSA on the P-256 curve, SHA-256
    — P-493/WO-081 (SHIPPED): same disposal-ownership fix as RsaSignatureService
    — P-493/WO-081 (SHIPPED): gained a BRAND-NEW 256-bit minimum-key-size check (EnsureMinimumKeySize),
      applied to both Sign and Verify — this is NOT an extension of a pre-existing check the way RSA's is;
      EcdsaSignatureService had NO minimum-key-size check anywhere before this phase, a silent
      accept-any-curve-size gap now closed

IAsymmetricKeyProvider  (BREAKING as of P-493/WO-081, SHIPPED — the prior synchronous shape was removed outright)
    GetRsaKeyAsync(string keyId, CancellationToken ct = default)   → ValueTask<RSA>        (throws KeyNotFoundException, propagated through the ValueTask, if unknown — unchanged failure shape from the removed sync member)
    GetEcdsaKeyAsync(string keyId, CancellationToken ct = default) → ValueTask<ECDsa>      (same KeyNotFoundException shape)
    — implemented by the consuming service (Key Vault, certificate store, environment config); mirrors IEncryptionKeyProvider
      for the asymmetric-signing case. SharedKernel.Cryptography ships no default implementation and holds no key material.
      Introduced during P-207 implementation — required because IAsymmetricSignatureService.Sign/Verify take only a `keyId`
      string with no resolution mechanism defined; this is the resolver RsaSignatureService/EcdsaSignatureService depend on.
    — the prior synchronous GetRsaKey(string)/GetEcdsaKey(string) members were REMOVED OUTRIGHT (P-493/WO-081,
      SHIPPED) — not kept as a parallel overload, mirroring P-446's IEncryptionKeyProvider precedent
      exactly. A synchronous/config/certificate-backed implementer migrates mechanically by returning an
      already-completed `new ValueTask<RSA>(...)`/`new ValueTask<ECDsa>(...)`, matching P-446's precedent
    — the returned RSA/ECDsa instance is NOT caller-owned (P-493/WO-081, SHIPPED) — RsaSignatureService/
      EcdsaSignatureService never call Dispose on it

ISynchronousAsymmetricKeyProvider : IAsymmetricKeyProvider  (zero-member marker interface — P-493/WO-081, SHIPPED)
    — the asymmetric-signing analog of ISynchronousEncryptionKeyProvider; implemented ONLY by a provider that
      can genuinely guarantee GetRsaKeyAsync/GetEcdsaKeyAsync never perform a blocking network/IPC round trip.
      SharedKernel.Cryptography.KeyVault.Azure's AzureKeyVaultAsymmetricKeyProvider (P-494, SHIPPED)
      NEVER implements this — it always performs genuine network I/O (confirmed: `AsymmetricKeyProviderCapabilities.
      IsGenuinelySynchronous` returns false against it, and its sync `Sign`/`Verify` throw `NotSupportedException`)

AsymmetricKeyProviderCapabilities  (static class — P-493/WO-081, SHIPPED)
    IsGenuinelySynchronous(IAsymmetricKeyProvider provider)      → bool
      — a DIRECT marker check only (`provider is ISynchronousAsymmetricKeyProvider`) — no decorator-unwrapping
        logic exists, since no caching decorator exists for IAsymmetricKeyProvider as of this phase. A future
        decorator, if ever added, must extend this helper the same recursive-unwrap way P-492 extended
        EncryptionKeyProviderCapabilities for CachedEncryptionKeyProvider

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

ITotpReplayGuard  (SHIPPED, P-451/WO-069; shape change SHIPPED BREAKING as of P-514/WO-083)
    TryMarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default) → ValueTask<bool>   (P-514/WO-083, SHIPPED — REPLACES HasBeenUsedAsync + MarkUsedAsync below)
    — implemented by the consuming service (in-memory for single-instance dev, Redis-backed for production
      multi-replica); mirrors 12.Security.Oidc's DPoP replay-check seam — never a direct 02.Caching reference
      from this package; SharedKernel.Cryptography ships no default implementation
    — ATOMIC REPLAY MARKING (P-514/WO-083, SHIPPED, BREAKING): the prior two-step HasBeenUsedAsync(check)+
      MarkUsedAsync(mark) shape was a textbook TOCTOU — two concurrent VerifyAsync calls presenting the same
      valid code could BOTH observe "not yet used" before either marked it used, so both passed. Replaced by
      one atomic member, TryMarkUsedAsync, returning true only when THIS call is the first to mark (identityKey,
      code) used (a fresh code, now consumed) and false when it was already marked (a replay) — the identical
      compare-and-set/atomic-reservation shape 18.Idempotency's Redis (SET NX PX) and EF Core (INSERT ... ON
      CONFLICT DO NOTHING) stores already use for this exact class of problem. Proven by a genuine concurrency
      test (T-81, Barrier-synchronized, up to 50 simultaneous callers against a real ConcurrentDictionary.TryAdd-
      backed guard — never an NSubstitute mock, since a mock with canned returns cannot demonstrate atomicity).
      The prior members:
        HasBeenUsedAsync(string identityKey, string code, CancellationToken ct = default)          → ValueTask<bool>   [REMOVED, P-514/WO-083]
        MarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default) → ValueTask   [REMOVED, P-514/WO-083]
    — CORRECTED BLAST RADIUS (P-514/WO-083, SHIPPED): the dispatched brief for this phase claimed "zero
      migration cost" on the premise that no in-repo consumer existed yet — VERIFIED FALSE against real
      repository state, at design time AND again during implementation. 12.Security.Totp is confirmed ALREADY
      SHIPPED (contradicting both the brief and this root CLAUDE.md's own now-corrected "Queued, WO-069/P-452"
      line for that domain), and THREE real, already-shipped consumers require their own migration, all outside
      this file's jurisdiction:
        1. 16.Testing/SharedKernel.Testing/Cryptography/FakeTotpReplayGuard.cs — a shipped ITotpReplayGuard
           implementer of the removed two-member shape (companion migration: 16.Testing, not yet dispatched
           at time of writing)
        2. 12.Security.Totp/SharedKernel.Security.Totp.Tests/Challenge/FakeTotpReplayGuard.cs — an independent,
           test-local duplicate implementer of the same removed shape (companion migration: 12.Security, not
           yet dispatched at time of writing)
        3. 12.Security.Totp/Challenge/TotpChallengeService.cs (PRODUCTION source, NOT a test double) — calls
           TotpVerifier.VerifyAsync(identityKey, secret, code, ct) with ct passed POSITIONALLY as the 4th
           argument. Because the four new optional parameters below are inserted before ct exactly as designed,
           that positional argument now binds to the new int digits parameter instead of CancellationToken ct
           and FAILS TO COMPILE (CS1503 — CancellationToken has no implicit conversion to int). THIS WAS NOT
           ANTICIPATED BY THE ORIGINAL DESIGN — the design's own claim that TotpChallengeService.VerifyAsync
           "keeps compiling unchanged" was verified FALSE for this exact call site during implementation. Found
           by reading the real 12.Security.Totp source, not by trusting the design's own prior claim. Fix is
           trivial (pass ct by name: ct: ct) but is out of this package's jurisdiction — it is 12.Security's own
           companion migration to make, alongside its FakeTotpReplayGuard migration above (item 2)

ITotpAttemptThrottle  (SHIPPED, P-514/WO-083, new — additive)
    IsThrottledAsync(string identityKey, CancellationToken ct = default)     → ValueTask<bool>
    RecordAttemptAsync(string identityKey, CancellationToken ct = default)  → ValueTask
    — a documented attempt-throttling seam for RFC 4226 §7.3's rate-limiting recommendation, mirroring
      ITotpReplayGuard's own no-default-implementation shape exactly: implemented by the consuming service,
      SharedKernel.Cryptography ships no default. Deliberately NEVER wired into TotpVerifier's constructor
      (which stays exactly two parameters) — rate-limiting attempts is the caller's own concern (it decides
      lockout responses, HTTP 429 shaping, etc.), the same "ships uninvolved, consumer composes" pattern this
      package already uses for ITotpReplayGuard itself and 12.Security.Oidc's DPoP replay-check/
      ITokenRevocationCheck seams — this keeps TotpVerifier's constructor signature stable, adding zero further
      blast radius beyond ITotpReplayGuard's own shape change. Not registered by AddSharedKernelCryptography.

TotpVerifier  (sealed class — SHIPPED, P-451/WO-069; VerifyAsync's new optional parameters SHIPPED as of P-514/WO-083)
    VerifyAsync(string identityKey, byte[] secret, string code, int digits = 6, int stepSeconds = 30, int driftWindow = 1, HotpAlgorithm algorithm = HotpAlgorithm.Sha1, CancellationToken ct = default) → ValueTask<bool>   (P-514/WO-083 signature — the four new parameters are optional, inserted before the existing trailing ct, defaulted to the exact values the two removed DefaultStepSeconds/DefaultDriftWindow constants used. Source-compatible for a NAMED-argument ct call site; a call passing ct POSITIONALLY as the 4th argument now fails to compile — see 12.Security.Totp's TotpChallengeService.cs finding above, a real instance of exactly this trap)
    — composes ITotpGenerator.ValidateCode + ITotpReplayGuard; returns false on an invalid code OR a code
      already used within its validity window; calls the new atomic TryMarkUsedAsync only after a fresh valid
      code (P-514/WO-083 — replaces the old HasBeenUsedAsync-check-then-MarkUsedAsync-call sequence, closing the
      TOCTOU) — this is the type that actually prevents double-acceptance of one code, not ValidateCode itself
      (which stays pure/stateless by design)
    — CONFIG-CONSISTENT REPLAY WINDOW (P-514/WO-083, SHIPPED): the two hardcoded DefaultStepSeconds=30/
      DefaultDriftWindow=1 private constants are REMOVED entirely, not merely bypassed. digits/stepSeconds/
      driftWindow/algorithm are threaded straight through to ITotpGenerator.ValidateCode, and the replay window
      passed to TryMarkUsedAsync is computed from those SAME actual values — stepSeconds * (2 * driftWindow + 1)
      — instead of always assuming the defaults regardless of what was actually validated against. At the
      defaults this is still 90 seconds, unchanged (proven by a dedicated parity test); a caller validating
      against a non-default configuration (e.g. stepSeconds=60, driftWindow=2) now gets a replay window that
      genuinely matches what it validated (300 seconds, proven by T-82), not a window silently computed against
      the wrong assumption

RecoveryCodeGenerator  (sealed class — SHIPPED, P-451/WO-069)
    GenerateCodes(int count = 10, int lengthBytes = 5)          → IReadOnlyList<string>   (via ISecureRandomGenerator)
    — generates plaintext backup codes shown once to the user; NEVER persists or hashes them itself — hashing
      at rest via the existing IOneWayHasher before persistence is the consuming service's responsibility,
      exactly like any other secret

CryptographyOptions  (bound via IOptions<T>; validated via SharedKernel.Configuration's AddValidatedOptions)
    .Pbkdf2Iterations                                           → int                     (default 600_000; floor MinimumPbkdf2Iterations (100,000) as of P-512/WO-083, shipped — [Range(MinimumPbkdf2Iterations, int.MaxValue)], enforced by the already-wired ValidateOnStart path, no new IValidateOptions<T> needed. Previously [Range(1, int.MaxValue)] — a configured value of 1 passed validation cleanly, defeating the point of a slow KDF)
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
    — (P-521/WO-083, SHIPPED) IbanValidator/IsoCurrencyValidator/IsoCountryValidator each expose a documented
      RegistryAsOf as-of/registry-version public constant ("Reviewed WO-067/P-443, 2026-09-02" — an honest
      last-reviewed marker, not a formal ISO/SWIFT registry version number, since none of ISO 13616/4217/3166
      publish one in that shape); IbanValidator.Validate/.IsValid gained an optional trailing
      allowFallbackForUnknownCountry parameter (default false, unchanged behavior) that, when explicitly true,
      falls back to mod-97-only validation (still bounded by ISO 13616's general 34-character length and
      alphanumeric-BBAN shape) for a country the 78-entry table does not recognize instead of hard-rejecting
      it — trading away only country-specific length checking, never checksum correctness. The `.FluentValidation`
      adapter's matching `.MustBeValidIban(allowFallbackForUnknownCountry:)` overload remains out of scope

LeiValidator / AbaRoutingNumberValidator / SepaCreditorIdentifierValidator  (static classes)
    — (P-525/WO-083, design-locked, implementation pending) the identical dual-mode IsValid/Validate + Guard.Against.*
      shape as every validator above: LeiValidator (ISO 17442 mod-97-10 over a 20-character alphanumeric
      identifier), AbaRoutingNumberValidator (9-digit US routing number, (3,7,1)-weighted checksum),
      SepaCreditorIdentifierValidator (country + check digits + business code + national identifier, an
      IbanValidator-style rearrange-and-mod-97 approach)

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
    — (P-525/WO-083, design-locked, implementation pending) gains ValidationErrorCodes.Lei / .AbaRoutingNumber /
      .SepaCreditorIdentifier nested classes, following this exact same convention

GuardValidationExtensions  (static class — Guard.Against.* extensions on IGuardClause, functional path only;
    Guard.Throw.* parity is intentionally out of scope — the Guard.Throw nested class (now living in
    SharedKernel.Core, under the unchanged SharedKernel.Guards namespace, since P-505/WO-082) is hardcoded
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
    MustBeValidLei<T>(this IRuleBuilder<T, string>)               → IRuleBuilderOptionsConditions<T, string>
    MustBeValidAbaRoutingNumber<T>(this IRuleBuilder<T, string>)  → IRuleBuilderOptionsConditions<T, string>
    MustBeValidSepaCreditorIdentifier<T>(this IRuleBuilder<T, string>) → IRuleBuilderOptionsConditions<T, string>
    — (P-525/WO-083, design-locked, implementation pending) the three Lei/AbaRoutingNumber/SepaCreditorIdentifier
      rules follow this exact same Custom(...)-based pattern
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
AzureKeyVaultEncryptionKeyProvider  (sealed class, implements IEncryptionKeyProvider + IEnvelopeEncryptionProvider + IEncryptionKeyProviderProbe)
    — direct-retrieval mode (IEncryptionKeyProvider) is built INTERNALLY ON TOP OF the envelope-wrap mode
      (IEnvelopeEncryptionProvider): GetCurrentKeyAsync/GetKeyAsync resolve through GenerateDataKeyAsync/
      UnwrapDataKeyAsync, exposing only the already-in-memory plaintext data key as CryptographicKey.Material
      — never a second, parallel raw-export code path (Azure Key Vault Keys does not export raw HSM-protected
      key material by default). The vault's own master key material never crosses the process boundary either way
    — envelope-wrap mode (IEnvelopeEncryptionProvider) is the vendor-idiomatic path, backed by
      CryptographyClient.WrapKeyAsync/UnwrapKeyAsync (RSA-OAEP or AES-KW depending on key type)
    — fails closed: any Azure SDK exception (unreachable vault, RequestFailedException for permission/auth
      failure) propagates directly from every member EXCEPT ProbeAsync — no silent fallback; GetKeyAsync also
      translates a Key Vault "not found" (HTTP 404, RequestFailedException.Status == 404) into null, per its
      documented "retired or unknown" contract — every other failure (auth, unreachable, non-404) still throws
    — CONNECTION REUSE (P-496/WO-081, SHIPPED): a CryptographyClient is resolved at most once per distinct
      (Azure key name, key version) pair — cached in an Internal.SingleFlightCache<string, ResolvedAzureKey>
      (P-511/WO-083, shipped — replaced the original ConcurrentDictionary<string, Lazy<Task<ResolvedAzureKey>>>
      shape to close the cross-caller-cancellation-leak defect; see below)
      — never constructed inside a per-call code path. DELIBERATELY KEYED BY (name, version), NOT BY NAME ALONE
      like the sibling AzureKeyVaultAsymmetricKeyProvider's cache: UnwrapDataKeyAsync must be able to pin to the
      EXACT historical Azure key version that wrapped a given data key (which may differ from whatever is
      "current" today after an out-of-band Azure-side key rotation) — keying by name alone would silently
      collide two genuinely different key versions onto one cache entry
    — DURABLE VERSION REGISTRY (P-496/WO-081, SHIPPED): replaces the pre-P-496 process-lifetime "current data
      key" cache, which meant every process/pod/replica silently minted its OWN unique local AES-256 data key
      on first use — "current" was never a genuinely shared concept. CryptographicKey.Id is now a short opaque
      version tag ("v1", "v2", …). Each version is stored as its own Key Vault SECRET (via a NEW
      Azure.Security.KeyVault.Secrets SecretClient, confined to this package — see AddSharedKernelAzureKeyVaultCryptography
      below), holding a JSON VersionSecretPayload (MasterKeyId + Base64 WrappedKey) — serialized via a
      source-generated JsonSerializerContext, no reflection. A single shared "current version" pointer SECRET is
      read LIVE on every GetCurrentKeyAsync call (no internal caching of "which tag is current" — bounded-TTL
      caching of THAT concern is left to an externally-composed CachedEncryptionKeyProvider, unchanged from
      before) — so every replica genuinely converges on the same tag. GetCurrentKeyAsync THROWS
      InvalidOperationException if no version has EVER been minted — it deliberately NEVER auto-mints one on
      first use, since doing so would silently reintroduce the exact per-process "current key" accident this
      redesign exists to eliminate; call MintNewVersionAsync once during initial provisioning first
    — PER-TAG PLAINTEXT MEMOIZATION (P-496/WO-081, SHIPPED): an Internal.SingleFlightCache<string, byte[]>
      (P-511/WO-083, shipped — replaced the original ConcurrentDictionary<string, Lazy<Task<byte[]>>> shape;
      see below) memoizes an already-resolved version tag's plaintext data key for the remainder of the process's lifetime
      — a second GetKeyAsync/GetCurrentKeyAsync call citing an already-resolved tag costs ZERO further Key
      Vault calls (proven via a call-counting test double, T-73)
    — CANCELLATION-TOKEN LEAK FIX (P-511/WO-083, shipped): both P-496 caches above (_resolvedKeysByCacheKey/
      ResolveAsync and _plaintextKeysByTag/GetOrAddMemoizedPlaintextKeyAsync) reproduce the SAME captured-
      caller-token defect SharedKernel.Cryptography's CachedEncryptionKeyProvider had — each Lazy<Task<T>>
      factory closure captures the CancellationToken of whichever caller's GetOrAdd race happened to win, so
      that one caller's cancellation could fault or cancel every other concurrent caller awaiting the same
      shared Task. Fixed identically: a dedicated per-slot CancellationTokenSource never derived from a caller's
      token, plus Task.WaitAsync(callerCt) per caller, plus refcounted abandonment-triggered cancellation — see
      CachedEncryptionKeyProvider's own entry above for the full pattern description (documented once, not
      three times)
    — Internal.SingleFlightCache<TKey, TValue>  (internal sealed generic class, new file
      SharedKernel.Cryptography.KeyVault.Azure/Internal/SingleFlightCache.cs — P-511/WO-083, shipped): the
      shared, package-internal implementation of the CancellationTokenSource-per-slot +
      Task.WaitAsync(callerCt) + refcounted-abandonment-eviction pattern above, used by BOTH
      AzureKeyVaultEncryptionKeyProvider's two cache sites AND AzureKeyVaultAsymmetricKeyProvider's cache site
      (three sites, one implementation) — introduced specifically so this fix is guaranteed identical across
      all three rather than three independently hand-copied, drift-prone versions. GetOrAddAsync(key, factory,
      callerCt) resolves via an existing in-flight/succeeded slot or starts a new single-flight resolution;
      Seed(key, value) directly memoizes an already-known value (used by MintNewVersionAsync, which already
      knows the plaintext key it just minted, without forcing a redundant round trip through the factory
      path). An abandoned-or-failed slot is evicted immediately once every waiter departs; a successfully
      completed slot is never evicted just because its waiters happened to all leave — this is what makes
      Seed-then-never-touched entries permanent for the process's lifetime, matching the pre-P-511 behavior
      exactly for the success path
    — BACKWARD-READ COMPATIBLE, ADDITIVE/MINOR, NOT BREAKING (P-496/WO-081, SHIPPED): GetKeyAsync first checks
      whether keyId matches the new short tag shape ("v{N}"); if not, it falls back to decoding the pre-P-496
      self-decodable envelope shape (length-prefixed Base64 masterKeyId + wrapped-key blob) — any row already
      encrypted under the old shape stays decryptable indefinitely, no forced migration. UnwrapDataKeyAsync's
      masterKeyId parser ALSO defensively accepts both the new short "{azureKeyName}/{version}" shape (D-83)
      AND the legacy full Key Vault key identifier URI shape it always accepted — a deliberate robustness
      addition beyond the letter of the original design, since the envelope-wrap path's caller (not this
      provider) is the durable store for that value and could plausibly still hold an old-shaped one
    — MintNewVersionAsync(CancellationToken ct = default) → ValueTask<string> (P-496/WO-081, SHIPPED) — see the
      dedicated block below this one; kept here only as a pointer since it is documented once, not duplicated
    — ships ZERO caching of "which key material is current" beyond the durable registry itself — composes with
      SharedKernel.Cryptography's CachedEncryptionKeyProvider (P-446) externally for bounded-TTL caching rather
      than duplicating it; two independent caches with different TTL semantics must never both wrap the same
      provider. That external cache's working set is now genuinely bounded by live-key-version count (P-496).
    — ProbeAsync (IEncryptionKeyProviderProbe, P-487/WO-080, shipped) performs exactly one read-only
      key-metadata call (KeyClient.GetKeyAsync — the same call GenerateDataKeyAsync makes before it ever
      wraps anything), never a wrap/unwrap/sign/verify, and never touches Key Vault Secrets. This is the ONE
      deliberate, narrow exception to this class's fail-closed-via-exception contract: it catches every
      non-OperationCanceledException exception and returns EncryptionKeyProviderHealth.IsHealthy = false with
      .Description set from the exception message, rather than propagating

AzureKeyVaultCryptographyOptions  (bound via IOptions<T>; validated via SharedKernel.Configuration's AddValidatedOptions)
    .VaultUri                                                   → Uri
    .KeyNames                                                   → IReadOnlyDictionary<string, string>   (keyId → Azure Key Vault key name)
    .Credential                                                 → TokenCredential?   (defaults to Azure.Identity.DefaultAzureCredential when null)

AddSharedKernelAzureKeyVaultCryptography(IConfiguration configuration)
    → registers AzureKeyVaultCryptographyOptions (validated, ValidateOnStart) and AzureKeyVaultEncryptionKeyProvider
      as IEncryptionKeyProvider, IEnvelopeEncryptionProvider, and IEncryptionKeyProviderProbe (same singleton
      instance, three service-type registrations); does NOT register any caching decorator
    — P-494/WO-081 (SHIPPED): additionally registers a NEW, DISTINCT singleton,
      AzureKeyVaultAsymmetricKeyProvider, as IAsymmetricKeyProvider — never the same instance as
      AzureKeyVaultEncryptionKeyProvider (proven via Assert.NotSame in AzureKeyVaultCryptographyServiceCollectionExtensionsTests)

AzureKeyVaultAsymmetricKeyProvider  (sealed class, implements IAsymmetricKeyProvider only — P-494/WO-081, SHIPPED, depends on P-493)
    — deliberately a SEPARATE class from AzureKeyVaultEncryptionKeyProvider: signing keys (used directly for
      sign/verify) and wrap/unwrap keys are a different Key Vault key usage pattern even when both live in the
      same vault. Reuses the existing AzureKeyVaultCryptographyOptions.VaultUri/.KeyNames/.Credential — no new
      options type. Any entry in KeyNames may be used as a signing keyId (unlike AzureKeyVaultEncryptionKeyProvider,
      this class has no "current key" concept)
    — GetRsaKeyAsync/GetEcdsaKeyAsync back Azure Key Vault Keys' REMOTE sign/verify operations
      (CryptographyClient's hash-taking Sign(SignatureAlgorithm, byte[] digest, ct)/Verify(SignatureAlgorithm,
      byte[] digest, byte[] signature, ct) overloads — CONFIRMED VIA DIRECT REFLECTION against the installed
      4.7.0 assembly, NOT CryptographyClient.SignData/VerifyData, which take raw data and are the wrong overload
      for a SignHash/VerifyHash-shaped override — see KeyVaultRsaKey/KeyVaultEcdsaKey below) rather than
      exporting private key material Key Vault does not release. Resolves each distinct Azure key name's
      KeyVaultKey metadata (KeyType; KeySize derived via JsonWebKey.ToRSA(false)/.ToECDsa(false) — public
      material only, disposed immediately after reading .KeySize) and CryptographyClient exactly once, cached in
      an Internal.SingleFlightCache<string, ResolvedAzureKey> (P-511/WO-083, shipped — replaced the original
      ConcurrentDictionary<string, Lazy<Task<ResolvedAzureKey>>> shape) keyed by Azure key name (including
      clearing a failed entry so the next call retries) — never constructed per call, deliberately baking in the
      connection-reuse pattern P-496 (SHIPPED) later applied to the sibling AzureKeyVaultEncryptionKeyProvider,
      rather than repeating that defect twice
    — CANCELLATION-TOKEN LEAK FIX (P-511/WO-083, shipped — a scope correction discovered while auditing
      the two sites above, not named in that phase's original dispatched brief): this class's own
      _resolvedKeysByAzureKeyName/ResolveAsync cache has the IDENTICAL captured-caller-token defect as
      CachedEncryptionKeyProvider and AzureKeyVaultEncryptionKeyProvider's two caches — fixed with the same
      dedicated-CancellationTokenSource + Task.WaitAsync(callerCt) + refcounted-abandonment pattern
    — an unrecognized keyId throws KeyNotFoundException purely locally (no Azure call attempted), per
      IAsymmetricKeyProvider's documented contract; a keyId resolving to the wrong Azure key type (RSA requested
      against an EC key or vice versa) throws InvalidOperationException
    — NEVER implements ISynchronousAsymmetricKeyProvider — always performs genuine network I/O
    — ctor(IOptions<AzureKeyVaultCryptographyOptions> options, KeyClient? keyClient)  (internal, P-511/WO-083,
      shipped — test-seam only, mirrors AzureKeyVaultEncryptionKeyProvider's own existing internal test-seam
      constructor exactly): added purely so T-78's cross-caller-cancellation concurrency test can substitute a
      holdable/cancellable fake KeyClient without a reachable Azure Key Vault; the public single-parameter
      ctor is unaffected and is all production DI wiring (AddSharedKernelAzureKeyVaultCryptography) ever
      resolves — a null keyClient falls back to the real Azure SDK client, exactly matching the public ctor

KeyVaultRsaKey : RSA / KeyVaultEcdsaKey : ECDsa  (internal sealed — P-494/WO-081, SHIPPED)
    — thin subclasses returned by GetRsaKeyAsync/GetEcdsaKeyAsync; the delegation to CryptographyClient's real
      SYNCHRONOUS sign/verify methods (not a .GetAwaiter().GetResult() bridge over the async ones — RSA/ECDsa's
      BCL surface gives this class no async entry point to bridge from in the first place) MUST override
      SignHash/VerifyHash, NOT SignData/VerifyData — discovered during P-493's own test-double implementation:
      RSA.SignData/VerifyData and ECDsa.SignData/VerifyData are non-virtual convenience methods that hash the
      input and then call SignHash/VerifyHash internally; attempting `override` on SignData/VerifyData directly
      fails to compile (CS0506). So RsaSignatureService/EcdsaSignatureService (P-493) consume it through the
      EXACT SAME contract as a local key, with ZERO changes to those two classes beyond what P-493 already
      introduced — confirmed empirically, this is the phase's own headline acceptance criterion, not merely
      asserted
    — maps the fixed (HashAlgorithmName, RSASignaturePadding)/(HashAlgorithmName) combination each signature
      service actually requests to the matching Azure SignatureAlgorithm — PS256 for RSA+SHA-256+PSS (matching
      RsaSignatureService's fixed choice), ES256 for ECDSA+SHA-256 on the P-256 curve (matching
      EcdsaSignatureService's fixed choice) — throwing NotSupportedException for any other combination; this is
      NOT a general-purpose algorithm-negotiation surface. ECDsa.SignHash(byte[] hash) carries NO algorithm
      parameter at all (unlike RSA's overload) — KeyVaultEcdsaKey discriminates on the 32-byte SHA-256 digest
      length instead, throwing NotSupportedException for any other length
    — GOTCHA discovered and fixed during implementation: AsymmetricAlgorithm (RSA's/ECDsa's own base class)
      declares an inherited instance `string SignatureAlgorithm` property, which shadows the
      Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm TYPE name for unqualified identifier
      resolution inside a class deriving from RSA/ECDsa (CS0120/CS1061). Fixed via a
      `using AzureSignatureAlgorithm = Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm;` alias in
      both files — any future `: RSA`/`: ECDsa` subclass in this platform referencing the Azure SDK's
      SignatureAlgorithm type will hit the identical collision
    — ExportParameters/ImportParameters throw NotSupportedException on both (GenerateKey too, for ECDsa) —
      private key material NEVER crosses the process boundary; KeySize's setter also throws
    — XML docs state IN CAPITALS that SignHash/VerifyHash perform a real, unavoidable blocking network call —
      carrying forward IAsymmetricSignatureService's own P-493 BCL-limitation warning at the exact point it
      becomes concretely observable

AzureKeyVaultEncryptionKeyProvider — hardening (P-496/WO-081, SHIPPED, depended on P-494)
    — connection reuse, the durable version registry, MintNewVersionAsync, backward-read compatibility, and
      the per-tag memoization are all documented inline on AzureKeyVaultEncryptionKeyProvider above — not
      duplicated here. Two implementation-time refinements over the ORIGINAL design text, both because the
      literal wording would have been either incorrect or unnecessarily narrow (found by verifying against
      real requirements while implementing, not merely following the design brief — the same discipline P-494's
      D-76 correction established):
        1. The connection-reuse cache is keyed by (Azure key name, key VERSION), not by Azure key name alone as
           originally worded — UnwrapDataKeyAsync must pin to the EXACT historical Azure key version that
           wrapped a given data key, which can differ from "current" after an out-of-band Azure-side key
           rotation; keying by name alone would silently collide two real key versions onto one cache slot
        2. UnwrapDataKeyAsync's masterKeyId parser accepts BOTH the new short shape and the legacy full URI
           shape (not new-shape-only as originally worded) — a low-cost defensive addition since the
           envelope-wrap path's caller, not this provider, is the durable store for that value
    — EnvelopeDataKey.MasterKeyId simplification (envelope-wrap path, DISTINCT from the direct-retrieval path
      above): since UnwrapDataKeyAsync's caller already supplies the actual WrappedKey bytes directly (never
      round-tripped through this provider's own durable store), MasterKeyId never needed the oversized
      self-decodable envelope shape at all — it is now the real (short) "{azureKeyName}/{version}" identifier.
      Required NO new durable registry — unlike the direct-retrieval path, the envelope path's CALLER is
      already the durable store for the wrapped bytes
    — CachedEncryptionKeyProvider's working set is now genuinely bounded: every replica resolving "current"
      down to the same shared, deliberately-minted tag (rather than one unique tag per process restart) is
      what actually bounds a wrapping CachedEncryptionKeyProvider's dictionary by live-key-version count (a
      small, deliberate, ops-driven number) instead of by pod-restart count (unbounded over a service's whole
      operational history)
    — TEST-ONLY internal constructor overload (AzureKeyVaultEncryptionKeyProvider(options, secureRandomGenerator,
      KeyClient?, SecretClient?, Func<Uri, TokenCredential, CryptographyClient>?)) plus a project-scoped
      InternalsVisibleTo to SharedKernel.Cryptography.KeyVault.Azure.Tests — enables T-73's call-counting test
      doubles (built by SUBCLASSING the real, non-sealed, protected-parameterless-ctor Azure SDK client types
      directly, not a bespoke abstraction) to exercise this class's real production code with no reachable Key
      Vault. Every parameter is unregistered in DI, with no default value, so MS.DI's automatic
      constructor-selection can never pick this overload for the real AddSharedKernelAzureKeyVaultCryptography
      registration — confirmed by construction, not merely asserted
```

### `SharedKernel.Cryptography.Argon2` — public surface (P-495/WO-081, SHIPPED — thirteenth package)

```
Argon2idOneWayHasher  (sealed class, implements IOneWayHasher)
    Hash(string secret)                                         → string   (generates a cryptographically random salt via ISecureRandomGenerator; emits the PHC string format below)
    Verify(string hash, string secret)                          → HashVerificationResult
    — self-describing output uses the REAL, INTEROPERABLE PHC string format —
      $argon2id$v=19$m=<memoryKb>,t=<iterations>,p=<parallelism>$<base64 salt>$<base64 subkey> — the
      industry-standard Argon2 hash-storage shape, deliberately NOT a bespoke encoding the way
      Pbkdf2OneWayHasher's format is (PBKDF2 has no equivalently universal standard string format; Argon2
      does, and departing from it would forfeit interoperability for no benefit)
    — Verify parses the PHC string, recomputes under the SAME parsed parameters, constant-time-compares the
      subkey (CryptographicOperations.FixedTimeEquals, never ==/SequenceEqual), and returns
      HashVerificationResult.SuccessRehashNeeded when the parsed m/t/p differ from the CURRENTLY-configured
      Argon2CryptographyOptions — the same rehash-on-parameter-change shape Pbkdf2OneWayHasher already uses.
      A malformed/foreign-algorithm string returns Failed, NEVER throws
    — SHIPPED (P-495): Verify's "never throws" guarantee also covers a STRUCTURALLY-valid PHC string whose
      embedded cost parameters Konscious itself rejects (e.g. a parsed m below its own internal 4 KiB floor) —
      confirmed via direct inspection of the Konscious source (NOT assumed from prose) that it throws
      InvalidOperationException/NotSupportedException for a parameter-floor/output-length violation, NEVER
      ArgumentOutOfRangeException; Verify's catch clause is written against the REAL exception types

Argon2CryptographyOptions  (bound via IOptions<T>; validated via SharedKernel.Configuration's AddValidatedOptions)
    .MemorySizeKb                                                → int   (default 19456 = 19 MiB)
    .Iterations                                                  → int   (default 2)
    .DegreeOfParallelism                                         → int   (default 1)
    — defaults cite OWASP Password Storage Cheat Sheet's CURRENT minimum recommended Argon2id configuration
      explicitly, mirroring Pbkdf2Iterations's "600,000 (OWASP 2023+ guidance)" precedent
    — SHIPPED (P-495): each property carries a REAL [Range] floor/ceiling, not a nominal one — MinMemorySizeKb
      (7168, the smallest `m` across OWASP's own four-row acceptable-configurations table), MinIterations (2,
      the smallest `t` across those same four rows), MinDegreeOfParallelism (1), each paired with a generous
      sanity ceiling (2 GiB / 10 / 16). Deliberately does NOT repeat CryptographyOptions.Pbkdf2Iterations's
      original `[Range(1, int.MaxValue)]` mistake (a floor in name only — `Pbkdf2Iterations: 1` passed startup
      validation cleanly; that fix is P-512/WO-083, out of this phase's own scope, but this phase's floor was
      designed from the start not to repeat it)

AddSharedKernelArgon2Cryptography(IConfiguration configuration)
    → registers Argon2CryptographyOptions (validated, ValidateOnStart) and Argon2idOneWayHasher as a
      KEYED-ONLY singleton — Argon2CryptographyServiceCollectionExtensions.Argon2idOneWayHasherKey = "Argon2id"
      — NEVER as the unkeyed IOneWayHasher default, mirroring GZipPayloadCompressor's keyed-only-no-unkeyed-
      registration precedent exactly. Pbkdf2OneWayHasher (registered by SharedKernel.Cryptography's own
      AddSharedKernelCryptography) remains the sole unkeyed IOneWayHasher default and the FIPS-mode choice —
      completely untouched by this package
```

**FIPS-mode is the deciding factor** between Argon2id and PBKDF2 on this platform: PBKDF2 stays in
FIPS-approved-algorithm territory (and remains the unkeyed default for exactly this reason); Argon2id is the
OWASP-preferred choice everywhere FIPS is not a hard constraint. The package README documents this explicitly.
Third-party dependency: `Konscious.Security.Cryptography.Argon2` — a maintained, pure-managed .NET
implementation, chosen over a native/libsodium-backed binding to avoid compounding this platform's
AOT-preferred posture with a P/Invoke dependency on top of an already-third-party choice; AOT status flagged,
not blocked on, mirroring the FluentValidation/Azure SDK precedent. Confined entirely to this package,
verified via the established `.nuspec`-inspection technique in `SharedKernel.Consumer.Tests`.
**SHIPPED (P-495):** pinned at 1.3.1 (latest stable, MIT license, actively maintained — verified directly
against its GitHub source and NuGet listing before pinning, not assumed); the packed `.nuspec` directly
inspected confirming exactly three dependencies (`SharedKernel.Cryptography`, `SharedKernel.Configuration`,
`Konscious.Security.Cryptography.Argon2`) and confirming `SharedKernel.Cryptography`'s own already-packed
`.nuspec` still carries zero `Konscious`/`Argon2` dependency.

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
    — (P-516/WO-083, design-locked, implementation pending) gains Seal()/IsSealed — AddTranslation throws
      InvalidOperationException once sealed; AddInMemoryLocalizationCatalog's DI factory auto-seals immediately
      after its configure callback returns, so the shipped DI path is thread-safe with zero consumer action

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
    — (P-518/WO-083, design-locked, implementation pending) both registrations convert from AddSingleton to
      TryAddSingleton — a deliberate, documented behavior inversion from "last call wins" to "first call wins"
      (the domain-wide TryAdd standardization); call exactly one of these two methods per service
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
- `SharedKernel.Primitives` carries exactly one NuGet dependency, `Microsoft.Extensions.DependencyInjection.Abstractions`, referenced solely for the optional `ClockExtensions.AddClock()` DI convenience extension — never described as "zero dependencies" anywhere; the shipped `<Description>` used to, corrected as of **P-517/WO-083, shipped**. Removing `AddClock()` to make a literal "zero dependencies" claim true was considered and declined — it is live, widely-referenced public API, and removing shipped API to fix a documentation error is the wrong trade.
- **(P-520/WO-083, shipped)** Every shipped package's `<PackageReleaseNotes>`/`<Description>` must describe present-tense/lockstep-versioning-accurate reality — never a per-package version number (e.g. "v2.0.0:", "1.1.0:"), which stopped meaning anything the moment the platform switched to one repo-wide MinVer-derived version (2026-08-25). This file's own Package Board must be kept in sync with each package's real Published/design-locked state — a board showing `○ Not started` for an already-`●`-Published package is exactly the staleness class this rule exists to prevent.
- **(P-505/WO-082, shipped)** The former `SharedKernel.Guards` package (zero NuGet dependencies of its own) is now part of `SharedKernel.Core`, in the `SharedKernel.Guards` namespace (its `.Clauses`/`.Descriptions` sub-namespaces were folded into it before the first publish, so one `using` reaches every guard) — `SharedKernel.Core` itself still carries no third-party NuGet dependency, confirmed by direct `.nuspec` inspection of the repacked assembly (`SharedKernel.Primitives` remains its sole dependency).
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
- **(P-515/WO-083, design-locked, implementation pending)** Reaching `SmartEnum<TEnum,TValue>.FromValue`/`TryFromValue`/`FromName`/`List` through an INHERITED static call alone does not reliably trigger `TEnum`'s own static constructor (per ECMA-335 semantics — the member is physically declared on the base type). `SmartEnum<TEnum,TValue>` must force `TEnum`'s cctor exactly once, via a private static field on `SmartEnum<TEnum,TValue>` itself whose initializer calls `RuntimeHelpers.RunClassConstructor(typeof(TEnum).TypeHandle)` — never a per-call check inside `FromValue`/`TryFromValue`/`FromName`, which would reintroduce cost on the hot path.
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
- **(P-519/WO-083, shipped; registration idiom CORRECTED by P-530/C-128)** The `AddValidatedOptions<TOptions, TValidator>` overloads must register `TValidator` via **`TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>())`** — never `TryAddSingleton`, and never plain `AddSingleton`. P-519 specified `TryAddSingleton` and that was wrong: `IValidateOptions<TOptions>` is a multi-implementation collection service (the options pipeline runs *every* registered validator for a type, not the first), so `TryAddSingleton` silently registers NOTHING whenever another validator for that options type already exists — measured, with a cross-property rule never running and invalid configuration starting the host cleanly. `TryAddEnumerable` still prevents the identical `(IValidateOptions<TOptions>, TValidator)` pair registering twice. These overloads must never change the behavior of the DataAnnotations-only overloads, which stay every existing consumer's unmodified default.
- **(P-530/C-129)** DataAnnotations validation in this package must be registered as an immutable pre-built `DataAnnotationValidateOptions<TOptions>` **instance**, guarded by a duplicate check keyed on the options **name**, rather than by calling the BCL's `.ValidateDataAnnotations()`. Two independent reasons, both measured: the BCL method uses a plain `AddSingleton`, so a second registration for one options type duplicates every failure message (4 instead of 2); and a name-blind `TryAddEnumerable` — the otherwise-correct idiom above — de-duplicates on implementation type, which every named instance shares, while `DataAnnotationValidateOptions<T>` is itself name-scoped and *skips* other names, so it would leave every named instance after the first entirely unvalidated. A pre-built instance is what makes the per-name check possible at all: a factory-registered descriptor exposes no inspectable name. **Generalizable: `TryAddEnumerable` is not a drop-in for `TryAddSingleton` when the service is keyed on something finer than its implementation type.**
- **(P-530/C-133)** `SharedKernel.Configuration`'s public overloads must carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, and each `TOptions`/`TValidator` type parameter must carry `[DynamicallyAccessedMembers]`. Configuration binding is genuinely reflective — the BCL's own `OptionsBuilder<T>.Bind` declares both attributes — and a generic library wrapper cannot escape it, because .NET's configuration-binding source generator intercepts `Bind` calls in the *calling* assembly and can never specialize a `Bind<TOptions>` that lives in a library. Suppressing those warnings instead of declaring them is prohibited here, and `aot` must not reappear in this package's `PackageTags`.
- **(P-530)** A `ValidateOnStart`-armed registration only fails fast under a real `IHost`. With a bare `ServiceCollection`/`BuildServiceProvider()` the failure defers to the first `.Value` read, so no doc in this domain may claim startup validation unconditionally. Likewise, once an `IOptionsMonitor<T>` has been resolved, a reload introducing an invalid value throws `AggregateException(OptionsValidationException)` out of `IConfigurationRoot.Reload()` itself — on the file-watcher's thread under `reloadOnChange: true`.
- **(P-518/WO-083, design-locked, implementation pending)** Every DI extension method in this domain must register its own services via `TryAddSingleton`/`TryAddKeyedSingleton`/`TryAddEnumerable` — never a plain `AddSingleton`/`AddKeyedSingleton` — so calling an `AddSharedKernelX()` method twice never double-registers, and a consumer registration made BEFORE the platform's call always wins. The one required exception: a service intentionally registered as a genuine multi-implementation collection (e.g. `SharedKernel.Validation`'s `INationalIdValidator`) must use `TryAddEnumerable(ServiceDescriptor.Singleton<TService, TImpl>(...))`, never `TryAddSingleton`, which would silently drop every implementation after the first.
- `IFeatureManager` is the only permitted feature-flag interface in consuming services — never inject `Microsoft.FeatureManagement.IFeatureManager` directly. This includes the variant/allocation path (P-298/WO-049): `GetVariantAsync` must never leak a `Microsoft.FeatureManagement` type (e.g. `Variant`, `VariantAssignmentReason`) through `IFeatureManager`'s public surface — always the neutral `FeatureVariant` record.
- `IFeatureManager`'s variant surface (`GetVariantAsync`) is additive-only — the existing boolean `IsEnabledAsync` members and their behavior must never change as a side effect of adding variant support. `MicrosoftFeatureManagerAdapter` deliberately keeps discarding the caller-supplied `ct` on both `IsEnabledAsync` overloads even though the `IVariantFeatureManager` interface it is now built on technically accepts one where the previously-injected `IFeatureManager` did not — forwarding it would be an observable behavior change this rule forbids.
- `AddSharedKernelFeatureManagement(configuration)` must always pass its `configuration` parameter through to `Microsoft.FeatureManagement`'s own `AddFeatureManagement(...)` call **unmodified** — never `configuration.GetSection("FeatureManagement")` or any other pre-scoped subsection. Confirmed empirically (P-298/WO-049): pre-scoping silently makes the Microsoft Feature Management variant/allocation schema (`feature_management:feature_flags`) unreachable with no error or warning, because it lives under a different, unscoped root key that a pre-scoped `IConfiguration` can no longer see.
- Guard extensions return `Error?` — **null means the guard passed**, non-null means violation. Never use `Error.None` as the "passed" sentinel in guard returns; use actual `null` so callers can distinguish cleanly.
- `Guard.Throw.*` methods are thin wrappers: call the matching `Against.*` extension, throw `DomainException(error)` if the result is non-null, otherwise return. No independent logic.
- `IGuardClause` is a public marker interface with no members — `DefaultGuardClause` (the implementation) is `private sealed` to the `Guard` class. Callers must never reference `DefaultGuardClause` directly.
- `InvalidFormat` and `Email` guard extensions must use a **static cached `Regex`** (e.g., via `ConcurrentDictionary<string, Regex>` keyed by pattern) with a bounded `RegexOptions.Compiled` timeout — a new `Regex` instance must never be created per call.
- **(P-522/WO-083, design-locked, implementation pending — depends on P-505)** The pattern-keyed `Regex` cache backing `InvalidFormat`/`Email` must be bounded (an approximately-FIFO-evicting fixed capacity, e.g. 256 distinct patterns), never allowed to grow unboundedly — a soft, approximate bound honestly documented as such, not a strict LRU. Every existing call site passes a compile-time literal pattern and is never observably affected; the bound exists solely against a future call site that might ever derive a pattern from configuration or user input.
- Collection guards (`Empty`, `MaxCount`, `MinCount`) must enumerate the `IEnumerable<T>` source **at most once** per call — use `Count()` or a single materialization pass.
- `GuardDescriptions` is `internal` — it is not part of the public API and must not be exposed to consumers.
- `InvalidSmartEnum<TEnum, TValue>` must use `SmartEnum<TEnum, TValue>.TryFromValue` — no reflection, no `Enumeration<T>` or parallel type.
- `SharedKernel.Cryptography` has **zero third-party NuGet dependencies** — pure BCL `System.Security.Cryptography` only; references only `SharedKernel.Primitives` (for `Result<T>`/`Error`) and `SharedKernel.Configuration` (for `AddValidatedOptions`).
- **(P-524/WO-083, design-locked, implementation pending)** Every `byte[]` buffer this package (and `SharedKernel.Cryptography.Argon2`) allocates and fully owns the lifetime of — PBKDF2/Argon2id's `salt`/`subkey`/comparison buffers, `HmacSha256Signer.Verify`'s internal comparison buffer, and the intermediate `byte[]` conversions inside `AesGcmEncryptionService`'s `EncryptToString(Async)`/`DecryptToString(Async)` STRING overloads only — must be zeroed via `CryptographicOperations.ZeroMemory` immediately after last use. The `byte[]` returned DIRECTLY to a caller by `Encrypt`/`Decrypt`(`Async`)'s primary `byte[]`-based overloads must **never** be zeroed by this package — it is the caller's own needed output. No `IDisposable CryptographicKey` and no `ReadOnlySpan<char>`/`char[]` `IOneWayHasher` overloads — both were explicitly considered and declined (see `SharedKernel.Cryptography/README.md`'s "Key-Material Zeroization" section once shipped).
- **(P-526/WO-083, SHIPPED, docs-only)** A FIPS 140-3 posture statement lives in `SharedKernel.Cryptography/README.md`'s "FIPS 140-3 / Approved-Algorithm Posture" section — see that README for the full per-primitive breakdown (PBKDF2-HMACSHA256, AES-256-GCM, RSA/ECDSA at their documented minimums, HMACSHA256, and SHA-256 are FIPS-approved; Argon2id is not). The one gap every consumer must know without reading the README: RFC 4226/6238's DEFAULT `HotpAlgorithm.Sha1` (`HotpGenerator`/`TotpGenerator`/`TotpVerifier`) is NOT FIPS-approved for this purpose — a FIPS-constrained consumer must explicitly pass `HotpAlgorithm.Sha256`/`.Sha512`. Argon2id (`Argon2idOneWayHasher`) is also NOT FIPS-approved — a FIPS-constrained consumer must use the PBKDF2 default instead; `SharedKernel.Cryptography.Argon2/README.md` carries a short cross-reference back to this same posture statement. Zero `.cs` file changed by this phase — verified via `git status`/`git diff --stat` before closing it out.
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
- **(P-443/WO-067, SHIPPED)** `SharedKernel.Validation` must never be folded into the Guard Clause System's own home (`SharedKernel.Core` since P-505/WO-082, formerly the standalone `SharedKernel.Guards` package) — the Guard surface's value is deliberate minimalism (a generic precondition/argument-guard surface with no topic-specific catalog); a whole country/format-algorithm catalog belongs in its own package. `Guard.Against.*` extension methods for format validators live in `SharedKernel.Validation` (extending `IGuardClause` from the referencing side, confirmed to work exactly as designed — a marker interface's extension methods can be authored from any referencing package with zero changes to the defining package), never inside `SharedKernel.Core`'s Guard surface itself.
- **(P-443/WO-067, SHIPPED)** `Guard.Throw.*` parity is intentionally never added for format-validator guards — the `Guard.Throw` nested class (now living in `SharedKernel.Core`, under the unchanged `SharedKernel.Guards` namespace, since P-505/WO-082) is hand-enumerated and hardcoded inside that class; adding to it requires modifying `SharedKernel.Core` itself, out of `SharedKernel.Validation`'s reach and never requested by WO-067's acceptance criteria (functional `Against.*` path only).
- **(P-443/WO-067, SHIPPED)** `ValidationErrorCodes` is a package-local nested-static-class string-constant catalog living entirely inside `SharedKernel.Validation` — it must never be added as a new nested category under `SharedKernel.Primitives.ErrorCodes`. `ErrorCodes`'s own documented rule ("consuming packages may add local constants without forking the SharedKernel") already covers this; a whole country-algorithm error-code catalog must never bloat the platform's most-depended-upon primitives package.
- **(P-443/WO-067, SHIPPED)** `CardNetwork` is a plain `enum`, not a `SmartEnum<TEnum,TValue>` — BIN-range network detection carries no per-value behavior beyond the name, so the `SmartEnum` machinery would be pure ceremony here.
- **(P-443/WO-067, SHIPPED)** `IbanValidator`'s per-country length table and every shipped test vector (IBAN, PAN, TCKN) were verified against real published sources (canonical ISO/SWIFT/Wikipedia IBAN examples, Stripe's published test-card catalogue, the published TCKN checksum formula) rather than hand-constructed — a future validator added to this package should hold itself to the same bar rather than inventing a "valid" example to satisfy its own implementation.
- **(P-443/WO-067, SHIPPED)** `VatValidator` is a baseline format check only (2-letter prefix + 2-12 alphanumeric characters) — it performs no per-country checksum and must never be documented or extended to imply otherwise without a dedicated new phase.
- **(P-521/WO-083, SHIPPED)** `IbanValidator`/`IsoCurrencyValidator`/`IsoCountryValidator` each expose a documented `RegistryAsOf` as-of/registry-version constant. `IbanValidator`'s `allowFallbackForUnknownCountry` parameter defaults to `false` (today's exact hard-reject behavior, unchanged) — the mod-97-only fallback for an unrecognized country prefix is opt-in only, never the default, and it never weakens checksum correctness, only country-specific length checking (the general ISO 13616 34-character bound and alphanumeric-BBAN shape are still enforced in fallback mode).
- **(P-525/WO-083, design-locked, implementation pending)** `LeiValidator`/`AbaRoutingNumberValidator`/`SepaCreditorIdentifierValidator` must follow the exact dual-mode `IsValid`/`Validate` + `Guard.Against.*` shape every existing validator in this package uses, with new `ValidationErrorCodes` nested classes (never added to `SharedKernel.Primitives.ErrorCodes`) — and every test vector must be sourced from a real published reference, never hand-constructed, mirroring the rule directly above.
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
- **(P-516/WO-083, design-locked, implementation pending)** `InMemoryLocalizationCatalog.AddTranslation` must throw `InvalidOperationException` once the catalog is sealed (`Seal()`/`IsSealed`) — never silently no-op, never corrupt state. `AddInMemoryLocalizationCatalog`'s DI factory must call `Seal()` immediately after its `configure` callback returns, so the shipped DI path is thread-safe with zero consumer action required. `StringLocalizerLocalizationCatalog` has no equivalent mutator and is unaffected.
- **(P-491/WO-081, SHIPPED, BREAKING)** Every `ISymmetricEncryptionService` member's `associatedData` parameter is mandatory — no overload may default it to `null`/`Array.Empty<byte>()`. A caller with nothing to bind must pass `Array.Empty<byte>()` explicitly. `associatedData` must never be persisted inside `EncryptedPayload` or the packed `EncryptToString`/`DecryptToString` string — it must always be re-derivable by the caller from context available at decrypt time.
- **(P-491/WO-081, SHIPPED)** A mismatched `associatedData` at decrypt time must surface as `Result<byte[]>.Failure(Error.Unexpected(...))` — the identical failure shape as tamper/wrong-key/unknown-KeyId — never a new `ErrorCodes` constant and never a thrown `CryptographicException`.
- **(P-492/WO-081, shipped)** `ISynchronousEncryptionKeyProvider` is opt-in and author-asserted only — it must never be inferred from a provider's shape (e.g. "it doesn't obviously call a network API") and a KMS/HSM-backed provider must never implement it. `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous` must fail toward `false` (requiring `*Async`) for any provider type it does not specifically recognize — never toward `true`.
- **(P-492/WO-081, shipped)** `CachedEncryptionKeyProvider` must never itself implement `ISynchronousEncryptionKeyProvider` directly — its synchronous-safety is entirely a function of what its `.Inner` wraps, checked recursively by `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous`, never assumed from the decorator's own best-case (cache-hit) behavior.
- **(P-492/WO-081, shipped)** The sync-vs-async capability gate must be evaluated once, at construction time, and cached — never re-evaluated per call. This keeps the gate cheap enough that it introduces no measurable overhead on the hot synchronous path for the common config-backed provider.
- **(P-492/WO-081, shipped)** When gating multiple public sync members that internally delegate to one another (e.g. `EncryptToString` calling `Encrypt`), the capability check must be applied independently at each public entry point — never only at the innermost one — so every member's `NotSupportedException` names its own correct `*Async` counterpart instead of a less-accurate one borrowed from whatever it happens to delegate to internally.
- **(P-493/WO-081, shipped, BREAKING)** `IAsymmetricKeyProvider`'s two members are `GetRsaKeyAsync`/`GetEcdsaKeyAsync` (`ValueTask`-returning, `CancellationToken`-aware) — the prior synchronous members were **removed**, never kept as a parallel additive overload, mirroring `IEncryptionKeyProvider`'s P-446 precedent exactly.
- **(P-493/WO-081, shipped)** `RsaSignatureService`/`EcdsaSignatureService` must never call `Dispose` on an `RSA`/`ECDsa` instance returned by `IAsymmetricKeyProvider` — that instance is never caller-owned. `Verify` must apply the identical `EnsureMinimumKeySize` check `Sign` applies, for both algorithms — for RSA this extended an existing Sign-only 2048-bit check onto Verify too; for ECDSA this introduced a wholly new 256-bit check onto both members, since `EcdsaSignatureService` had no minimum-key-size check at all before this phase. A future divergence between `Sign`'s and `Verify`'s check requires a deliberate, stated reason, never a silent omission.
- **(P-493/WO-081, shipped)** `IAsymmetricSignatureService.SignAsync`/`VerifyAsync` make **key resolution** non-blocking only — they must never be documented or assumed to make the underlying cryptographic sign/verify call itself non-blocking, since `RSA`/`ECDsa` expose no async `SignData`/`VerifyData` in the BCL. Every provider whose signing call performs real I/O (P-494's Azure Key Vault provider included) must state this limitation in its own XML docs, not rely on this rule being remembered from `01.Core`.
- **(P-493/WO-081, shipped)** A test double proving "this service never disposes a provider-returned key instance" must genuinely fail if disposal happens — subclass `RSA`/`ECDsa` directly and throw from `Dispose(bool)`, returning the SAME cached instance across repeated calls (see `SharedKernel.Cryptography.Tests/Signing/DisposeGuardedKeys.cs`/`DisposeThrowingAsymmetricKeyProvider.cs`). A double that hands back a fresh clone per call (the shape `InMemoryAsymmetricKeyProvider` already used for other reasons) cannot catch this regression — a fresh instance being disposed never breaks the next call. `RSA`/`ECDsa`'s `SignData`/`VerifyData` are non-virtual convenience methods; a test subclass must override the real BCL extension points (`SignHash`/`VerifyHash`, plus `ExportParameters`/`ImportParameters`/`GenerateKey`) instead — overriding `SignData`/`VerifyData` directly does not compile.
- **(P-494/WO-081, shipped)** `AzureKeyVaultAsymmetricKeyProvider` must never implement `ISynchronousAsymmetricKeyProvider` — it always performs genuine network I/O, unlike a config/certificate-backed implementer. `KeyVaultRsaKey`/`KeyVaultEcdsaKey` must never export private key material via `ExportParameters`/`ImportParameters` — both throw `NotSupportedException` structurally, mirroring `AzureKeyVaultEncryptionKeyProvider`'s existing "vault master key material never crosses the process boundary" invariant.
- **(P-494/WO-081, shipped)** `AzureKeyVaultAsymmetricKeyProvider` must cache one `CryptographyClient` per distinct Azure key name from its first implementation — never construct one per call. This is deliberately not deferred to a later hardening pass the way `AzureKeyVaultEncryptionKeyProvider`'s equivalent defect was (P-496) — a defect known in advance must never be shipped a second time in a sibling class.
- **(P-494/WO-081, shipped)** A subclass of `RSA`/`ECDsa` (or `AsymmetricAlgorithm`) that needs the Azure SDK's `Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm` TYPE must alias it (e.g. `using AzureSignatureAlgorithm = Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm;`) — `AsymmetricAlgorithm` declares an inherited instance `string SignatureAlgorithm` property that shadows the type name for unqualified resolution inside the derived class body (`CS0120`/`CS1061`), discovered building `KeyVaultRsaKey`/`KeyVaultEcdsaKey`.
- **(P-494/WO-081, shipped)** `CryptographyClient.SignData`/`.VerifyData` take raw (unhashed) data; `CryptographyClient.Sign`/`.Verify` take an already-computed digest. A class overriding `RSA.SignHash`/`ECDsa.SignHash` (which receive only the digest, never the original data) must call `CryptographyClient.Sign`/`.Verify` — the hash-taking overloads — never `SignData`/`VerifyData`. Verified via direct reflection against the installed SDK assembly, not assumed.
- **(P-495/WO-081, SHIPPED)** `SharedKernel.Cryptography.Argon2`'s `Argon2idOneWayHasher` must be registered ONLY as a `"Argon2id"`-keyed singleton — never as the unkeyed `IOneWayHasher` default. `Pbkdf2OneWayHasher` remains the sole unkeyed default and the FIPS-mode-compatible choice; nothing in this package may alter that.
- **(P-495/WO-081, SHIPPED)** `Argon2idOneWayHasher`'s stored-hash format must be the standard PHC string format, never a bespoke encoding — this is the one deliberate departure from `Pbkdf2OneWayHasher`'s own custom-format precedent, justified because Argon2 (unlike PBKDF2) has a genuine, widely-interoperable standard string format worth preserving.
- **(P-495/WO-081, SHIPPED)** `Argon2idOneWayHasher.Verify`'s narrow catch clause is written against Konscious's REAL thrown exception types (`InvalidOperationException`/`NotSupportedException`), confirmed by direct source inspection — NOT `ArgumentOutOfRangeException`, which the design-lock pass's own prose had assumed. Any future upgrade of `Konscious.Security.Cryptography.Argon2` must re-verify this before trusting the catch clause still covers every parameter-floor violation.
- **(P-496/WO-081, design-locked)** `AzureKeyVaultEncryptionKeyProvider.GetKeyAsync` must remain backward-read-compatible with the pre-P-496 self-decodable envelope `keyId` shape for any `keyId` that does not match the new short-tag format — a service with existing encrypted rows must never be forced into a data migration by this hardening pass. `GetCurrentKeyAsync` always mints/returns the new short-tag shape for new encryption going forward; the two shapes are never mixed within one minted version.
- **(P-496/WO-081, design-locked)** `MintNewVersionAsync` must never be invoked automatically by this package on any schedule, timer, or startup hook — it is a callable-only operational surface. Automatic/policy-driven rotation scheduling remains explicitly out of scope for `01.Core`, mirroring `SharedKernel.DataPrivacy`'s declined cross-service erasure orchestrator and `06.Persistence`'s `IEncryptionRotationJob` precedent of leaving scheduling to the caller.
- **(P-496/WO-081, design-locked)** Every previously-minted key version's Key Vault secret must remain untouched and resolvable via `GetKeyAsync(oldTag)` indefinitely after a `MintNewVersionAsync` call — minting a new "current" version must never delete, overwrite, or otherwise disturb any prior version's stored wrapped-DEK secret.
- **(P-510/WO-083, shipped, SEVERE)** `ResultTry`'s default (no custom `onException`) exception mapping must never interpolate a caught exception's raw type/message into `Error.Message` — the fixed default message is the only text external callers may ever see through `Error.ToProblemDetails()`. The raw exception detail belongs on the ambient `Activity` (`Activity.Current?.AddException(exception)`), never on `Error` itself — `Error` stays exactly `(Code, Message, Type)` per the root state-map's ratified `⊘ DECLINED` "no metadata bag on `Error`" ruling. A caller-supplied `onException` mapper is never redacted or altered by this rule.
- **(P-510/WO-083, shipped)** Every `ResultTry` catch clause — including the custom-`onException` overloads — must exclude `OperationCanceledException` (`catch (Exception exception) when (exception is not OperationCanceledException)`). A genuine cancellation must always propagate as a thrown exception; it must never be converted into a `Result.Failure`, regardless of which mapper (default or caller-supplied) would otherwise handle it.
- **(P-511/WO-083, shipped)** Every `Lazy<Task<T>>`-shaped single-flight/memoization cache in this domain (`CachedEncryptionKeyProvider`, `AzureKeyVaultEncryptionKeyProvider`'s two caches, `AzureKeyVaultAsymmetricKeyProvider`'s cache) must drive its shared inner factory call from a `CancellationToken` genuinely detached from every individual caller — never from whichever caller's race happened to construct/win the cache slot. Each caller must observe cancellation of only ITS OWN await (`Task.WaitAsync(callerCt)`, never a token baked into the shared factory closure). A future single-flight cache added anywhere in `01.Core` must follow this exact pattern from its first implementation — this defect class has now been found and fixed in four separate places across two packages in one pass; a fifth occurrence would be a repeat of a defect this domain's own brain now documents explicitly.
- **(P-512/WO-083, shipped)** `Pbkdf2OneWayHasher.Verify` must never run `Rfc2898DeriveBytes.Pbkdf2` against an untrusted stored iteration count or subkey length without first checking both against a fixed ceiling (`MaxVerifiableIterations`) and an exact expected length (`SubkeySize`) — the check must happen BEFORE the expensive derive call, never after (checking after defeats the entire purpose of the ceiling). `MaxVerifiableIterations` must never be derived from `CryptographyOptions.Pbkdf2Iterations`'s currently-configured value — it is a fixed constant, set independently, so a future legitimate config increase never requires a simultaneous ceiling bump.
- **(P-512/WO-083, shipped)** `CryptographyOptions.Pbkdf2Iterations`'s floor (`MinimumPbkdf2Iterations`) applies ONLY at `Hash()` time (via `[Range]`/`ValidateOnStart`) — `Verify` must never reject a stored hash for having an iteration count below the floor. A hash legitimately created before this floor existed (or under an older, lower configuration) must continue to verify exactly as before; only the NEW ceiling and the exact-subkey-length check gate what `Verify` will honor from a stored value.
- **(P-513/WO-083, shipped)** `AesGcmEncryptionService` must reject any `CryptographicKey.Material` whose length is not EXACTLY 32 bytes, before constructing any `AesGcm` instance — a shorter OR longer key is a configuration defect, never silently accepted as AES-128/AES-192-GCM. This check belongs in `AesGcmEncryptionService` (the point of use, "the declared algorithm"), never in `CryptographicKey` itself, which stays algorithm-agnostic.
- **(P-514/WO-083, shipped, BREAKING)** `ITotpReplayGuard` implementations must mark a code used ATOMICALLY (`TryMarkUsedAsync`) — a check-then-act sequence (a prior `HasBeenUsedAsync` call followed by a separate `MarkUsedAsync` call) reintroduces the exact TOCTOU this phase exists to close and must never be reintroduced by a future implementer, even one composing this interface's members by hand. `TotpVerifier`'s replay window must always be derived from the SAME `digits`/`stepSeconds`/`driftWindow`/`algorithm` values actually passed to `ITotpGenerator.ValidateCode` for that call — never a hardcoded assumption independent of what was actually validated.
- **(P-514/WO-083, shipped)** `ITotpAttemptThrottle` must never be added as a required constructor dependency of `TotpVerifier` — it is a standalone, optionally-composed seam the CALLER invokes around `TotpVerifier.VerifyAsync`, mirroring `ITotpReplayGuard`'s own "ships uninvolved, consumer composes" shape. Adding it to `TotpVerifier`'s constructor would reintroduce exactly the kind of blast-radius expansion this phase's own corrected brief flagged as a real cost, not a hypothetical one.
- **(P-529, shipped, BEHAVIOUR CHANGE)** `Result.Error` throws a named `InvalidOperationException` for an uninitialized `default(Result)` rather than returning `null`. `Error`'s never-null contract is absolute: `Error.None` expresses "no error", `null` never does, and both `Failure` factories plus both implicit `Error` conversions reject a `null` argument. Do not "simplify" that null-coalescing guard away — the struct's all-zero value reports `IsFailure` while holding no error, and it is reachable without writing `default` at all (a failed `Dictionary.TryGetValue` out-parameter, an element of `new Result[n]`, an unassigned field).
- **(P-529, shipped, BEHAVIOUR CHANGE)** `ValidationResult`/`ValidationResult<T>` carry hand-written `Equals`/`GetHashCode` and snapshot the supplied error sequence into a private array. Neither may be replaced with the compiler-generated `record` versions: an `IReadOnlyList<Error>` member makes generated equality fall back to reference equality (failures compared unequal while successes compared equal by accident of sharing one static empty array), and `IReadOnlyList<T>` is a read-only VIEW rather than an immutable collection, so storing the caller's list let a later `Clear()` produce a failed result carrying zero errors — the exact state `Failure`'s own guard rejects.
- **(P-529, shipped)** `SmartEnum<TEnum,TValue>`'s `TValue` constraint must stay `IEquatable<TValue>` ALONE. Adding `IComparable<TValue>` compiles inside `SharedKernel.Primitives` and passes a survey of every concrete subclass in the repo (all use `int`), then breaks `SharedKernel.Core`'s `Guard.Against.InvalidSmartEnum<TEnum, TValue>` with `CS0314`, because that forwarder is itself generic over `TValue` and constrains only `IEquatable` — widening a public API in another package. Ordering therefore goes through `Comparer<TValue>.Default`. **The generalizable lesson: surveying concrete subclasses does not assess a constraint change; generic forwarders in other packages must be searched for too.**
- **(P-529, shipped)** Any `System.Text.Json` code in this domain must use the `JsonTypeInfo<T>` overloads, resolved via `JsonSerializerOptions.GetTypeInfo(Type)`. `JsonSerializer.Deserialize<T>(ref reader, options)`, `JsonSerializer.Serialize(writer, value, options)`, and `JsonSerializerOptions.GetConverter(Type)` are all annotated `[RequiresUnreferencedCode]`+`[RequiresDynamicCode]` and were each MEASURED emitting `IL2026`+`IL3050` from `SmartEnumJsonConverter`. A converter must never be a `JsonConverterFactory` either — a factory needs `MakeGenericType` at runtime, which this platform bars.
- **(P-529, shipped)** Every `[DebuggerDisplay]` display member in this domain reads BACKING FIELDS, never public properties. `Result.Value`/`.Error`, `Result<T>.Value`/`.Error`, and `ValidationResult<T>.Value` each throw depending on state, and a display expression that throws renders as an evaluation error in the watch window instead of the outcome — strictly worse than having no attribute.
- **(P-529, shipped)** `GenerateDocumentationFile=true` must be set in each packable `.csproj` individually. `Directory.Build.targets`' default is guarded on the property being empty and the .NET SDK assigns it `false` before that guard evaluates, so relying on the central default ships a package with an assembly and no `.xml`. Every package in this domain that has not set it explicitly is still shipping no documentation.
- **(P-529, shipped)** `WellKnownBaggageKeys.TenantId` is `"TenantId"`, deliberately NOT `"tenant.id"` for symmetry with `WellKnownTagKeys.TenantId`. `13.ServiceDefaults`' `BaggageLogRecordProcessor` copies `Activity` baggage generically rather than by known key, so a baggage key string IS the emitted log property name — renaming it silently renames a field that deployed dashboards, saved searches, and alert rules filter on. That is an operational breaking change owned by whoever runs the log pipeline, never a constants-registry tidy-up.
- **(P-514/WO-083, shipped)** A caller of `TotpVerifier.VerifyAsync` must pass `ct` BY NAME (`ct: ct`), never positionally as the 4th argument — the four new optional `digits`/`stepSeconds`/`driftWindow`/`algorithm` parameters sit between `code` and `ct`, so a positional 4th argument now binds to `digits` instead. This is not hypothetical: `12.Security.Totp`'s production `TotpChallengeService.cs` was found, during this phase's own implementation pass, to do exactly this and fails to compile as a result — a real consumer break the design's own "source-compatible" claim did not anticipate. `01.Core/README.md`'s samples were corrected to demonstrate the named-argument form for this reason.

---

## DI Registration (expected shape)

> **(P-518/WO-083, design-locked, implementation pending)** Every DI extension method's own service
> registrations across this domain (`AddSharedKernelCryptography`, `AddSharedKernelCompression`,
> `AddSharedKernelArgon2Cryptography`, `AddSharedKernelAzureKeyVaultCryptography`,
> `AddSharedKernelFeatureManagement`, `AddInMemoryLocalizationCatalog`/`AddStringLocalizerCatalog<TResource>`,
> `ClockExtensions.AddClock()`) converts from plain `AddSingleton`/`AddKeyedSingleton` to
> `TryAddSingleton`/`TryAddKeyedSingleton` — fixing a confirmed bug where calling `AddSharedKernelCryptography()`
> twice today double-registers all eleven of its services, and inverting override semantics so a consumer
> registration made BEFORE the platform's `AddX()` call wins (never after). The one exception:
> `SharedKernel.Validation`'s `AddNationalIdValidator<TValidator>()` converts to
> `TryAddEnumerable(ServiceDescriptor.Singleton<INationalIdValidator, TValidator>())` instead of a plain
> `TryAddSingleton`, since `INationalIdValidator` is a genuine, intentional multi-implementation collection —
> a plain `TryAddSingleton` there would silently drop every country validator registered after the first.

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

// Preferred shape (P-530): the options type declares its own section, so the call site names none.
// public sealed class MyServiceOptions : ISectionBoundOptions
// {
//     public static string SectionName => "SharedKernel:MyService";   // static property, not const
// }
services.AddValidatedOptions<MyServiceOptions>(configuration);

// With a zero-reflection [OptionsValidator]-generated validator (validation only — binding is still
// reflective; see AOT Compatibility). Leave validateDataAnnotations at its false default here.
services.AddValidatedOptions<MyServiceOptions, MyServiceOptionsValidator>(configuration);

// With a hand-written cross-property validator, keeping the per-property attributes enforced too.
services.AddValidatedOptions<PoolOptions, PoolOptionsValidator>(
    configuration.GetSection(PoolOptions.SectionName),
    validateDataAnnotations: true);

// Named instances — two configurations of one options type. Consume via IOptionsMonitor<T>.Get(name)
// or IOptionsSnapshot<T>.Get(name); plain IOptions<T> only ever sees the default instance.
services.AddValidatedOptions<ClientOptions>(configuration.GetSection("Clients:Primary"), name: "primary");
services.AddValidatedOptions<ClientOptions>(configuration.GetSection("Clients:Secondary"), name: "secondary");

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

// Cryptography — associated data (AAD) on every ISymmetricEncryptionService call (P-491/WO-081,
// design-locked, BREAKING). No new registration — every existing call site must add an explicit
// associatedData argument. Derive it deterministically from context available at both encrypt and decrypt
// time; Array.Empty<byte>() is the explicit "no context binding" choice, never an implicit default.
var aad = Encoding.UTF8.GetBytes($"cache-key:{cacheKey}"); // or a row id, message type, subscription id, etc.
var payload = symmetricEncryptionService.Encrypt(plaintext, aad);
var roundTrip = symmetricEncryptionService.Decrypt(payload, aad); // wrong aad => Result.Failure(Error.Unexpected)

// Cryptography — marking a custom IEncryptionKeyProvider as genuinely synchronous (P-492/WO-081,
// SHIPPED). Only implement ISynchronousEncryptionKeyProvider when GetCurrentKeyAsync/GetKeyAsync
// truly never perform a blocking network/IPC round trip — a KMS-backed provider must NEVER implement this.
// An unmarked provider makes the sync Encrypt/Decrypt/EncryptToString/DecryptToString members throw
// NotSupportedException instead of silently blocking a thread; use the *Async overloads against it instead.
public sealed class MySynchronousConfigKeyProvider : ISynchronousEncryptionKeyProvider
{
    // GetCurrentKeyAsync/GetKeyAsync return an already-completed ValueTask — genuinely non-blocking.
    // No separate ": IEncryptionKeyProvider" needed — ISynchronousEncryptionKeyProvider already extends it.
}

// Cryptography — Argon2id, a keyed OWASP-preferred alternative to the unkeyed PBKDF2 default
// (P-495/WO-081, SHIPPED, thirteenth package). Never replaces the unkeyed IOneWayHasher default —
// resolve it explicitly via the "Argon2id" key. Choose Argon2id unless a FIPS-mode requirement mandates PBKDF2.
services.AddSharedKernelArgon2Cryptography(configuration);
var argon2 = provider.GetRequiredKeyedService<IOneWayHasher>(
    Argon2CryptographyServiceCollectionExtensions.Argon2idOneWayHasherKey);

// Cryptography — asymmetric signing goes async (P-493/WO-081, shipped, BREAKING); a synchronous/
// config/certificate-backed IAsymmetricKeyProvider implementer migrates mechanically by returning an
// already-completed ValueTask, matching P-446's IEncryptionKeyProvider precedent exactly, and should
// additionally implement ISynchronousAsymmetricKeyProvider to keep the sync Sign/Verify members usable.
// A genuinely network-bound provider (e.g. a future remote-signing Key Vault provider) must never mark
// itself — MyAsyncKeyVaultBackedAsymmetricKeyProvider below deliberately does not.
services.AddSingleton<IAsymmetricKeyProvider, MyAsyncKeyVaultBackedAsymmetricKeyProvider>();
var signature = await signatureService.SignAsync(data, keyId, ct); // never disposes the resolved RSA/ECDsa

// Cryptography.KeyVault.Azure — remote signing (P-494/WO-081, SHIPPED) additionally registers
// AzureKeyVaultAsymmetricKeyProvider as IAsymmetricKeyProvider (a DISTINCT singleton from
// AzureKeyVaultEncryptionKeyProvider) — any AzureKeyVaultCryptographyOptions.KeyNames entry works as a
// signing keyId; rotation hardening (P-496/WO-081, SHIPPED) adds a callable
// MintNewVersionAsync (resolved against the concrete AzureKeyVaultEncryptionKeyProvider type, not
// through IEncryptionKeyProvider/IEnvelopeEncryptionProvider) — never invoked automatically by this
// package. Call it once during initial provisioning before the first GetCurrentKeyAsync — that
// method throws InvalidOperationException rather than silently auto-minting a version.
services.AddSharedKernelAzureKeyVaultCryptography(configuration);
AzureKeyVaultEncryptionKeyProvider azureKeyVaultEncryptionKeyProvider =
    provider.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>();
var newVersionTag = await azureKeyVaultEncryptionKeyProvider.MintNewVersionAsync(ct); // e.g. "v3"

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

// Cryptography.KeyVault.Azure (P-447, shipped; probe added P-487/WO-080) — implements
// IEncryptionKeyProvider, IEnvelopeEncryptionProvider, and IEncryptionKeyProviderProbe (same singleton,
// three service-type registrations); wrap in CachedEncryptionKeyProvider (above) if caching is desired —
// this package ships none of its own.
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

`SharedKernel.Core` (including the Guard Clause System merged in from the former `SharedKernel.Guards` package, P-505/WO-082) ships **no DI extensions** — it is a pure library with no `Microsoft.Extensions.DependencyInjection.Abstractions` reference at all. `SharedKernel.Primitives` is not fully dependency-free of that package, however: it already carries one package-owned extension, `ClockExtensions.AddClock()` (registering `SystemClock` as `IClock`), which is why it references `Microsoft.Extensions.DependencyInjection.Abstractions` in the first place. `IIdGenerator` and `SystemClock`'s `TimeProvider` overload deliberately do **not** get an equivalent `AddX()` extension — both are registered with a plain `services.AddSingleton<...>()` call at the consumer's own composition root. This is a per-abstraction design choice (each new abstraction in this package is evaluated on its own merits for whether a convenience extension pulls its weight), not evidence that the package avoids the DI abstractions package altogether.

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
- **(P-491/WO-081, SHIPPED)** The `associatedData` parameter is a plain `byte[]` passed straight through to `AesGcm.Encrypt`/`.Decrypt` — no reflection, no new AOT surface whatsoever; this phase changes a method signature only.
- **(P-492/WO-081, shipped)** `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous` is a plain `switch`-expression `is`-pattern-match chain (interface check, then a type check against `CachedEncryptionKeyProvider` with a recursive call on `.Inner`) — static IL `isinst`/`castclass` instructions only, no reflection, fully AOT-safe.
- **(P-493/WO-081, shipped)** `AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous` is the same AOT-safe `is`-pattern-match shape as `EncryptionKeyProviderCapabilities`. `SignAsync`/`VerifyAsync` are ordinary `ValueTask`-returning async methods — no reflection.
- **(P-494/WO-081, design-locked, implementation pending)** `KeyVaultRsaKey`/`KeyVaultEcdsaKey`'s AOT status is bounded by the Azure SDK's (`Azure.Security.KeyVault.Keys`) own AOT compatibility, mirroring the existing `AzureKeyVaultEncryptionKeyProvider` precedent — confined entirely to `SharedKernel.Cryptography.KeyVault.Azure`, never propagating into `SharedKernel.Cryptography` itself. Subclassing the abstract `RSA`/`ECDsa` BCL types is ordinary virtual-method override dispatch — no reflection. **Correction discovered during P-493's own test-double implementation, applies here too:** `RSA.SignData`/`VerifyData` and `ECDsa.SignData`/`VerifyData` are non-virtual convenience methods, NOT overridable — the BCL compiler rejects an `override` on them (CS0506). The real extension points a subclass must override are `SignHash`/`VerifyHash` (plus `ExportParameters`/`ImportParameters`, and `GenerateKey` for `ECDsa`) — `SignData`/`VerifyData` hash the input internally and then delegate to those. `KeyVaultRsaKey`/`KeyVaultEcdsaKey` must override `SignHash`/`VerifyHash`, not `SignData`/`VerifyData`.
- **(P-495/WO-081, SHIPPED)** `Argon2idOneWayHasher`'s AOT status is bounded by `Konscious.Security.Cryptography.Argon2`'s own AOT compatibility — must be verified on each version upgrade, mirroring the `FluentValidation`/Azure SDK precedent; confined entirely to `SharedKernel.Cryptography.Argon2`, never propagating into `SharedKernel.Cryptography` core. The PHC-string parse/format logic itself is plain string/byte manipulation, no reflection.
- **(P-496/WO-081, design-locked, implementation pending)** The new `Azure.Security.KeyVault.Secrets` client's AOT status is bounded the same way as the existing `Azure.Security.KeyVault.Keys` dependency — verify on each upgrade, confined to `SharedKernel.Cryptography.KeyVault.Azure`. The version-tag registry (`ConcurrentDictionary`-backed) and `GetKeyAsync`'s dual-shape (new-tag-then-legacy-envelope) parsing are plain string/byte-array logic, no reflection.
- **(P-530, shipped, user-directed — the one package in this domain that is deliberately NOT AOT-clean)** `SharedKernel.Configuration`'s `AddValidatedOptions` overloads are **not** trim- or AOT-safe, and now say so in the type system rather than in prose: each carries `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`, mirroring the BCL's own annotations on `OptionsBuilder<TOptions>.Bind`. This is not a gap to be closed later — a generic library wrapper structurally cannot get generated binding, because .NET's configuration-binding source generator intercepts `Bind` calls in the **calling** assembly and so can never specialize a `Bind<TOptions>` that lives inside a library and is generic over an options type it has not seen. Each `TOptions` additionally carries `[DynamicallyAccessedMembers(PublicProperties | NonPublicProperties | PublicParameterlessConstructor)]` and each `TValidator` `[DynamicallyAccessedMembers(PublicConstructors)]`, which is what a trimmer needs to keep a **flat** options class working; the residual, genuinely-unfixable risk is an options class whose own properties are complex types, whose nested members the trimmer cannot see. Measured: 12 IL warnings (6× `IL2091`, 4× `IL2026`, 2× `IL3050`) before this phase, **0 after** — not because anything was suppressed, but because the requirement is now declared and propagates to the caller. Before P-530 this package advertised `aot` in `PackageTags` and "AOT-clean" in its README while emitting all twelve into every trimming consumer's build; `aot` must not reappear in its tags. A consuming service that genuinely needs a trimmed or native-AOT publish should keep such options flat, or bind them by hand at its own composition root where the source generator can see the concrete type. Note the split worth keeping straight: an `[OptionsValidator]`-generated `TValidator` really does make **validation** reflection-free — it is **binding** that never was.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Primitives.Tests/` — Result, Error, IClock, SmartEnum, IHasSuccessFlag, IResultOfT\<T\>, IFailureFactory\<TSelf\> (generic-constraint dispatch producing a correct failure `Result<T>` for at least two distinct closed shapes, e.g. `Result<int>`/`Result<string>`, plus confirmation that `Result` (non-generic) is not assignable to `IFailureFactory<Result>`), `LoggingEventIdRanges` (all 18 domain base constants are pairwise unique, each is a multiple of 1000, and each equals exactly `{domain-folder-number} * 1000` matching the root CLAUDE.md folder map 00 through 17), `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys` (every constant's literal value is pinned exactly — `CorrelationId` header = `"X-Correlation-Id"`, `TenantId` header = `"X-Tenant-Id"`, `CorrelationId` baggage key = `"correlation.id"`, and every `WellKnownTagKeys` field — so a future edit cannot silently drift a cross-service propagation identifier), `IIdGenerator`/`UuidV7IdGenerator` (uniqueness across a large generation batch; values whose embedded millisecond timestamps differ compare as non-decreasing under the default `Guid` comparer — values sharing the same millisecond carry no such guarantee against each other and must not be asserted as strictly ordered), `SystemClock`'s `TimeProvider`-backed internals (parameterless `SystemClock()` reflects real time; `SystemClock(fakeTimeProvider)` reflects the injected provider's current instant, including after the fake advances time; `Today` derives correctly from the same instant)
- `SharedKernel.Core.Tests/` — exceptions, railway extensions, BCL extensions, `ResultTry`/`ResultTry.TryAsync` (delegate success path, thrown-exception-to-`Error.Unexpected` translation including a nested/flattened `AggregateException` case, custom exception-mapper overload, never-rethrows guarantee), `ResultCombine` (all-success non-generic and generic variants, single-failure and all-failure variants verifying every collected `Error` surfaces — not just the first — for both the non-generic `Result` and generic `Result<T>` overloads); `Guards/` subfolder (merged from the former `SharedKernel.Guards.Tests`, P-505/WO-082, namespace `SharedKernel.Core.Tests.Guards`) — guard functional path (Against.*), guard throw path (Throw.*), boundary theories, and the bounded-regex-cache eviction proof (P-522/WO-083)
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
- **(P-451/WO-069, shipped; `TotpVerifier` coverage superseded by P-514/WO-083, see below)** `SharedKernel.Cryptography.Tests/Totp/` covers: `Base32` round-trip against RFC 4648 §10's published vectors; `HotpGenerator` against RFC 4226 Appendix D's published test vectors (all 10 counters); `TotpGenerator` against RFC 6238 Appendix B's published test vectors (SHA-1/256/512 at all 6 documented timestamps, fetched verbatim from the RFC text rather than transcribed from memory — every vector matched the implementation exactly on the first run); clock-drift-window accept/reject via a `FakeTimeProvider`-backed `IClock` (never real wall-clock sleeping); `TotpProvisioningUri.Build` output matching the Key Uri Format field-for-field; `RecoveryCodeGenerator` output count/length correctness and statistical non-repetition; a dedicated `ReadmeSampleCompileTests.cs` compiling the README's enrollment/challenge/verification/recovery-code samples verbatim (kept up to date as of P-514).
- **(P-514/WO-083, shipped)** `SharedKernel.Cryptography.Tests/Totp/TotpVerifierTests.cs` — the P-451-era assertion (NSubstitute interaction verification against the now-removed `HasBeenUsedAsync`) is retired and replaced: T-81 is a GENUINE concurrency test — `Barrier`-synchronized concurrent callers (a pairwise test, plus a 50-way variant) presenting the identical valid code against a real, atomic, `ConcurrentDictionary.TryAdd`-backed in-jurisdiction `ITotpReplayGuard` test double (`AtomicInMemoryTotpReplayGuard`, colocated in the test file) — proving exactly one call succeeds and every other call is rejected. Deliberately NOT a sequential test — a sequential test would have passed against the OLD, defective two-step implementation too, making the whole phase unverifiable; a real atomic double is used specifically because an NSubstitute mock configured with canned returns cannot demonstrate genuine atomicity. T-82 proves the replay window passed to `TryMarkUsedAsync` is derived from the ACTUAL `stepSeconds`/`driftWindow` passed to that call (`stepSeconds=60, driftWindow=2` → 300 seconds), not the removed hardcoded `DefaultStepSeconds`/`DefaultDriftWindow`-derived 90 seconds, plus a parity test confirming the value is unchanged at the defaults. 318/318 `SharedKernel.Cryptography.Tests` passing (up from 235/235 at P-451, 314/314 immediately prior to this phase).
- **(P-474/WO-076, shipped)** `SharedKernel.DataPrivacy.Tests/` (56/56 passing) — `PiiMasking.*` deterministic output for known inputs (email local-part masking incl. 1-char/empty/no-`@`/multi-`@` edge cases, phone digit-count-dependent reveal windows with separator preservation, PAN fixed-last-4 incl. 19-digit and sub-4-digit inputs, `Suppress`'s fixed sentinel), null/empty-input never throws; a compiled-assembly `System.Reflection.Metadata`/`PEReader` scan of the production DLL's `TypeReference` table proving no reflection-invocation type is referenced (not a source grep — see the public-surface block above), plus a companion test proving the attribute-exclusion branch is actually exercised; attribute-application mechanics for `DataClassificationAttribute`/`SensitiveDataCategoryAttribute` (a test-only reflective read proving mechanics, never a production-code claim); confirmation `IDataSubjectRequestHandler` has no default implementation registered anywhere in this package. `SharedKernel.Consumer.Tests` gained 6 tests including a `.nuspec` dependency-count assertion proving zero third-party NuGet dependency (67/67 passing, up from 61/61).
- **(P-482/WO-078, shipped)** `SharedKernel.Localization.Tests/` (43/43 passing) — `InMemoryLocalizationCatalog` (registered pair resolves correctly, unregistered pair returns `false`/`null` and never throws/blanks, `AddTranslation` chaining, parent-culture-chain fallback down to `CultureInfo.InvariantCulture`, case-sensitive `code` lookup); `StringLocalizerLocalizationCatalog` (wraps an NSubstitute-doubled `IStringLocalizerFactory`, correctly resolves a found key, correctly signals `false` for `ResourceNotFound` despite `LocalizedString.Value` carrying the raw key, and proves the ambient `CultureInfo.CurrentUICulture` swap is set during the call and restored afterward — including on a thrown exception); DI registration sanity for both extensions, including a reflection-based scan over every public static method this assembly declares confirming none is named `AddSharedKernelLocalization`, with a companion non-vacuous-check test; a `ReadmeSampleCompileTests.cs` compiling every README code sample verbatim. `SharedKernel.Consumer.Tests` gained 5 tests including a `.nuspec` dependency-count assertion proving exactly two dependencies (72/72 passing, up from 67/67).
- **(SK.01.LoggingRangesNewDomains, shipped)** `LoggingEventIdRangesTests` extended so the pairwise-uniqueness/multiple-of-1000/folder-number-to-value theory cases cover all 21 domain base constants (00 through 20), with a dedicated `PreExistingEighteenDomainConstants_AreByteForByteUnchanged` fact hardcoding all 18 prior expected values independently of the shared theory table — a transposition between two existing constants would still pass the pairwise-uniqueness/modulo checks alone (both remain unique multiples of 1000), so only this independent hardcoding catches it. 146/146 `SharedKernel.Primitives.Tests` passing.
- **(P-491/WO-081, SHIPPED)** `SharedKernel.Cryptography.Tests/` gains: round-trip with matching `associatedData` succeeds; round-trip with mismatched `associatedData` at decrypt fails with `Result.Failure(Error.Unexpected)`, proven by a test that swaps AAD between two otherwise-identical payloads (the phase's headline acceptance criterion); `Array.Empty<byte>()` is a valid, always-succeeding no-context-binding AAD choice; every pre-existing sync/async `Encrypt`/`Decrypt` test updated to pass explicit AAD and re-verified green.
- **(P-492/WO-081, shipped)** `SharedKernel.Cryptography.Tests/` gained: a provider marked `ISynchronousEncryptionKeyProvider` — sync `Encrypt`/`Decrypt` behave exactly as before; an unmarked provider — sync `Encrypt`/`Decrypt`/`EncryptToString`/`DecryptToString` throw `NotSupportedException` without attempting the bridge (proven via zero inner-provider call count, not just exception-type match); `CachedEncryptionKeyProvider` wrapping a marked inner reports `IsGenuinelySynchronous = true`, wrapping an unmarked inner reports `false`; a nested `CachedEncryptionKeyProvider`-wrapping-`CachedEncryptionKeyProvider` resolves recursively to the true leaf provider's marking (both a marked and an unmarked leaf); regression of P-446's thread-pool-starvation guard re-verified green against the now-explicitly-marked `InMemoryEncryptionKeyProvider`. When asserting a sealed type does NOT implement a marker interface, assign through the base interface type first — asserting directly on the concrete sealed type trips CS0184 (always-false, statically provable) under this repo's 0-warning build gate.
- **(P-493/WO-081, shipped)** `SharedKernel.Cryptography.Tests/Signing/` (294/294 `SharedKernel.Cryptography.Tests` passing, up from 267) gained: `SignAsync`/`VerifyAsync` byte-identical to `Sign`/`Verify` for the same input under a synchronously-marked provider; sync `Sign`/`Verify` throw `NotSupportedException` against a new `NonSynchronousAsymmetricKeyProvider` unmarked-wrapper double; wrong-key/tampered-data failure cases for both RSA and ECDSA via the async members; `InMemoryAsymmetricKeyProvider` migrated to the async contract and now implements `ISynchronousAsymmetricKeyProvider`. The disposal-ownership proof (T-70, the phase's highest-value test) uses `DisposeGuardedRsa`/`DisposeGuardedEcdsa` — direct `RSA`/`ECDsa` subclasses whose `Dispose(bool)` throws `InvalidOperationException` — wrapped by `DisposeThrowingAsymmetricKeyProvider`, which returns the SAME cached guarded instance across repeated calls (a fresh-clone-per-call double, the shape used elsewhere in this test project for other reasons, cannot catch this regression). **Its genuineness was verified empirically, not assumed**: the old `using`-disposal bug was temporarily reintroduced into `RsaSignatureService.Sign`, the test was confirmed to fail with exactly the expected `InvalidOperationException`, then the fix was reverted and the full suite re-confirmed green — mirrors P-492's "prove the assertion isn't vacuous" discipline (see the CS0184 note above) applied to a runtime regression instead of a compile-time one. `Verify` rejecting a below-minimum-size key exactly like `Sign` does, for both algorithms, via `RsaWithKeySize`/`EcdsaWithKeySize` (wrap a real, otherwise-valid key but override `KeySize` to report an artificially small value) plus `DelegateAsymmetricKeyProvider` — this avoids ever needing to construct a genuinely undersized/non-standard-curve key just to exercise the gate.
- **(P-494/WO-081, design-locked, implementation pending)** `SharedKernel.Cryptography.KeyVault.Azure.Tests/` gains (mirroring T-54's/T-65's existing skip-if-unavailable-gated real-vault posture): `GetRsaKeyAsync`/`GetEcdsaKeyAsync` round-trip via `RsaSignatureService.SignAsync`/`VerifyAsync` and `EcdsaSignatureService.SignAsync`/`VerifyAsync` against a real (or env-gated dev-tenant) Key Vault key, without ever exporting private key material; `ExportParameters`/`ImportParameters` throw `NotSupportedException`; a structural/compile-time proof `RsaSignatureService`/`EcdsaSignatureService` require zero code changes beyond P-493 to consume this provider.
- **(P-495/WO-081, SHIPPED)** `SharedKernel.Cryptography.Argon2.Tests/` — `Hash`→`Verify` round trip (`Success`); a tampered/incorrect secret (`Failed`); a hash produced under prior options resolves `SuccessRehashNeeded` after `Argon2CryptographyOptions` changes; a malformed/foreign-algorithm PHC string, a structurally-valid-but-uncomputable one, returns `Failed` rather than throwing; DI registration sanity confirming `Argon2idOneWayHasher` resolves only via the `"Argon2id"` keyed lookup, and the unkeyed `IOneWayHasher` still resolves to `Pbkdf2OneWayHasher` when `AddSharedKernelCryptography` is also registered. 45/45 passing.
- **(P-496/WO-081, SHIPPED)** `SharedKernel.Cryptography.KeyVault.Azure.Tests/` gained a new `AzureKeyVaultEncryptionKeyProviderVersionRegistryTests.cs` (T-73, 9 tests) built on a new `TestSupport/AzureKeyVaultCallCountingFakes.cs` — call-counting test doubles that SUBCLASS the real, non-sealed, protected-parameterless-ctor `KeyClient`/`SecretClient`/`CryptographyClient` types directly (never a bespoke abstraction), constructed via a new `internal`-only test-seam constructor overload on `AzureKeyVaultEncryptionKeyProvider` (exposed to the test assembly via a project-scoped `InternalsVisibleTo`, deliberately narrow — every extra parameter is unregistered in DI with no default value, so MS.DI's constructor-selection can never pick it for the real registration). Azure SDK model types (`KeyVaultKey`, `WrapResult`, `UnwrapResult`) expose only an `internal` constructor and/or `internal`-setter properties from outside the SDK's own assembly — constructing them required reflection over those exact members, isolated entirely inside the fakes file (test-only, never production code) — a legitimate, narrowly-scoped technique worth reusing for any future Azure SDK model type that needs faking. Covers exactly T-73's four acceptance criteria (a superseded version still decrypts after a mint; a second `GetKeyAsync` for an already-resolved tag costs zero further Key Vault calls; two independently-constructed provider instances sharing one simulated vault converge `GetCurrentKeyAsync` on the identical tag; a legacy pre-P-496 envelope-shaped `keyId` still resolves via the fallback path) plus three bonus assertions (`GetCurrentKeyAsync` throws before any mint; an unknown-but-tag-shaped `keyId` returns `null` via 404-translation; repeated mints reuse one cached Azure-key resolution, proving C-92 directly via a call count). `AzureKeyVaultEncryptionKeyProviderIntegrationTests.cs` (T-74) gained `MintNewVersionAsync_UnreachableVault_ThrowsInsteadOfSilentNoOp` (always exercised, mirrors every other member's fail-closed proof) and `MintNewVersionAsync_ThenEncryptUnderNewCurrent_ThenDecryptUnderOldTag_RoundTripsAgainstRealAzureKeyVault` (env-gated behind the SAME `SHAREDKERNEL_TEST_AZURE_KEYVAULT_URI`/`_KEY_NAME` variables T-54/T-65/T-71 already use — no new harness invented — including a length assertion confirming a newly-minted `CryptographicKey.Id` is short). All 30 tests in this project, including every pre-existing one, verified GREEN via a temporary out-of-tree HintPath-based compile+run harness (see this phase's WO-081 changelog entry for why `dotnet test` itself could not run in this session).

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
- [2026-09-04] P-487 implemented and closed (WO-080) — `IEncryptionKeyProviderProbe`/`EncryptionKeyProviderHealth` shipped additively in `SharedKernel.Cryptography` (mirrors `07.Messaging`'s `IMessageBusProbe`/`MessageBusHealth` shape — plain `Task<THealth>`, chosen over `17.Workflows`'s `Task<Result<T>>` and `19.Scheduling`'s zero-I/O bare `Task<T>` — per the dispatching spec's explicit instruction). `AzureKeyVaultEncryptionKeyProvider` now additionally implements it: `ProbeAsync` performs one read-only Key Vault key-metadata call, never a wrap/unwrap/sign/verify, and is the one deliberate, documented carve-out from that class's fail-closed-via-exception contract — it catches every non-cancellation exception and reports `IsHealthy = false` instead of propagating. `AddSharedKernelAzureKeyVaultCryptography` now registers the probe as a third service type from the same singleton. Config-based `EncryptionOptionsKeyProvider`/`NullEncryptionKeyProvider` (`06.Persistence.EfCore`) confirmed unaffected — never required to implement this opt-in contract. Closes the blocker root Phase Backlog P-449 was waiting on. Packages/Interface Contracts sections updated. 239/239 `SharedKernel.Cryptography.Tests` (up from 235), 36/36 `SharedKernel.Cryptography.KeyVault.Azure.Tests` (up from 33). Both packages repacked to the local feed. `SharedKernel.Consumer.Tests` skipped — pre-existing, unrelated NU1101 restore failure (missing `SharedKernel.DataPrivacy`/`SharedKernel.Localization` nupkgs). `SK.01.P487` fully `●` (core-phase-implementer)
- [2026-09-08] Six phases processed in a single pass for WO-081 (P-491→P-496) — a coordinated, `01.Core`-first breaking wave whose contracts seven other domains' own planners (`02.Caching`/`06.Persistence`/`07.Messaging`/`15.Integration`/`17.Workflows`/`16.Testing`/`13.ServiceDefaults`, plus `00.Governance`) dispatch against once this brain is read. All six design-locked (`D-*` tasks `●`); implementation (`S-*`/`C-*`/`T-*`/`DO-*`/`P-*`) left `○`, mirroring the WO-067/068/069/076/078 precedent. **P-491** (breaking) — required `associatedData` (AAD) on every `ISymmetricEncryptionService` member, passed through to `AesGcm`'s own `associatedData` parameter; `EncryptedPayload` gains no new field, AAD is never persisted; a mismatch fails authentication as `Result.Failure(Error.Unexpected)`, the same shape as any other tamper case. A full six-domain call-site inventory (with `06.Persistence`'s `EncryptedValueConverter<T>` flagged as the hardest — no direct row-PK access inside a vanilla EF Core `ValueConverter`) is recorded in `01.Core/state-map.md`'s `SK.01.P491` notes, satisfying that phase's own named acceptance criterion. **P-492** — `ISynchronousEncryptionKeyProvider` capability marker + `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous`, replacing the retained sync `Encrypt`/`Decrypt`/`EncryptToString`/`DecryptToString` members' silent `.GetAwaiter().GetResult()` thread-blocking hazard with a structural `NotSupportedException`; `CachedEncryptionKeyProvider` gains a public `.Inner` property so the check recursively unwraps to the true leaf provider — the decorator itself deliberately never implements the marker directly, so its own best-case (cache-hit) behavior can never be mistaken for a guarantee. **P-493** (breaking, depends on P-492) — `IAsymmetricKeyProvider.GetRsaKey`/`GetEcdsaKey` become `GetRsaKeyAsync`/`GetEcdsaKeyAsync` (sync members removed outright, not retained); `IAsymmetricSignatureService` gains `SignAsync`/`VerifyAsync`, its retained sync `Sign`/`Verify` gated by an analogous new `ISynchronousAsymmetricKeyProvider`/`AsymmetricKeyProviderCapabilities` pair; `RsaSignatureService`/`EcdsaSignatureService` stop disposing the provider-returned key (a latent `ObjectDisposedException` hazard invisible until P-494 makes it reachable) and `Verify` gains the same `EnsureMinimumKeySize` check `Sign` already had. Recorded explicitly rather than glossed over: `RSA`/`ECDsa` expose no async `SignData`/`VerifyData` anywhere in the BCL, so `SignAsync`/`VerifyAsync` make only **key resolution** non-blocking — the cryptographic call itself against a remote-KMS-backed key remains genuinely synchronous, a limitation P-494 carries forward at the exact point it becomes observable. **P-494** (depends on P-493) — a new `AzureKeyVaultAsymmetricKeyProvider` (deliberately separate from `AzureKeyVaultEncryptionKeyProvider`) backing `GetRsaKeyAsync`/`GetEcdsaKeyAsync` via `CryptographyClient.SignData`/`VerifyData` through thin `KeyVaultRsaKey : RSA`/`KeyVaultEcdsaKey : ECDsa` subclasses (`ExportParameters`/`ImportParameters` throw `NotSupportedException`); caches one `CryptographyClient` per Azure key name from its first implementation specifically so P-496 never has to fix the same connection-pooling defect twice. **P-495** (independent of every other WO-081 phase) — a new, **thirteenth `01.Core` package**, `SharedKernel.Cryptography.Argon2` (`Konscious.Security.Cryptography.Argon2`, pure-managed): `Argon2idOneWayHasher` emitting the real, interoperable PHC string format rather than a bespoke encoding, registered as a `"Argon2id"`-keyed-only singleton — `Pbkdf2OneWayHasher` stays the sole unkeyed default and the FIPS-mode choice, completely untouched; `Argon2CryptographyOptions` defaults (19 MiB memory, 2 iterations, parallelism 1) cite OWASP's current Argon2id minimum explicitly. **P-496** (depends on P-494) — three `AzureKeyVaultEncryptionKeyProvider` redesigns: the same per-key-name `CryptographyClient` reuse P-494 established; `CryptographicKey.Id` becomes a short durable version tag backed by a new `Azure.Security.KeyVault.Secrets`-persisted registry plus a shared `"current version"` pointer secret every replica reads — correcting a real, previously-undocumented P-447 gap where every process/pod silently minted its own unique "current" key with no cross-replica sharing; `GetKeyAsync` stays backward-read-compatible via a dual-shape fallback (new tag first, legacy self-decodable envelope second), so this ships as an **additive MINOR repack, not a breaking one** — a deliberate, explicitly-recorded choice rejecting a forced-data-migration alternative; a new callable-only `MintNewVersionAsync` gives the provider its first real rotation story, every prior version staying resolvable indefinitely. Packages/Technology Stack/Interface Contracts/Implementation Rules/DI Registration/AOT/Test Rules sections all updated across every phase above; package count now 12 published/1 design-locked (`SharedKernel.Cryptography.Argon2`, the thirteenth). 58 tasks added across six new phase keys (`SK.01.P491`→`SK.01.P496`; D-65→D-83, S-25, C-81→C-95, T-66→T-74, DO-34→DO-39, P-35→P-42); `01.Core/state-map.md` total tasks now 365 (core-arch-planner, WO-081)
- [2026-09-08] P-491 implemented and closed (WO-081, C-81/T-66/DO-34/P-35) — the breaking AAD signature change shipped exactly per the D-65/D-66 design already recorded above: all eight `ISymmetricEncryptionService` members gained a required `byte[] associatedData` parameter with no default value on any overload; `AesGcmEncryptionService.EncryptCore`/`DecryptCore` pass it straight through to `AesGcm.Encrypt`/`.Decrypt`'s own `associatedData` parameter; `EncryptedPayload` gained no new field and the packed `EncryptToString`/`DecryptToString` string format never embeds it. Every pre-existing `SharedKernel.Cryptography.Tests` case updated to pass explicit AAD; new tests prove the headline acceptance criterion (matching AAD round-trips; mismatched or omitted AAD at decrypt fails as `Result.Failure(Error.Unexpected)`/`CryptographyErrorCodes.DecryptionFailed`, the same shape as any other tamper/wrong-key case; `Array.Empty<byte>()` is a valid always-succeeding no-context-binding choice; a null `associatedData` throws `ArgumentNullException`). 249/249 `SharedKernel.Cryptography.Tests` passing (up from 236). `01.Core/README.md`'s "Symmetric Encryption" section gained a full before/after migration table plus an AAD-derivation usage example; the package's `.csproj` `<Description>` gained this breaking change's release notes and had its stale "Next breaking release (P-446/WO-068)" wording corrected to "Shipped". `SharedKernel.Consumer.Tests`' three symmetric-encryption call sites updated to the new signature; `dotnet pack` succeeded cleanly (no `<Version>` added — a real MAJOR bump is devops-lead's git-tag decision, per the established P-446/P-447/P-451/P-487 precedent). Full consumer dependency-graph re-verification was attempted but blocked by the same pre-existing, unrelated NU1101 gap the 2026-09-04 P-487 entry above already documents (`SharedKernel.DataPrivacy`/`SharedKernel.Localization` were never packed to this sandbox's local feed) — confirmed unrelated before deciding not to chase it. Interface Contracts section above updated from "design-locked, implementation pending" to "shipped" for every AAD-related line, leaving P-492's still-pending `NotSupportedException` sync-gating description clearly marked as future/conditional rather than already-true. `SK.01.P491` fully `●` (7/7) — P-492→P-496 remain design-locked, implementation pending, unaffected by this pass (core-phase-implementer)
- [2026-09-08] P-492 implemented and closed (WO-081, C-82/C-83/T-67/T-68/DO-35/P-36) — the synchronous-provider capability gate shipped exactly per the D-68/D-69/D-70 design already recorded above: `ISynchronousEncryptionKeyProvider` (zero-member marker) and `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous` (marker → `true`; `CachedEncryptionKeyProvider` → recurse into new public `.Inner`; else `false` — a static provider-identity check, never a per-call cache-warmth test) both shipped; `AesGcmEncryptionService` computes the check once at construction (cached `bool` field) and gates all four retained sync members independently (each names its own correct `*Async` counterpart in its `NotSupportedException`, rather than relying on internal delegation to throw with a less-accurate name). `InMemoryEncryptionKeyProvider` (the one in-jurisdiction test double needing it) now implements the marker; `ControllableEncryptionKeyProvider` deliberately left unmarked (tests `CachedEncryptionKeyProvider`'s async caching, can `Hold()`). 18 new tests (`EncryptionKeyProviderCapabilitiesTests.cs` + additions to `AesGcmEncryptionServiceTests.cs`); 267/267 `SharedKernel.Cryptography.Tests` passing (up from 249), 0 warnings. `01.Core/README.md` gained a full "Gating the synchronous members" subsection. `.csproj` `<Description>` gained this phase's release notes, explicitly distinguishing a breaking **behavior** change (no signature changed — an existing custom provider that relied on the old silent-blocking bridge now throws `NotSupportedException`) from a compile-time API break; `dotnet pack` succeeded cleanly. Consumer dependency-graph re-verification blocked by the same pre-existing, unrelated NU1101 gap as P-491 — `SharedKernel.Consumer.Tests`' source was still fixed for correctness (`ConsumerEncryptionKeyProvider` now marked, new `ConsumerUnmarkedEncryptionKeyProvider` + gate-resolution test added) since restore-blocked is not build-blocked. Interface Contracts/Packages/Implementation Rules/AOT/Test Rules/DI Registration sections above updated from "design-locked, implementation pending" to "shipped". `SK.01.P492` fully `●` (9/9) — P-493→P-496 remain design-locked, implementation pending, unaffected by this pass (core-phase-implementer)
- [2026-09-08] P-493 implemented and closed (WO-081, C-84→C-87/T-69/T-70/DO-36/P-37) — the breaking async `IAsymmetricKeyProvider`/`IAsymmetricSignatureService` change shipped exactly per the D-71→D-74 design already recorded above: `GetRsaKey`/`GetEcdsaKey` removed outright, replaced by `GetRsaKeyAsync`/`GetEcdsaKeyAsync`; `ISynchronousAsymmetricKeyProvider`/`AsymmetricKeyProviderCapabilities` (direct marker check only, confirmed no decorator-unwrapping needed since no caching decorator exists for this contract); `IAsymmetricSignatureService.SignAsync`/`VerifyAsync` added, retained sync `Sign`/`Verify` gated via the same `NotSupportedException` pattern P-492 established. Fixed the key-ownership defect (the `using` around the provider-returned `RSA`/`ECDsa` instance removed — not caller-owned) and the minimum-key-size parity gap — **one correction to the design's own framing, made precise during implementation**: RSA's 2048-bit check was already `Sign`-only and is now also applied to `Verify`, but ECDSA had NO minimum-key-size check anywhere before this phase — it gained a wholly new 256-bit check on both `Sign` and `Verify`, not an extension of a pre-existing one. T-70 (the phase's highest-value test per the dispatching brief) required subclassing `RSA`/`ECDsa` directly rather than composing over the interface — `SignData`/`VerifyData` are non-virtual convenience methods on both BCL types, so a dispose-guard/key-size-override test double must override the real extension points (`SignHash`/`VerifyHash`, plus `ExportParameters`/`ImportParameters`/`GenerateKey`) instead; this same correction was propagated into P-494's own design notes above (`KeyVaultRsaKey`/`KeyVaultEcdsaKey`) so that phase's future implementer does not hit the identical CS0506 compile error. T-70's genuineness was verified empirically: the old `using`-disposal bug was temporarily reintroduced into `RsaSignatureService.Sign`, confirmed to fail the new test with the exact expected `InvalidOperationException`, then reverted. 294/294 `SharedKernel.Cryptography.Tests` passing (up from 267). `01.Core/README.md`'s "Asymmetric Signing" section rewritten with the async pattern, a full breaking-change migration table, and a new "Gating the synchronous members" subsection; `.csproj` `<Description>` gained this phase's release notes. `dotnet pack` succeeded cleanly (`1.0.0-alpha.0.854`, no `<Version>` added, same devops-lead git-tag precedent as P-491/P-492). Consumer dependency-graph re-verification blocked by the same pre-existing, unrelated NU1101 gap as P-491/P-492 — `SharedKernel.Consumer.Tests`' `ConsumerAsymmetricKeyProvider` was still fixed for correctness (now implements `ISynchronousAsymmetricKeyProvider` with the async member shapes) since restore-blocked is not build-blocked. Interface Contracts/Packages/Implementation Rules/AOT/Test Rules/DI Registration sections above updated from "design-locked, implementation pending" to "shipped". `SK.01.P493` fully `●` (12/12) — P-494→P-496 remain design-locked, implementation pending, unaffected by this pass (core-phase-implementer)
- [2026-09-08] P-494 implemented and closed (WO-081, C-88/C-89/T-71/DO-37/P-38) — the Azure Key Vault Keys remote-signing `IAsymmetricKeyProvider` shipped per D-75, but **D-76's own recorded design text was itself factually wrong** (it assumed `KeyVaultRsaKey`/`KeyVaultEcdsaKey` would override `SignData`/`VerifyData` — non-virtual convenience methods on `RSA`/`ECDsa`, CS0506 on `override`) and had to be corrected before writing any code, not merely followed; the correction — the real BCL extension points are `SignHash`/`VerifyHash` — was already recorded in this file's Interface Contracts/Implementation Rules sections from P-493's own discovery, found by searching before implementing, per this phase's explicit dispatch instruction. `AzureKeyVaultAsymmetricKeyProvider` (distinct singleton from `AzureKeyVaultEncryptionKeyProvider`, reuses `AzureKeyVaultCryptographyOptions`) resolves each Azure key name's `KeyType`/`KeySize` (derived via `JsonWebKey.ToRSA(false)`/`.ToECDsa(false)`, public material only) and one `CryptographyClient` exactly once, cached in a `ConcurrentDictionary<string, Lazy<Task<ResolvedAzureKey>>>` — baking in the connection-reuse fix P-496 otherwise has to apply retroactively. `KeyVaultRsaKey`/`KeyVaultEcdsaKey`'s `SignHash`/`VerifyHash` delegate to `CryptographyClient`'s real synchronous, HASH-taking `Sign`/`Verify` overloads (confirmed via direct reflection against the installed 4.7.0 assembly in a throwaway scratch probe — `SignData`/`VerifyData` exist too but take raw data and are the wrong overload). A second, unrelated BCL gotcha was hit and fixed: `AsymmetricAlgorithm.SignatureAlgorithm` (an inherited instance `string` property) shadows the Azure SDK's `SignatureAlgorithm` TYPE name for unqualified resolution inside an `RSA`/`ECDsa` subclass (`CS0120`/`CS1061`) — resolved via a `using AzureSignatureAlgorithm = ...` alias, now recorded as its own Implementation Rule for future subclassers. `ExportParameters`/`ImportParameters`/`GenerateKey` throw `NotSupportedException`; the provider never implements `ISynchronousAsymmetricKeyProvider`. 53/53 `SharedKernel.Cryptography.KeyVault.Azure.Tests` passing (12 locally-decidable unit tests plus env-gated real-vault integration tests using new, deliberately distinct `SHAREDKERNEL_TEST_AZURE_KEYVAULT_RSA_SIGNING_KEY_NAME`/`_ECDSA_SIGNING_KEY_NAME` variables — not exercised in this sandbox, honestly reported as skipped, mirroring T-54/T-65/P-487's precedent); full solution build 0 errors. `SharedKernel.Cryptography.KeyVault.Azure/README.md` gained a "Remote Signing" section; `01.Core/README.md` gained a matching subsection plus a package-table update. `.csproj` `<Description>`/`<PackageTags>` gained this phase's release notes; `dotnet pack` succeeded cleanly (`1.0.0-alpha.0.858`, no `<Version>` added, same devops-lead git-tag precedent); the packed `.nuspec` directly re-inspected confirming exactly four dependencies (`Azure.Security.KeyVault.Keys`/`Azure.Identity`/`SharedKernel.Cryptography`/`SharedKernel.Configuration`), and `SharedKernel.Cryptography`'s own already-packed `.nuspec` re-confirmed to carry zero `Azure.*` dependency. Consumer dependency-graph re-verification found the same pre-existing, unrelated NU1101 gap as P-491/P-492/P-493 (`SharedKernel.DataPrivacy`/`SharedKernel.Localization` nupkgs absent from this sandbox's feed) — confirmed unrelated, not chased; unlike those three, no `SharedKernel.Consumer.Tests` source edit was made either, since this phase named no such deliverable. Interface Contracts/Packages/Implementation Rules/DI Registration sections above updated from "design-locked, implementation pending" to "shipped", and D-76's own state-map row text corrected in place. `SK.01.P494` fully `●` (7/7) — P-495/P-496 remain design-locked, implementation pending, unaffected by this pass (core-phase-implementer)
- [2026-09-08] P-495 implemented — the thirteenth `01.Core` package, `SharedKernel.Cryptography.Argon2`, shipped exactly per the D-77/D-78/D-79 design already recorded above, with no design correction needed unlike P-494's D-76 (the Hash/Verify/Options/DI shapes described there matched the real implementation exactly — verified by comparing line by line before writing this entry, not assumed). Pinned `Konscious.Security.Cryptography.Argon2` at 1.3.1 (latest stable, MIT, pure-managed — verified via its GitHub source and NuGet listing, not assumed). `Argon2idOneWayHasher.Verify`'s "never throws" catch clause was written against Konscious's REAL exception types — `InvalidOperationException`/`NotSupportedException`, confirmed by direct source inspection — since the design brief's own guess of `ArgumentOutOfRangeException` was wrong; recorded as a new Implementation Rule so a future Konscious upgrade knows to re-verify it. `Argon2CryptographyOptions` carries a genuinely real `[Range]` floor/ceiling on all three properties (7168/2/1 floors, the smallest value across OWASP's own four-row acceptable-configurations table; 2097152/10/16 ceilings) — deliberately designed from the start to not repeat `Pbkdf2Iterations`'s original nominal-only `[Range(1, int.MaxValue)]` floor (that fix is the separate P-512/WO-083). 45/45 new `SharedKernel.Cryptography.Argon2.Tests` passing; full solution build re-verified (0 errors, 80 pre-existing warnings unrelated). Packed to the local feed (`1.0.0-alpha.0.858`, no `<Version>` added, same devops-lead git-tag precedent as P-491–P-494); its `.nuspec` directly re-inspected confirming exactly three dependencies and confirming `SharedKernel.Cryptography`'s own already-packed `.nuspec` still carries zero `Konscious`/`Argon2` dependency. Unlike the P-491–P-494 entries above, this phase did NOT stop at the pre-existing `SharedKernel.Consumer.Tests` NU1101 restore gap — it repacked all nine transitively-needed sibling packages (`SharedKernel.Primitives`/`.Core`/`.Configuration`/`.FeatureManagement`/`.Guards`/`.Cryptography`/`.Compression`/`.Validation`/`.Validation.FluentValidation`/`.Cryptography.KeyVault.Azure`/`.DataPrivacy`/`.Localization`) at the current source, all landing at the identical `1.0.0-alpha.0.858` height, specifically so the consumer dependency-graph test could genuinely run rather than be skipped again — 77/77 `SharedKernel.Consumer.Tests` passing, including four new tests for this package. Packages/Technology Stack/Interface Contracts/Implementation Rules/DI Registration/AOT/Test Rules sections above updated from "design-locked, implementation pending" to "SHIPPED". `SK.01.P495` is 10/11 `●` in `01.Core/state-map.md` — every deliverable this session controls is done; only `S-25`'s `.slnx`-registration sub-item is `⚑` blocked on the root-owned `Platform.SharedKernel.slnx`, per this session's own explicit shared-file protocol (which also confirmed, via `git log`/`git status` before proceeding, that the already-modified `01.Core/state-map.md`/`CLAUDE.md`/`SharedKernel.Cryptography.KeyVault.Azure/*` found on disk at session start were uncommitted P-491–P-494 prior-session state, not a live concurrent agent). Root `state-map.md`/`CLAUDE.md`/`Platform.SharedKernel.slnx` were not touched — closing Phase Backlog `P-495` and updating the root Domain Summary Board row are left to the user's own centralized process, mirroring the `SK.01.P487`/`SK.01.P491`–`P494` precedent (core-phase-implementer)
- [2026-09-08] P-496 implemented (WO-081, C-92→C-95/T-73/T-74/DO-39) — `AzureKeyVaultEncryptionKeyProvider` hardening shipped exactly per the D-80→D-83 design, with two implementation-time refinements over its literal wording (both discovered and corrected while implementing, not assumed from the brief — the same discipline P-494's D-76 correction established, and explicitly flagged in this pass's own dispatch message as expected): (1) the connection-reuse cache is keyed by (Azure key name, key VERSION), not by Azure key name alone as D-80 literally said — `UnwrapDataKeyAsync` must pin to the exact historical Azure key version that wrapped a given data key, which can legitimately differ from "current" after an out-of-band Azure-side key rotation between two local-DEK mints; keying by name alone would silently collide two real key versions onto one cache slot the first time that ever happened; (2) `UnwrapDataKeyAsync`'s `masterKeyId` parser accepts BOTH the new short `"{azureKeyName}/{version}"` shape and the legacy full Key Vault key identifier URI shape, not new-shape-only — a low-cost defensive addition since the envelope-wrap path's caller (never this provider) is the durable store for that value. The durable version registry (`VersionSecretPayload` + a source-generated `AzureKeyVaultJsonSerializerContext`, a new `SecretClient` field) stores each minted tag's wrapped-DEK + short master-key id as its own Key Vault Secret, plus a shared `"current version"` pointer secret read LIVE on every `GetCurrentKeyAsync` call (no internal caching of which tag is current — bounded-TTL caching of that stays an externally-composed `CachedEncryptionKeyProvider` concern, unchanged). `GetCurrentKeyAsync` throws `InvalidOperationException` rather than auto-minting when nothing has ever been minted — a deliberate design choice (not merely inferred): auto-minting on first use would silently reintroduce the exact per-process "current key" accident this whole phase exists to eliminate. `MintNewVersionAsync` provides no cross-caller mutual exclusion by design (documented explicitly, XML docs in capitals) — an accepted limitation given its intended low-frequency, ops-triggered usage; no data is ever lost by a race since every minted version's secret independently remains resolvable. T-73's four required acceptance criteria — a superseded version still decrypts after a mint; a second `GetKeyAsync` for an already-resolved tag costs zero further Key Vault calls; two independently-constructed provider instances sharing one simulated vault converge `GetCurrentKeyAsync` on the identical tag; a legacy pre-P-496 envelope-shaped `keyId` still resolves via the fallback path — are all proven via a NEW `AzureKeyVaultCallCountingFakes.cs` test double built by SUBCLASSING the real, non-sealed `KeyClient`/`SecretClient`/`CryptographyClient` types (their `protected` parameterless ctors and `virtual` members make this legal from outside the SDK's own assembly), constructed through a NEW `internal`-only test-seam constructor overload on `AzureKeyVaultEncryptionKeyProvider` exposed via a project-scoped `InternalsVisibleTo` — deliberately narrow: every extra parameter is unregistered in DI with no default value, so MS.DI's automatic constructor-selection can never pick it for the real `AddSharedKernelAzureKeyVaultCryptography` registration (verified, not merely asserted, by the fact this repo's real DI registration test file needed zero changes). Azure SDK model types (`KeyVaultKey`/`WrapResult`/`UnwrapResult`) expose only an `internal` constructor and/or `internal`-setter properties from outside the SDK's own assembly, so constructing realistic fake return values required reflection over those exact members — confirmed feasible via a throwaway scratch probe BEFORE writing the real fakes file, isolated entirely inside that one test-support file, never production code. T-74 added an always-exercised `MintNewVersionAsync_UnreachableVault_ThrowsInsteadOfSilentNoOp` fail-closed proof plus an env-gated `MintNewVersionAsync_ThenEncryptUnderNewCurrent_ThenDecryptUnderOldTag_RoundTripsAgainstRealAzureKeyVault` test, mirroring T-54/T-65/T-71's existing skip-if-unavailable-gated real-vault posture exactly — no new harness. `SharedKernel.Cryptography.KeyVault.Azure/README.md` gained a "Key rotation and the durable version registry" section with the `MintNewVersionAsync` recipe; `01.Core/README.md`'s Azure Key Vault section gained matching "Connection reuse"/"durable version registry"/"Key rotation" content, plus package-table and dependency-diagram updates. Interface Contracts section above updated from "design-locked, implementation pending" to "SHIPPED", including the two corrections above recorded explicitly rather than silently. **BLOCKED, honestly reported rather than worked around: this phase's `.csproj` change adds a `PackageReference` to `Azure.Security.KeyVault.Secrets` with no corresponding root-owned `Directory.Packages.props` `PackageVersion` entry** — `dotnet build`/`dotnet pack`/`dotnet test` on this package (and its Tests project, and anything transitively referencing it) fail with NU1010 in this repository as committed. The exact line needed: `<PackageVersion Include="Azure.Security.KeyVault.Secrets" Version="4.7.0" />` (matching the already-pinned `Azure.Security.KeyVault.Keys` 4.7.0 — both exist side-by-side in the local NuGet cache, confirmed). Per this phase's own explicit dispatch instruction, `Directory.Packages.props` was NOT edited (a direct edit attempt was in fact blocked by this session's own tool-permission classifier before any file write occurred, independently confirming it is a protected root file). All code and every test in this changelog entry WAS genuinely compiled and executed — 30/30 tests green, including all pre-existing tests in this project unmodified by content — via a temporary, entirely out-of-tree (`%TEMP%`, never inside this repo) scratch harness that referenced the exact same production/test `.cs` files plus every transitive dependency DLL directly by `HintPath` (bypassing NuGet/CPM restage entirely, so the root-owned pin gap was never touched), including a hand-rolled reflection-based `[Fact]`/`[Theory]` runner substituting for `dotnet test` (full xUnit `Microsoft.NET.Test.Sdk`/VSTest infrastructure was not worth standing up out-of-tree for this one check). This is a genuine, reproducible verification of logical correctness, but it is NOT the same as `dotnet test` succeeding inside this repository as currently committed — that remains blocked until the pin above is added by whoever owns `Directory.Packages.props`. **P-42 (repack) is therefore `⚑` Blocked, not `●`** — packing and publishing an artifact to the shared local feed from a source tree that cannot actually restore/build in place would be actively misleading to any other agent or session that later tries to consume it. C-92/C-93/C-94/C-95/T-73/T-74/DO-39 are marked `●` — their deliverables (source code, tests, documentation) are complete and independently verified correct via the scratch harness above, distinct from P-42's genuinely-blocked packaging step. `SK.01.P496` is 11/12 `●` (D-80→D-83 already `●` from the design pass; C-92→DO-39 newly `●` this session; P-42 `⚑` Blocked) — propagate to root `state-map.md` only once the CPM pin lands and P-42 can actually run (core-phase-implementer)
- [2026-09-08] Five phases design-locked in a single pass for WO-083 (P-510→P-514) — the remaining security-cluster findings from the `01.Core` gold-standard audit that started WO-081 (14/14 `●` complete). All mutually independent, meant to implement as one batch; `D-84`/`D-85`/`D-86`/`D-87`/`D-88` all `●`, `C-*`/`T-*`/`DO-*`/`P-*` left `○`. **Two of the five design tasks corrected a factually wrong premise in their own dispatched brief**, found by reading real, current source rather than trusting the brief's prose — the same discipline WO-081's twenty-one prior corrections established. **P-510** (SEVERE) — `ResultTry`'s default exception mapping no longer leaks a caught exception's raw message into `Error.Message`; the raw detail instead flows through `System.Diagnostics.Activity.Current?.AddException(...)` (a real .NET 8+ BCL member, zero new dependency), honoring — not working around — this same audit's ratified `⊘ DECLINED` "no metadata bag on `Error`" ruling by using the platform's own already-ambient trace-context channel; every `ResultTry` catch clause also now excludes `OperationCanceledException`. **P-511** — fixes the cancellation-token leak in every `Lazy<Task<T>>`-shaped single-flight cache in this domain via a dedicated per-slot `CancellationTokenSource` (never a caller's own token) plus `Task.WaitAsync(callerCt)`. **Scope corrected during design**: the brief's named `AzureKeyVaultEncryptionKeyProvider` "current key" slot no longer exists (P-496 replaced it); the fix instead targets P-496's own two new caches, `CachedEncryptionKeyProvider`'s pre-existing cache, and a fourth site not named in the brief at all — `AzureKeyVaultAsymmetricKeyProvider`'s identical cache, found by auditing the rest of the same package family. **P-512** — a real `MinimumPbkdf2Iterations` floor (100,000, via the already-wired `[Range]`/`ValidateOnStart` path) closing the "iterations=1 passes validation" gap, plus a `Verify`-time `MaxVerifiableIterations` ceiling (2,000,000, a fixed constant independent of the configured value, checked before the expensive derive call) and an exact-32-byte subkey-length check closing a second CPU-exhaustion vector found while designing the first. **P-513** — a structural 32-byte `CryptographicKey.Material` length check in `AesGcmEncryptionService`, before any `AesGcm` construction, closing a silent AES-128/AES-192 downgrade gap every doc on this package claims cannot happen. **P-514** (BREAKING) — `ITotpReplayGuard`'s two-step `HasBeenUsedAsync`+`MarkUsedAsync` TOCTOU collapses into one atomic `TryMarkUsedAsync`; `TotpVerifier` gains optional `digits`/`stepSeconds`/`driftWindow`/`algorithm` parameters (source-compatible defaults) so its replay window matches what was actually validated; a new standalone `ITotpAttemptThrottle` seam ships, deliberately never wired into `TotpVerifier`'s constructor. **Premise corrected during design**: the brief's "zero blast radius" claim is verified FALSE — `12.Security.Totp`/P-452 is confirmed ALREADY SHIPPED (not queued as the brief and this file's own now-corrected prior wording assumed), and both `16.Testing`'s shipped `FakeTotpReplayGuard` and `12.Security.Totp.Tests`' own test-local `FakeTotpReplayGuard` are real implementers that will fail to compile — two companion migration phases are needed in those domains, reported to the coordinator, out of this file's jurisdiction to dispatch. Interface Contracts and Implementation Rules sections above updated for all five phases. 32 tasks added across five new phase keys (`SK.01.P510`→`SK.01.P514`; D-84→D-88, C-96→C-104, T-75→T-82, DO-40→DO-44, P-43→P-47); `01.Core/state-map.md` total tasks now 397. Root `state-map.md`/`CLAUDE.md` were not touched, per this session's shared-file protocol (core-arch-planner, WO-083)
- [2026-09-09] P-510/P-511/P-512/P-513 implemented and closed (WO-083, C-96→C-102/T-75→T-80/DO-40→DO-43/P-43→P-46) — all four shipped exactly per their locked D-84→D-87 designs, no further design corrections needed. **P-510**: `ResultTry`'s new public `DefaultUnexpectedMessage` constant is the fixed default `Error.Message`; `Activity.Current?.AddException(exception)` records raw detail once per flattened `AggregateException` inner exception; all four members' catch clauses narrowed to exclude `OperationCanceledException`. **P-511**: fixed at all four sites via a new package-internal `SingleFlightCache<TKey,TValue>` (`SharedKernel.Cryptography.KeyVault.Azure/Internal/`) shared by `AzureKeyVaultEncryptionKeyProvider`'s two caches and `AzureKeyVaultAsymmetricKeyProvider`'s cache — one implementation, not three hand-copied ones — plus `CachedEncryptionKeyProvider`'s own equivalent `CacheSlot`; both apply the identical dedicated-`CancellationTokenSource` + `Task.WaitAsync(callerCt)` + refcounted-abandonment-eviction pattern, with abandonment eviction refined during implementation to check `IsCompletedSuccessfully` at the exact moment the last waiter departs — never evicting a genuinely successful result, always evicting a still-pending-now-abandoned or already-faulted one, so a new caller can never join an already-doomed slot. `AzureKeyVaultAsymmetricKeyProvider` gained a new internal `(options, KeyClient?)` test-seam constructor purely to make this independently verifiable (T-78), mirroring the sibling class's existing one. **P-512**: `CryptographyOptions.MinimumPbkdf2Iterations` (100,000) via the existing `[Range]`/`ValidateOnStart` path; `Pbkdf2OneWayHasher.MaxVerifiableIterations` (2,000,000, private) ceiling plus exact-32-byte-subkey and `iterations<1` rejection, all checked before the expensive derive call. **P-513**: `AesGcmEncryptionService.EnsureKeySize`/`RequiredKeySizeBytes` (32, private) reject any non-32-byte `CryptographicKey.Material` in both `EncryptCore`/`DecryptCore`, throwing `CryptographicException` before any `AesGcm` construction. All four packed to the local feed (no `<Version>`/git tag — devops-lead's call). Interface Contracts/Implementation Rules sections above updated from "design-locked" to "shipped" for all sixteen P-510–P-513 references; three stale `ConcurrentDictionary<string, Lazy<Task<T>>>` cache-shape descriptions corrected to name the new `SingleFlightCache`. Full 151-project solution build 0 errors; `SharedKernel.Core.Tests` 104/104, `SharedKernel.Cryptography.Tests` 314/314, `SharedKernel.Cryptography.KeyVault.Azure.Tests` 69/69 all green. `P-514` deliberately untouched — its two companion migration phases in `16.Testing`/`12.Security` remain undispatched. `01.Core/state-map.md`'s four phase keys now fully `●` (5/5, 9/9, 6/6, 5/5). Root `state-map.md`/`CLAUDE.md` were not touched, per this session's explicit shared-file protocol — closing Phase Backlog `P-510`–`P-513` and any root Folder Map mention of WO-083 are left to the coordinator's own centralized process (core-phase-implementer)
- [2026-09-09] P-514 implemented and closed (WO-083, C-103/C-104/T-81/T-82/DO-44/P-47) — shipped exactly per its locked D-88 design. `ITotpReplayGuard`'s `HasBeenUsedAsync`+`MarkUsedAsync` TOCTOU collapsed into one atomic `TryMarkUsedAsync`; `TotpVerifier.VerifyAsync` gained optional `digits`/`stepSeconds`/`driftWindow`/`algorithm` (defaulted to the exact removed hardcoded values, inserted before the trailing `ct`), replay window now derived from those same actual values instead of the two removed `Default*` constants; new standalone `ITotpAttemptThrottle` seam shipped, never wired into `TotpVerifier`'s constructor. T-81's genuine `Barrier`-synchronized concurrency proof (a real `ConcurrentDictionary.TryAdd`-backed atomic guard, never an NSubstitute mock) proves exactly one success among concurrent identical-code submissions; T-82 proves the replay window matches non-default parameters (300s at `stepSeconds=60, driftWindow=2`, not the stale 90s). `01.Core/README.md`'s TOTP section and `ReadmeSampleCompileTests.cs` migrated to match. `SharedKernel.Cryptography` repacked to the local feed (no `<Version>`/git tag — devops-lead's call); 318/318 `SharedKernel.Cryptography.Tests` passing (was 314/314). **A THIRD real affected consumer was found during implementation, beyond the two D-88 already named**: `12.Security.Totp`'s PRODUCTION `Challenge/TotpChallengeService.cs` calls `TotpVerifier.VerifyAsync(identityKey, secret, code, ct)` with `ct` positional — the new parameters inserted before `ct` mean that positional argument now binds to `digits` and fails to compile (`CancellationToken` has no implicit conversion to `int`). This falsifies D-88's own "12.Security.Totp stays source-compatible" claim for this exact call site — verified against real source, not assumed. Reported to the coordinator and for `12.Security`'s own companion migration phase (P-528) to additionally fix, alongside its already-known `FakeTotpReplayGuard` migration; `16.Testing`'s companion migration (P-527) for its `FakeTotpReplayGuard` is unaffected by this new finding. Interface Contracts, Implementation Rules, Packages table, and Test Rules sections above updated from "design-locked" to "shipped" for every P-514 reference, with the new finding recorded explicitly rather than silently. `01.Core/state-map.md`'s `SK.01.P514` now fully `●` (7/7) — every WO-083 phase key (`SK.01.P510`→`SK.01.P514`) is now `●`. Root `state-map.md`/`CLAUDE.md` were not touched, per this session's explicit shared-file protocol — closing root Phase Backlog `P-514` (currently `○` Pending, titled "zero blast radius" — now known inaccurate) is left to the coordinator's own centralized process (core-phase-implementer)
- [2026-09-09] P-515–P-522, P-524–P-526 (WO-083 tail, eleven phases) design-locked — full task tables live in `01.Core/state-map.md` (`SK.01.P515`→`SK.01.P522`, `SK.01.P524`→`SK.01.P526`). Verified against real, current repository state before designing, correcting three claims in the dispatched brief rather than inheriting them: `P-517`'s "zero dependencies" ask was already ratified declined (fix the `<Description>` text, keep `AddClock()`); `P-520`'s named `SharedKernel.Cryptography` `<Description>` example is stale relative to the brief, not the repo (already reads "Shipped"), so the sweep retargeted a real leftover `"v2.0.0:"` version reference in that same field instead — and this file's own `SharedKernel.Primitives has zero NuGet dependencies` Implementation Rules line was ALSO stale (the same false claim, one layer deeper than the shipped `<Description>`) and is corrected directly in this pass, not left pending; `P-518`'s real registration-call count was grep-verified (3 already `TryAdd*` against ~27 plain `AddSingleton`/`AddKeyedSingleton` calls across eight DI methods), and design caught a genuine regression the brief's blanket instruction would have caused — `SharedKernel.Validation`'s `AddNationalIdValidator<TValidator>()` is a genuine multi-implementation collection and must convert to `TryAddEnumerable`, never a plain `TryAddSingleton`, or every country validator after the first silently vanishes; a second wrinkle, `SharedKernel.Localization`'s two `ILocalizationCatalog` registrations flipping from documented "last wins" to "first wins," is recorded as an intentional, XML-doc-updated side effect. Packages/Technology Stack/Interface Contracts/DI Registration/Implementation Rules sections updated with design-locked forward references for all eleven phases; package count and the Packages table are unaffected (no new package — every phase targets an existing shipped package). See `01.Core/state-map.md`'s own changelog entry for the full per-phase design rationale (core-arch-planner, WO-083)
- [2026-09-09] P-517 shipped — SharedKernel.Primitives.csproj's `<Description>` no longer claims "zero dependencies"; status flipped from design-locked to shipped in two places (core-phase-implementer)
- [2026-09-09] P-520 implemented and closed (WO-083, DO-50/P-53), docs-only — `SharedKernel.Primitives.csproj`'s `<PackageReleaseNotes>` rewritten from the stale "1.1.0 (WO-059 / P-384): ..." version-numbered heading to present-tense/lockstep-accurate prose (no per-package version number, cites WO-059/P-384 by phase ID only); `SharedKernel.Cryptography.csproj`'s `<Description>` had its leftover `"v2.0.0: IPasswordHasher renamed to IOneWayHasher"` reference reworded to `"Shipped (P-210-P-213/WO-034, breaking): ..."`, matching the phase-ID-only citation style every other entry in that same `<Description>` already uses — the `<Description>`'s own P-446 wording was independently re-verified already correct, confirming this phase's design-time correction (P-446 does NOT read "future breaking release" — it already reads "Shipped"). `01.Core/state-map.md`'s Package Board's five stale rows (`SharedKernel.Primitives`/`.Core`/`.Configuration`/`.FeatureManagement`/`.Guards`) corrected from Current Phase "—"/State `○` to "Published"/`●`; a new `SharedKernel.Cryptography.Argon2` row added (missing entirely before this phase). Zero `.cs` diff, as designed. Both `SharedKernel.Primitives` and `SharedKernel.Cryptography` repacked to the local feed at `1.0.0-alpha.0.867` (no `<Version>`/git tag added — devops-lead's call, same precedent as every prior WO-081/WO-083 repack). `01.Core/state-map.md`'s `SK.01.P520` now fully `●` (3/3). Root `state-map.md`/`CLAUDE.md`/`Directory.Packages.props`/`Platform.SharedKernel.slnx` were not touched — closing root Phase Backlog `P-520` is left to the coordinator's own centralized process, per this session's explicit shared-file protocol (core-phase-implementer)
- [2026-09-10] Test-reliability fix, no work order/phase — `CachedEncryptionKeyProviderTests.GetCurrentKeyAsync_InnerProviderFailureDuringRefresh_PropagatesToEveryWaitingCaller` (and its neighbor `GetCurrentKeyAsync_ConcurrentCallersPastExpiry_CallInnerProviderExactlyOnce`) were flaking on CI's contended `ubuntu-latest` runner, not locally. Root cause confirmed by direct reproduction, not assumed: both tests used a fixed `Task.Delay(200ms)` as a barrier to "prove" every `Task.Run`-scheduled concurrent caller had attached to `CachedEncryptionKeyProvider`'s shared in-flight slot before releasing a held inner call — under thread-pool scheduling delay, a caller could still be un-dispatched at the 200ms mark. In the failure test specifically, once the other (already-attached) callers observed the inner provider's one-shot `ThrowOnNextCall` failure and all departed, the last of them evicted the failed slot per P-511/WO-083's documented (and correct) eviction-on-unsuccessful-departure rule; a caller that arrives only after that eviction starts a genuinely fresh resolution against an already-consumed one-shot exception, so it returns a key instead of throwing — "No exception was thrown," exactly the observed CI failure. This was proven directly with a throwaway single-caller reproduction (attach → fault → sole-waiter eviction → late second call succeeds instead of throwing) before any fix was written. `CachedEncryptionKeyProvider` itself required no change — P-511's propagation-to-every-attached-waiter and single-flight guarantees hold correctly for every caller that is genuinely attached when the fault occurs; the defect was entirely in the test's synchronization. Fixed by replacing the fixed sleep with a `CountdownEvent` signaled by each caller immediately after `cached.GetCurrentKeyAsync()` returns its `Task`/`ValueTask` — since that call's entire synchronous prefix (dictionary lookup or `GetOrAdd`, `Interlocked.Increment` of the slot's waiter count) always completes before the method can suspend, signaling at that point is a mathematically deterministic proof of attachment, not a timing guess; `Release()` now waits on the countdown instead of sleeping a fixed duration. Swept the two P-511 cancellation-safety tests in this same file and all six in `SharedKernel.Cryptography.KeyVault.Azure.Tests/AzureKeyVaultCancellationSafetyTests.cs` for the identical anti-pattern: all of them start their (at most two) concurrent callers via direct synchronous method calls rather than `Task.Run`, so each caller's attach-time synchronous prefix has already run by the time the next line executes — there is no thread-pool dispatch race for `Task.Delay` to paper over there, and those files were left unchanged. Verified with real `dotnet test` runs: `SharedKernel.Cryptography.Tests` 324/324 and `SharedKernel.Cryptography.KeyVault.Azure.Tests` 70/70, plus 20 repeated runs of the two fixed tests with zero failures (agent, no phase)
- [2026-09-11] P-529 (user-directed pre-publish hardening of `SharedKernel.Primitives`) — six defects found by executing the compiled assembly and running the trim/AOT analyzers, four contradicting the affected type's own XML docs; `default(Result).Error` no longer returns `null`, `ValidationResult`/`<T>` now snapshot and compare by value, a real `IL2059` in an `aot`-tagged package resolved with a justified suppression, `TryFromValue(null)` returns `false`, both `Failure` factories reject null; added `ErrorCodes.Forbidden`, `WellKnownBaggageKeys.TenantId`, `SmartEnum` `IComparable`/`TryFromName`/`SmartEnumJsonConverter`, `[DebuggerDisplay]` on six types; XML docs ship for the first time and the README was rewritten 38 → ~250 lines. Two new domain-wide Implementation Rules capture the reusable lessons: a constraint survey must include generic forwarders in OTHER packages (the `IComparable<TValue>` widening broke `SharedKernel.Core` with `CS0314`), and STJ code must use the `JsonTypeInfo<T>` overloads only (the obvious ones are `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` and nearly reintroduced the very defect being fixed). 239/239 package tests, 5,261 green across the solution (agent)
- [2026-09-11] P-530 (user-directed pre-publish hardening of `SharedKernel.Configuration`, the next package after P-529 — chosen on measured grounds: 23 referencing projects, the most of any package depending on Primitives alone, and the gate for the whole `SharedKernel.Cryptography` sub-tree) — six defects, all found by execution or by the trim/AOT analyzers, none by reading. **The severe one:** `AddValidatedOptions<TOptions, TValidator>` registered via `TryAddSingleton<IValidateOptions<TOptions>, TValidator>()`, but `IValidateOptions<T>` is a multi-implementation COLLECTION service — the options pipeline runs every registered validator for a type, not the first — so the caller's validator was silently dropped whenever any other validator for that options type already existed, including the one this package's own sibling overload adds; measured, a cross-property rule never ran and configuration violating it started the host cleanly. That is precisely the regression class this domain's own `01.Core/README.md` already documents as named `TryAdd` exception #2, and `SharedKernel.Cryptography.KeyVault.Azure` hand-rolls `TryAddEnumerable` beside its own `AddValidatedOptions` call to route around it — the convention was recorded after P-518 hit it downstream, while the package that CAUSES it was never fixed, and the pre-existing `CalledTwice_RegistersValidatorOnlyOnce` test asserted the broken behaviour as the intended contract. Also fixed: two distinct validators for one options type lost the second (same cause); the package advertised `aot` in `PackageTags` and "AOT-clean" in its README while emitting 6× `IL2091` + 4× `IL2026` + 2× `IL3050` into every trimming consumer's build, because `OptionsBuilder<T>.Bind` carries `[RequiresUnreferencedCode]`+`[RequiresDynamicCode]` and BOTH overloads call it — so only *validation* was ever reflection-free, never *binding*, and a generic library wrapper structurally cannot fix that since the config-binding source generator intercepts `Bind` in the calling assembly; the DataAnnotations overload was not idempotent (4 duplicated failure messages for 2 broken properties on a second registration, because the BCL's `ValidateDataAnnotations()` uses a plain `AddSingleton`); the nuspec forced a DEAD `SharedKernel.Primitives` dependency on every consumer, with no `SharedKernel` type anywhere in the source; and no `.xml` shipped, the same per-project `GenerateDocumentationFile` cause as P-529. `01.Core/CLAUDE.md`'s own public-surface block additionally documented a `[ValidateOptions]` marker attribute that does not exist in this package or anywhere in the repo — corrected here. **Added** `ISectionBoundOptions` (`static abstract string SectionName`, zero reflection, resolved as a direct static call through the generic type parameter) plus two `IConfiguration` overloads reading it, making the platform's section-path convention compile-enforced for the first time — 33 options types across 12 domains already declare a `SectionName` constant and 28 call sites retype `GetSection(X.SectionName)`; named-options support on all four overloads; and a `validateDataAnnotations` flag composing DataAnnotations with a custom validator, which the `TryAddSingleton` defect had made impossible. **One design decision that went against the first instinct:** the obvious `TryAddEnumerable` fix for the idempotency defect would have introduced a new one — it de-duplicates on implementation type, every named instance shares `DataAnnotationValidateOptions<TOptions>`, and that validator is itself name-scoped and skips other names, so every named instance after the first would have been left entirely unvalidated; the fix is a per-name check over `ServiceDescriptor.ImplementationInstance`, possible only because the validator is registered as a pre-built immutable instance rather than through a factory. Verified: 41/41 package tests (was 10), every fix pinned by executed perturbation (2/2/2/3 failures across four independent reverts); 12 IL warnings → 0 under both analyzers; full solution 0 errors with no new warning at any of the 27 in-repo `AddValidatedOptions` call sites; 5,310 tests across 49 projects, 0 failures; packed nupkg ships its `.xml` (10 documented members) and declares zero `SharedKernel.*` dependencies — so the package is now publishable with nothing ahead of it in the publish workflow's dependency gate; every README sample compiled and executed. Only the publish itself (P-56) remains `○`
