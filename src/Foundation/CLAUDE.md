# 01.Core — Domain Brain

> The foundation every other package builds on: functional primitives, railway extensions and guards, the platform
> registries, the execution context (caller, tenant, correlation, unit of work, audit writer), validated options, feature
> flags, cryptography, compression, validated identifiers, data privacy and localization. Ten Foundation packages plus
> three Adapters that each wrap one third-party library. It owns **no** identity/authentication (`12.Security`), no
> `IHealthCheck` or host wiring (`13.ServiceDefaults`), no HTTP mapping (`14.Presentation`), no mediator, ORM or ASP.NET
> Core dependency. Cryptography lives here, not in `12.Security`, because workers with no identity stack need it.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Primitives` | Foundation | `Result`/`Result<T>`, `Error`/`ErrorType`/`ErrorCodes`, `ValidationResult<T>`, `IClock`, `IIdGenerator`, `SmartEnum`, `IReadinessProbe` (`.Health`), `LoggingEventIdRanges`, `WellKnown*` registries. Only NuGet dependency: `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `SharedKernel.Execution` | Foundation | `IRequestContext` and its implementations, `RequestContextScope`, `IRequestContextAccessor`, `RequestContextPropagation`, `CorrelationIds`, `TenantId`, `TenantScope`, `IUnitOfWork`, `IAuditTrailWriter`. References `Primitives` only |
| `SharedKernel.Core` | Foundation | Base exceptions (`error.ToException()`), railway extensions, `ResultTry`, `ResultCombine`, `Guard.Against.*`/`Guard.Throw.*` (namespace `SharedKernel.Guards`). References `Primitives` only |
| `SharedKernel.Configuration` | Foundation | `AddValidatedOptions`, `ISectionBoundOptions`, `OptionsStrictness`; always `ValidateOnStart` |
| `SharedKernel.FeatureManagement` | Foundation | OpenFeature `IFeatureClient` over `Microsoft.FeatureManagement`; typed `FeatureFlag<T>` |
| `SharedKernel.Cryptography` | Foundation | AES-256-GCM (async + sync), envelope encryption, signing, HMAC, PHC one-way hashing, content hashing, fixed-time comparison, secure random, HOTP/TOTP. BCL only; references `Primitives`, `Configuration` |
| `SharedKernel.Compression` | Foundation | `IPayloadCompressor`: framed Brotli (default) and gzip, raw interop modes. BCL only; references `Primitives`, `Configuration` |
| `SharedKernel.Validation` | Foundation | Validated identifiers (`Iban`, `Bic`, `CardNumber`, `VatNumber`, `NationalId`, …), `ValidationErrorCodes`/`ValidationMessages` (Turkish bundled). References `Primitives`, `Core`, `Localization` |
| `SharedKernel.DataPrivacy` | Foundation | `PrivacyTaxonomy` (23 classifications, one `…DataAttribute` each), redactors, `PiiMasking`, `Pseudonymizer`, `IDataSubjectRequestHandler`. References `Primitives`, `Microsoft.Extensions.Compliance.Abstractions` |
| `SharedKernel.Localization` | Foundation | `LocalizedMessage.Define<…>` → `ToError`, named-placeholder templates, in-memory and `IStringLocalizer` catalogs |
| `SharedKernel.Validation.FluentValidation` | Adapter | `MustBeValid*()` rules; `AddFluentValidationRequestValidators()` bridging `IValidator<T>` to kernel `IRequestValidator<T>`. References `Validation`, `SharedKernel.Application`, `FluentValidation` |
| `SharedKernel.Cryptography.KeyVault.Azure` | Adapter | Key Vault data keys (secret versions, master-key wrap, envelope provider, `encryption-key-provider` readiness probe) and signing keys (remote sign, local verify) |
| `SharedKernel.Cryptography.Argon2` | Adapter | `Argon2idOneWayHashAlgorithm` (Konscious, pure managed), selected by `OneWayHashing:Algorithm` |

Also here: `SharedKernel.Cryptography.Testing` and `SharedKernel.FeatureManagement.Testing` (Testing tier, catalogued by
`16.Testing`), and `SharedKernel.Consumer.Tests` — a test project over the **packed** 01.Core packages, run by CI's
`packaging-verify` job.

## Public Entry Points

Overloads, option keys and defaults: each package's `README.md` (overview in `src/Foundation/README.md`).

- **Primitives:** `services.AddClock()`; `IIdGenerator` has no extension (`AddSingleton<IIdGenerator, UuidV7IdGenerator>()`);
  `services.AddReadinessProbe<TProbe>()` (mapped by `13.ServiceDefaults`' `AddSharedKernelReadiness()`); `Error(Code, Message, Type)`
  with `Details` and `MessageArguments`; `ErrorType` (`Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`,
  `BusinessRule`, `Unavailable`, `Timeout`, `Unexpected`, `None`).
- **Execution:** no registration — `RequestContextAccessor` is `TryAddSingleton`ed by each adapter, `IRequestContext` by the
  host (`AddSharedKernelRequestContext()`, `13.ServiceDefaults.Security`). `RequestContextScope.Begin`, `context.WithTenant`,
  `RequestContextPropagation.WriteHeaders`/`ReadHeaders`, `CorrelationIds`, `TenantScope.Global`/`For`/`FromNullable`.
- **Core:** no DI. `Guard.Against.X` → `Error?`; `Guard.Throw.X` throws `DomainException`; `ResultTry.Try`/`TryAsync`, `ResultCombine.Combine`.
- **Configuration:** `services.AddValidatedOptions<TOptions>(…)` / `AddValidatedOptions<TOptions, TValidator>(…)`; the
  `IConfiguration` overloads read `ISectionBoundOptions.SectionName` (a `static string` property, not a `const`).
- **FeatureManagement:** `services.AddSharedKernelFeatureManagement(rootConfiguration, o => …)` — pass the **root**
  `IConfiguration`, call once (a second call throws); flags via `FeatureFlag.Boolean/String/Integer/Double/Object<T>(key, default)`.
- **Cryptography:** `services.AddSharedKernelCryptography(configuration)` → `ICryptographyBuilder` with `.Add…()` opt-ins
  per capability; adapters add `.AddArgon2id(…)`, `.AddAzureKeyVaultEncryption(…)`, `.AddAzureKeyVaultSigning(…)`. The
  service registers its key providers and its `ITotpReplayGuard`.
- **Compression:** `services.AddSharedKernelCompression(configuration)` — framed Brotli unkeyed, every mode keyed
  (`CompressionServiceCollectionExtensions.*PayloadCompressorKey`).
- **Validation:** `services.AddSharedKernelValidation().AddNationalIdValidator<T>()`; `Iban.Create(value)` → `Result<Iban>`;
  FluentValidation `RuleFor(x => x.Iban).MustBeValidIban()`.
- **DataPrivacy:** no extension of its own — `services.AddRedaction(r => r.SetPrivacyRedactors())` plus
  `builder.Logging.EnableRedaction(...)` in the host; the service implements `IDataSubjectRequestHandler`.
- **Localization:** `services.AddLocalizationCatalog(c => …)` or `AddStringLocalizerCatalog<TResource>()`; exactly one catalog.

## Rules & Invariants

**Tiering and dependencies**

1. No package here references ASP.NET Core; `Execution` never gains a mediator, ORM or ASP.NET Core dependency.
2. `Cryptography` and `Compression` stay BCL-only; Azure SDKs stay in `.KeyVault.Azure`, Konscious in `.Argon2`,
   FluentValidation in `.Validation.FluentValidation` — never a transitive dependency of the base package.
3. DI extensions use `TryAdd*` so a second call never double-registers and a consumer registration made first wins;
   multi-implementation services (`INationalIdValidator`, `IValidateOptions<T>`) use `TryAddEnumerable`.
4. No static mutable state; no test-only hooks in production code (no `InternalsVisibleTo` for tests, no capture callbacks).
5. Every package tracks its public API (`PublicAPI.*.txt`) and sets `GenerateDocumentationFile=true` in its own csproj
   (the central default never applies).

**Primitives**

6. `Result<T>` is a sealed class; `Result` is a readonly struct. Only `.Value`/`.Error` throw on wrong access.
   `default(Result)` has no error: `Result.Error` throws a named `InvalidOperationException` — never return `null`.
7. `Error` is never null (`Error.None` = no error) and carries no exception or metadata bag. `MessageArguments` holds
   translation values only, stays out of equality and JSON, and is filled only by `LocalizedMessage.ToError`.
8. `IHasSuccessFlag` is a zero-member marker; `IResultOfT<T>` exposes exactly `IsSuccess`/`IsFailure`/`Value`;
   `IFailureFactory<TSelf>` is satisfied by `Result<T>.Failure` (static abstract), never reflection. `Result` implements neither.
9. `ValidationResult`/`ValidationResult<T>` keep hand-written equality over a private error array, never record equality
   over an `IReadOnlyList<Error>`.
10. `IClock` is the only time source (SK0001); `SystemClock` defaults to `TimeProvider.System`. `UuidV7IdGenerator` uses
    `Guid.CreateVersion7()` only.
11. `SmartEnum` lookups use a list built at type initialization (cctor forced once via `RuntimeHelpers.RunClassConstructor`).
    `TValue` is constrained to `IEquatable<TValue>` alone — widening it breaks `Guard.Against.InvalidSmartEnum`.
12. JSON code uses `JsonTypeInfo<T>` overloads via `options.GetTypeInfo(Type)`; no `JsonConverterFactory`.
    `[DebuggerDisplay]` members read backing fields, never throwing properties. `Execution`, `Primitives` and `Core` stay reflection-free.

**Registries**

13. `LoggingEventIdRanges` fields are `const int` = domain number × 1000; add one per new domain, never renumber. It
    reserves only the 1000-wide block; sub-blocks are each domain's documentation.
14. `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys` are `const string` wire formats; changing one is a
    breaking cross-domain change. `WellKnownBaggageKeys.TenantId` is `"TenantId"` on purpose (the log property emitted by
    `BaggageLogRecordProcessor`). A baggage key belongs here only if platform middleware writes and replaces it.

**Execution**

15. Every inbound adapter opens exactly one `RequestContextScope`; a tenant refinement opens an inner scope with
    `WithTenant`. Outbound adapters read `IRequestContextAccessor` and use `RequestContextPropagation` — never a second
    header mapping. No `01.Core` package reads caller identity from `Activity` baggage.
16. `PropagatedRequestContext.HasPermissionAsync` never grants (a header cannot). `SystemRequestContext` takes an
    explicit permission set — never "all permissions".
17. `TenantId` never holds `Guid.Empty` (constructor throws); "no tenant" is `null` and consumers fail closed. Its
    string form (`"D"`) is embedded in stored formats (RLS setting, key ids, cache keys, idempotency keys) — never change it.

**Core**

18. Base exceptions always carry an `Error`; no string-only constructors.
19. `ResultTry` rethrows only `OperationCanceledException`. Its default mapping uses a fixed message, never the exception's
    type/message (the exception goes on `Activity.Current`); it flattens `AggregateException` and leaves `onException` mappers alone.
20. `ResultCombine.Combine` evaluates every input and returns every failing `Error`, never short-circuits.
21. Async railway extensions avoid `async`/`await` where they only await the input (`ResultTry.TryAsync` is the exception).
22. Guards return `Error?` (null = passed, never `Error.None`); `Guard.Throw.*` wrap `Against.*`. `IGuardClause` is a
    memberless marker. Collection guards enumerate once. The regex cache is bounded (256 patterns) with a match timeout.

**Configuration**

23. `AddValidatedOptions` always calls `ValidateOnStart` (fails fast only under a real `IHost`). DataAnnotations register
    as a pre-built `DataAnnotationValidateOptions<T>` with a per-name duplicate check — not `.ValidateDataAnnotations()`
    (duplicates failures) and not name-blind `TryAddEnumerable`.
24. Public overloads declare `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`/`[DynamicallyAccessedMembers]`, never
    suppress them; `aot` must not appear in this package's `PackageTags`.

**FeatureManagement**

25. Services evaluate flags only through `IFeatureClient` with a declared `FeatureFlag<T>` (SK0002 flags the
    `IFeatureManager` family and OpenFeature's `Api.Instance`; the package uses an isolated `Api`).
26. Pass `configuration` to `AddFeatureManagement` unmodified — never `GetSection("FeatureManagement")`.
27. The provider never throws for a flag problem (returns the default with `FlagNotFound`/`TypeMismatch`/`ParseError`/
    `General`, logged once at 1301). Messages never include exception text or configuration values; telemetry never
    carries targeting key, user id or tenant id (Microsoft's own evaluation event stays suppressed).

**Cryptography**

28. Secrets use `ISecureRandomGenerator` (never `System.Random`/`Guid.NewGuid()`), `FixedTimeComparison` (never `==`/
    `SequenceEqual`) and `IOneWayHasher` (never raw SHA/MD5); `IContentHasher` is for non-secret fingerprints only.
29. One-way hashes are PHC strings; `Verify` bounds attacker-controlled costs before deriving; PHC parameter `k` is
    reserved for the pepper id. Minimum costs apply to new hashes only.
30. Symmetric encryption is AES-256-GCM only (32-byte key); `associatedData` is required; decryption returns `Result`
    (malformed/unauthenticated = Validation, unknown key = Unexpected) and never echoes key ids.
31. Sync and async are separate interfaces (`ISynchronousSymmetricEncryptionService` over
    `ISynchronousEncryptionKeyProvider`, which never does I/O). No sync-over-async bridges or runtime capability gates.
32. Stored formats carry a version byte or PHC id (`EncryptedPayload` 0x01, `EnvelopePayload` 0x02). Key ids from
    payloads are untrusted: `CachedEncryptionKeyProvider` never caches misses and is bounded; the Azure provider validates
    id shape and rate-limits forced refreshes.
33. Single-flight caching uses the internal `SingleFlightCache` (linked as source into `.KeyVault.Azure`) — never a copy.
34. Signing keys carry their algorithm (RSA ≥ 2048; ECDSA algorithm follows the curve); unknown key or malformed
    signature → `false`. Envelope unwrap accepts only configured master keys.
35. `AddSharedKernelCryptography` registers no key-dependent service; those are builder opt-ins.
36. TOTP: stateless generators; replay protection via the consumer's `ITotpReplayGuard` (`TryAcceptTimeStepAsync`,
    atomic); time from `IClock`; secrets ≥ 16 bytes; 6–8 digits; `ITotpAttemptThrottle` stays a caller-composed seam.

**Compression**

37. The frame (13-byte header, `CompressionAlgorithm` values) is a wire format: never renumber or resize; a change is a
    new frame version and readers keep every old version. Unknown version → `compression.malformed_payload`.
38. Decompression returns `Result` (catching `InvalidDataException` and Brotli's `InvalidOperationException`), is bounded
    by bytes actually produced (`MaxDecompressedSize`), and never pre-allocates from a claimed length.
39. Both compressors are sealed forwarders to the internal `CompressionCodec`, no shared public base. Framed mode must
    detect truncation. Compress before encrypting, never the reverse.

**Validation, DataPrivacy, Localization**

40. Identifiers are created only through `Create` (never throws for input), normalize, compare by `Value`, implement
    `IParsable<T>` + `ValidatedValueJsonConverter<T>`; no public constructors or string conversions.
41. One error code per failure, one `ValidationMessages` entry per code, with a Turkish line in `Localization/tr.json`.
    No message or argument contains the rejected value; FluentValidation rules never set `AttemptedValue`/`PropertyValue`.
42. `CardNumber.ToString()`/`NationalId.ToString()` are masked and must equal `PiiMasking`'s output (a test compares them).
    Currency data must stay identical to `SharedKernel.Domain`'s `CurrencyCatalog`.
43. Data classification is Microsoft's compliance model, read at compile time by the logging source generator. A
    classification's `TaxonomyName`/`Value` is never renamed; `PiiMasking` rules may only reveal less; `SetPrivacyRedactors`
    never sets the fallback redactor; `OnlineIdentifier` is tokenized only with a supplied `Pseudonymizer`.
44. `IDataSubjectRequestHandler`: unknown subject = empty success; a repeated `RequestId` returns the first outcome;
    legally kept data is `Retained`, not a failure. No implementation ships.
45. Translation APIs return the translation or the original text, never blank or an unfilled placeholder, and never throw
    for a missing translation (`TryFormat` on error paths). Placeholders are named only; `LocalizedMessage.Define` validates
    its argument names at definition.
46. The catalog is immutable, built and validated at registration; exactly one `ILocalizationCatalog` (a second registration throws).

## Decisions

| Decision | Why |
| --- | --- |
| `ErrorCodes` are nested static string constants, not enums | Consumers add local constants without forking the kernel |
| `ValidationResult` separate from `Result<T>` | Multi-error input validation vs single-error operation outcome |
| Guards live in `SharedKernel.Core` (namespace `SharedKernel.Guards`) | One package, no extra dependency |
| `IIdGenerator` has no `AddX()` extension | A one-line consumer registration; the generator is opt-in, never auto-replacing `Guid.NewGuid()` |
| `SharedKernel.Configuration` is deliberately not AOT-clean | Library-generic `Bind<TOptions>` cannot use the configuration-binding source generator; the requirement is declared, not hidden |
| Feature flags on OpenFeature over `Microsoft.FeatureManagement` | Vendor-neutral API, Microsoft's `feature_management` schema for targeting/variants |
| Compression is one package with keyed algorithms, not `.Brotli`/`.GZip` siblings | Small, closed, BCL-only algorithm set |
| No generic `Error` metadata bag | `Error` is a value-equal record; field details go in `Details`, placeholders in `MessageArguments` |

## Logging

Block `1000`–`1999` (`LoggingEventIdRanges.Core`), split into 100-wide sub-blocks (`LoggingEventIdRanges.PackageSubBlockWidth`).

| Sub-block | Package | Current EventIds |
| --- | --- | --- |
| `1300`–`1399` | `SharedKernel.FeatureManagement` | `1301` evaluation failed (`FeatureManagementEventIds.EvaluationFailed`, Warning, once per failure) |

No other `01.Core` package logs. A package that starts logging takes the next free sub-block, declares it in an internal
`…EventIds` class derived from `LoggingEventIdRanges.Core`, and records it here.

## Cross-Domain Couplings

- `Execution` is the ambient caller for every adapter (`13` middleware, `07` consume filter, `17` interceptor, `19` job
  runner, `11`/`15` outbound propagation).
- `IUnitOfWork`/`IAuditTrailWriter`: implemented by `06.Persistence`, consumed by `05.Application` behaviors — never redeclared.
- `IReadinessProbe`: implemented by every provider with an external dependency, mapped by `13.ServiceDefaults`.
- `WellKnown*` constants: consumed by `07`, `11`, `13`, `14`, `15`, `17`.
- `ErrorType` → HTTP/gRPC status maps live in `14.Presentation`; `ErrorCodes.Idempotency` is used by `05` and `14`.
- `Validation.FluentValidation` references `05.Application`'s `SharedKernel.Application` (`IRequestValidator<T>`).
- `Validation`'s currency list must match `03.Domain`'s `CurrencyCatalog`; `DataPrivacy` masks must match `Validation`'s.
- `Cryptography` is used by `06` (field encryption, audit HMAC), `07` (payload transform), `12` (TOTP), `15` (webhook signing), `17` (payload codec).
- `00.Governance` analyzers enforce this domain's rules at call sites (SK0001 time, SK0002 feature flags, SK0022 magic strings, SK0030 discarded `Result`).

## Testing

- Every package has a nested `*.Tests` project in the **Unit lane**; none needs Docker. `SharedKernel.Consumer.Tests`
  restores the packed packages (CI `packaging-verify`).
- Cross-transport propagation is proven end to end by `13.ServiceDefaults.Security`'s `EndToEndPropagationTests`.
- Pin every `LoggingEventIdRanges`/`WellKnown*` value literally; test `Against.*` and `Throw.*` separately; assert options
  failures at `IHost.StartAsync()`; use independently published vectors (RFC, SWIFT, python-stdnum) so tests are not
  circular; `.KeyVault.Azure` tests use SDK client subclasses (no network); README samples are compiled or run by tests.
- Fakes: `SharedKernel.Testing`, `SharedKernel.Cryptography.Testing`, `SharedKernel.FeatureManagement.Testing` — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- `SharedKernel.Configuration` binding is reflective: options with complex nested members are not trim-safe; keep
  options flat or bind by hand where native AOT is required.
- `SharedKernel.FeatureManagement` is not trim/AOT-safe (`Microsoft.FeatureManagement` binds filter parameters by
  reflection); re-check on each upgrade. FluentValidation's and the Azure SDK's AOT status is likewise unverified.
- Raw compression modes cannot detect truncation; framed stream compression with both input and output non-seekable
  cannot record the length up front (see the Compression README).
- VAT numbers issued to individuals are format-only where the check digit depends on personal data (BG 10-digit,
  CZ 9/10-digit, LV personal codes).
- RSA-wrapped envelopes are confidentiality-only against public-key holders.
