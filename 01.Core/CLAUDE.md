# 01.Core — Domain Brain

> The foundation every other package builds on: functional primitives (`Result<T>`, `Error`, `ValidationResult`),
> railway extensions and guard clauses, `IClock`/`IIdGenerator`/`SmartEnum`, the readiness-probe contract, the platform
> registries (`LoggingEventIdRanges`, `WellKnownHeaders`, `WellKnownBaggageKeys`, `WellKnownTagKeys`), the execution
> context (caller, tenant, correlation, unit of work, audit writer), validated options, feature flags, cryptography,
> compression, validated identifiers, data privacy and localization. Ten Foundation packages plus three Adapter packages
> that each wrap one third-party library. This domain deliberately owns **no** identity/authentication (`12.Security`),
> no `IHealthCheck` or host wiring (`13.ServiceDefaults`), no ProblemDetails/HTTP mapping (`14.Presentation`), no
> mediator, ORM or ASP.NET Core dependency, and no metadata bag on `Error`. Cryptography lives here, not in
> `12.Security`, because workers with no identity stack need it and `12.Security` depends on it, never the reverse.
> Consumers read `01.Core/README.md` and each package `README.md`; phase status is on the living board (`state-map.md`).

## Packages

| Package | Tier | Purpose |
|---|---|---|
| `SharedKernel.Primitives` | Foundation | `Result`/`Result<T>`, `Error`, `ErrorType`, `ErrorCodes`, `ValidationResult`/`ValidationResult<T>`, `IHasSuccessFlag`/`IResultOfT<T>`/`IFailureFactory<TSelf>`, `IClock`/`SystemClock`, `IIdGenerator`/`UuidV7IdGenerator`, `SmartEnum<TEnum,TValue>` + `SmartEnumJsonConverter<TEnum,TValue>`, `SharedKernel.Primitives.Health` (`IReadinessProbe`, `ReadinessReport`, `ReadinessStatus`), `LoggingEventIdRanges`, `WellKnown*` registries. Only NuGet dependency: `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `SharedKernel.Execution` | Foundation | `IRequestContext`, `ActorKind`, `SystemRequestContext`, `AnonymousRequestContext`, `PropagatedRequestContext`, `RequestContextScope`, `IRequestContextAccessor`/`RequestContextAccessor`, `RequestContextPropagation`, `CorrelationIds`, `TenantId`, `TenantScope`, `IUnitOfWork` (+ `TransactionRolledBackException`, `CommitOutcomeUnknownException`), `IAuditTrailWriter`/`AuditEntry`/`AuditOutcome`. References `Primitives` only |
| `SharedKernel.Core` | Foundation | Base exceptions (`SharedKernelException`, `DomainException`, `NotFoundException`, `ConflictException`, `ValidationException`, `UnauthorizedException`, `ForbiddenException`; `error.ToException()`), railway extensions (`Map`/`MapError`/`Bind`/`Ensure`/`Match`/`Tap`/`TapError`, sync/`Task`/`ValueTask`), `ResultTry`, `ResultCombine`, BCL extensions, and `Guard.Against.*`/`Guard.Throw.*` in namespace `SharedKernel.Guards`. References `Primitives` only |
| `SharedKernel.Configuration` | Foundation | `AddValidatedOptions` (DataAnnotations and/or `IValidateOptions<T>`, named instances, `OptionsStrictness`, `ISectionBoundOptions`), always `ValidateOnStart` |
| `SharedKernel.FeatureManagement` | Foundation | OpenFeature `IFeatureClient` over `Microsoft.FeatureManagement`: typed `FeatureFlag<T>`, `IFeatureTargetingContextAccessor`, `FeatureTargetingContext`, `FeatureFlagOptions` |
| `SharedKernel.Cryptography` | Foundation | AES-256-GCM (async + synchronous), rotation, envelope encryption, HKDF subkeys, RSA/ECDSA signing, HMAC-SHA256, PHC one-way hashing (PBKDF2, pepper, rehash), SHA-256 content hashing, fixed-time comparison, secure random, HOTP/TOTP. BCL only; references `Primitives`, `Configuration` |
| `SharedKernel.Compression` | Foundation | `IPayloadCompressor`: framed Brotli (default) and gzip, raw interop modes, `MaxDecompressedSize`. BCL only; references `Primitives`, `Configuration` |
| `SharedKernel.Validation` | Foundation | Validated identifier value types (`Iban`, `Bic`, `CardNumber`, `VatNumber`, `NationalId`, `CountryCode`, `CurrencyCode`, `PhoneNumber`, `Lei`, `AbaRoutingNumber`, `SepaCreditorId`), `IValidatedValue<T>`, `ValidationErrorCodes`/`ValidationMessages` (Turkish bundled), `Guard.Against.Invalid<T>`. References `Primitives`, `Core`, `Localization` |
| `SharedKernel.DataPrivacy` | Foundation | `PrivacyTaxonomy` (23 classifications) with one `…DataAttribute` each, `SetPrivacyRedactors()`, `PiiMasking`, `Pseudonymizer`, `IDataSubjectRequestHandler`. References `Primitives`, `Microsoft.Extensions.Compliance.Abstractions` |
| `SharedKernel.Localization` | Foundation | `LocalizedMessage.Define<…>` → `ToError` (fills `Error.MessageArguments`), named-placeholder `MessageTemplate`, immutable `InMemoryLocalizationCatalog`, `StringLocalizerLocalizationCatalog`, `catalog.Localize(error, culture)` |
| `SharedKernel.Validation.FluentValidation` | Adapter | `MustBeValid*()` rules for every identifier, `AddFluentValidationRequestValidators()` bridging `IValidator<T>` to the kernel `IRequestValidator<T>`. References `Validation`, `SharedKernel.Application` (Abstractions), `FluentValidation` |
| `SharedKernel.Cryptography.KeyVault.Azure` | Adapter | Key Vault data keys (secret versions, master-key wrap, envelope provider, `encryption-key-provider` readiness probe) and signing keys (remote sign, local verify) |
| `SharedKernel.Cryptography.Argon2` | Adapter | `Argon2idOneWayHashAlgorithm` (Konscious, pure managed), selected by `OneWayHashing:Algorithm` |

Also here: `SharedKernel.Consumer.Tests` — a test project over the **packed** 01.Core packages (`PackageReference`), run
by CI's `packaging-verify` job.

## Public Entry Points

### Primitives

- `services.AddClock()` (`ClockExtensions`) registers `SystemClock` as `IClock`. For a shared `TimeProvider`, register
  `new SystemClock(timeProvider)` yourself. `IIdGenerator` has no extension: `services.AddSingleton<IIdGenerator, UuidV7IdGenerator>()`.
- `services.AddReadinessProbe<TProbe>()` / `AddReadinessProbe(factory)` (`SharedKernel.Primitives.Health`). A probe has
  `Name` and `ProbeAsync(ct)` → `ReadinessReport` (`Healthy`/`Degraded`/…); `13.ServiceDefaults`' `AddSharedKernelReadiness()` maps them.
- `Error(Code, Message, Type)` sealed record; `Error.None`; `Details` (field errors) and `MessageArguments` (translation
  values). `ErrorType`: `None`, `Unexpected`, `Validation`, `NotFound`, `Conflict`, `Unauthorized`, `BusinessRule`,
  `Forbidden`, `Unavailable`, `Timeout`. `ErrorCodes` nested classes per type plus `Idempotency` and `Domain`.

### Execution

No registration method. `RequestContextAccessor` is `TryAddSingleton`ed by each adapter that needs it; `IRequestContext`
is registered by the host (`13.ServiceDefaults.Security`'s `AddSharedKernelRequestContext()`).
`RequestContextScope.Begin(context)` / `RequestContextScope.Current`; `context.WithTenant(tenantId)` /
`WithCorrelationId(...)` (`RequestContextExtensions`); `RequestContextPropagation.WriteHeaders`/`ReadHeaders`/`ParseActorKind`;
`CorrelationIds.New`/`IsValid`/`AcceptOrCreate`/`Current`; `TenantScope.Global`/`For`/`FromNullable`; `TenantId.FromNullable`.

### Core

No DI. `Guard.Against.X(...)` returns `Error?` (null = passed); `Guard.Throw.X(...)` throws `DomainException`.
`ResultTry.Try`/`TryAsync`, `ResultCombine.Combine`.

### Configuration

`services.AddValidatedOptions<TOptions>(section | configuration, name:, strictness:)` and
`AddValidatedOptions<TOptions, TValidator>(section | configuration, validateDataAnnotations: false, name:, strictness:)`.
The `IConfiguration` overloads read the path from `ISectionBoundOptions.SectionName` (a `static string` property, not a
`const`). `OptionsStrictness`: `None`, `RequireSection`, `RejectUnknownKeys`.

### FeatureManagement

`services.AddSharedKernelFeatureManagement(rootConfiguration, o => o.ValidateOnStart(Flags.X))` — pass the **root**
`IConfiguration`; call once (a second call throws). Declare flags with `FeatureFlag.Boolean/String/Integer/Double/Object<T>(key, default)`;
register an `IFeatureTargetingContextAccessor` (default reads the open `RequestContextScope`); `FeatureTargetingContext.ForTenant(TenantId)`.
`FeatureFlagOptions`: `EvaluateOncePerScope`, `ScopeResultLifetime`, `Telemetry` (`FeatureTelemetryMode`), `AddFeatureFilter<T>()`, `ConfigureOpenFeature(...)`.

### Cryptography (section `SharedKernel:Cryptography`, `…:Pbkdf2`)

`services.AddSharedKernelCryptography(configuration)` → `ICryptographyBuilder` with opt-ins `.AddSymmetricEncryption()`,
`.AddSynchronousSymmetricEncryption()`, `.AddEnvelopeEncryption()`, `.AddAsymmetricSigning()`, `.AddTotpVerification()`,
`.AddOneWayHashAlgorithm<T>()`. Adapters extend the builder: `.AddArgon2id(configuration)` (`SharedKernel:Cryptography:Argon2`),
`.AddAzureKeyVaultEncryption(configuration)` (`SharedKernel:Cryptography:KeyVault:Azure:Encryption`),
`.AddAzureKeyVaultSigning(configuration)` (`…:Azure:Signing`). Key providers are registered by the service
(`StaticEncryptionKeyProvider`, `InMemorySigningKeyProvider`, `CachedEncryptionKeyProvider`, a consumer `ITotpReplayGuard`).

### Compression (section `SharedKernel:Compression`)

`services.AddSharedKernelCompression(configuration)` — framed Brotli unkeyed, plus keyed services under
`CompressionServiceCollectionExtensions.BrotliPayloadCompressorKey` (`"Brotli"`), `GZipPayloadCompressorKey` (`"GZip"`),
`RawBrotliPayloadCompressorKey` (`"Brotli.Raw"`), `RawGZipPayloadCompressorKey` (`"GZip.Raw"`).

### Validation / FluentValidation

`services.AddSharedKernelValidation().AddNationalIdValidator<T>()`; Turkish messages via
`AddLocalizationCatalog(c => c.AddValidationTranslations())`. Create values at the edge: `Iban.Create(value)` →
`Result<Iban>`. FluentValidation: `RuleFor(x => x.Iban).MustBeValidIban()`; `services.AddFluentValidationRequestValidators(assembly)`.

### DataPrivacy

No `IServiceCollection` extension: `services.AddRedaction(r => r.SetPrivacyRedactors())` (or `SetPrivacyRedactors(pseudonymizer)`)
plus `builder.Logging.EnableRedaction(...)` in the host; the service implements `IDataSubjectRequestHandler`.

### Localization

`services.AddLocalizationCatalog(c => c.AddJsonDirectory(path).Add(code, culture, text))` or
`services.AddStringLocalizerCatalog<TResource>()` (after `AddLocalization()`); exactly one catalog per application.

## Rules & Invariants

**Tiering and dependencies**

1. Foundation packages reference only Foundation packages; no package here references ASP.NET Core. `Execution` never
   gains a mediator, ORM or ASP.NET Core dependency.
2. `Cryptography` and `Compression` stay BCL-only; Azure SDKs stay in `.KeyVault.Azure`, Konscious in `.Argon2`,
   FluentValidation in `.Validation.FluentValidation` — never a transitive dependency of the base package.
3. Every DI extension uses `TryAddSingleton`/`TryAddKeyedSingleton`/`TryAddEnumerable` so a second call never
   double-registers and a consumer registration made first wins. Multi-implementation services (`INationalIdValidator`,
   `IValidateOptions<T>`) use `TryAddEnumerable`, never `TryAddSingleton`.
4. No static mutable state; no test-only hooks in production code (no `InternalsVisibleTo` for tests, no capture callbacks).
5. Every package tracks its public API (`PublicAPI.*.txt`; CS1591/RS0016/RS0017/RS0024/RS0025 are errors) and sets
   `GenerateDocumentationFile=true` in its own csproj (the central default never applies).

**Primitives**

6. `Result<T>` is a sealed class; `Result` is a readonly struct. Only `.Value`/`.Error` throw on wrong access.
   `default(Result)` has no error: `Result.Error` throws a named `InvalidOperationException` — never return `null`.
7. `Error` is never null — `Error.None` means "no error"; `Failure` factories and implicit conversions reject `null`.
   `Error` carries no exception and no metadata bag. `MessageArguments` holds translation values only, stays out of
   equality and JSON (`[JsonIgnore]`), and is filled only by `LocalizedMessage.ToError`.
8. `IHasSuccessFlag` stays a zero-member marker; `IResultOfT<T>` exposes exactly `IsSuccess`/`IsFailure`/`Value`.
   `IFailureFactory<TSelf>` is satisfied by `Result<T>.Failure` (static abstract member) — never by reflection; `Result`
   implements neither `IResultOfT<T>` nor `IFailureFactory<Result>`.
9. `ValidationResult`/`ValidationResult<T>` keep hand-written `Equals`/`GetHashCode` and snapshot errors into a private
   array — never compiler-generated record equality over an `IReadOnlyList<Error>`.
10. `IClock` is the only time source in this domain (`DateTime.UtcNow` is a violation); `SystemClock` defaults to
    `TimeProvider.System`. `UuidV7IdGenerator` uses `Guid.CreateVersion7()` only.
11. `SmartEnum` lookups use a list built at type initialization (no reflection); the base forces `TEnum`'s cctor once via
    `RuntimeHelpers.RunClassConstructor`. `TValue` is constrained to `IEquatable<TValue>` alone — widening it breaks
    `Guard.Against.InvalidSmartEnum` in `Core`; search generic forwarders before changing a constraint.
12. JSON code uses `JsonTypeInfo<T>` overloads via `options.GetTypeInfo(Type)`; no `JsonConverterFactory`.
    `[DebuggerDisplay]` members read backing fields, never throwing properties.
13. `Execution`, `Primitives` and `Core` are reflection-free; keep them so where it costs nothing (AOT preferred, not mandated).

**Registries**

14. `LoggingEventIdRanges` fields are `const int` = folder number × 1000. Add one field per new domain; never renumber.
    It reserves only the 1000-wide block; sub-blocks are each domain's documentation.
15. `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys` are `const string` and are wire formats — changing a
    value is a breaking cross-domain change. `WellKnownBaggageKeys.TenantId` is `"TenantId"` on purpose (it is the
    emitted log property name via `13.ServiceDefaults`' `BaggageLogRecordProcessor`). A baggage key belongs here only if
    platform middleware writes and replaces it. These constants never move to `04.Contracts`.

**Execution**

16. Every inbound adapter opens exactly one `RequestContextScope`; a tenant refinement opens an inner scope with
    `WithTenant`. Outbound adapters read `IRequestContextAccessor` and use `RequestContextPropagation` — never a second
    header mapping. No `01.Core` package reads caller identity from `Activity` baggage.
17. `PropagatedRequestContext.HasPermissionAsync` never grants (a header cannot). `SystemRequestContext` takes an
    explicit permission set — never "all permissions".
18. `TenantId` never holds `Guid.Empty` (constructor throws); "no tenant" is `null` and consumers fail closed. Its
    string form (`"D"`) is embedded in stored formats (RLS setting, key ids, cache keys, idempotency keys) — never change it.

**Core**

19. Base exceptions always carry an `Error`; no string-only constructors.
20. `ResultTry` never rethrows except `OperationCanceledException` (every catch has `when (exception is not OperationCanceledException)`).
    Its default mapping uses a fixed message — never the exception's type/message; record the exception on
    `Activity.Current` instead. Flatten `AggregateException`. Caller-supplied `onException` mappers are not altered.
21. `ResultCombine.Combine` evaluates every input and returns every failing `Error`, never short-circuits.
22. Async railway extensions avoid `async`/`await` where they only await the input (`ResultTry.TryAsync` is the exception).
23. Guards return `Error?` (null = passed, never `Error.None`); `Guard.Throw.*` are thin wrappers over `Against.*`.
    `IGuardClause` is a memberless marker; the implementation stays private. Collection guards enumerate once. The
    `InvalidFormat`/`Email` regex cache is bounded (256 patterns, approximately FIFO) with a match timeout.

**Configuration**

24. `AddValidatedOptions` always calls `ValidateOnStart` (fails fast only under a real `IHost`). Validators register via
    `TryAddEnumerable`; DataAnnotations register as a pre-built `DataAnnotationValidateOptions<T>` instance with a
    per-name duplicate check — not `.ValidateDataAnnotations()` (duplicates failures) and not name-blind `TryAddEnumerable`.
25. Public overloads carry `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` and `[DynamicallyAccessedMembers]` on
    type parameters — declare, never suppress. `aot` must not appear in this package's `PackageTags`.

**FeatureManagement**

26. Services evaluate flags only through `IFeatureClient` with a declared `FeatureFlag<T>` — never `IFeatureManager`/
    `IVariantFeatureManager` or OpenFeature's `Api.Instance` (the package uses an isolated `Api`; SK0002 flags both).
27. Pass `configuration` to `AddFeatureManagement` unmodified — never `GetSection("FeatureManagement")`.
28. The provider never throws for a flag problem (`FlagNotFound`, `TypeMismatch`/`ParseError`, else `General` with the
    default, logged once at EventId 1301). Returned messages never include exception text or configuration values;
    telemetry never carries targeting key, user id or tenant id (Microsoft's own evaluation event stays suppressed).

**Cryptography**

29. Never `System.Random`/`Guid.NewGuid()` for secrets (`ISecureRandomGenerator`); never compare secrets with `==`/
    `SequenceEqual` (`FixedTimeComparison`). No raw SHA/MD5 for secret hashing (`IOneWayHasher`); `IContentHasher` is for
    non-secret fingerprints only. `IOneWayHasher` keeps its secret-agnostic vocabulary.
30. One-way hashes are PHC strings; every `IOneWayHashAlgorithm.Verify` bounds attacker-controlled costs before deriving;
    `OneWayHasher` reserves PHC parameter `k` for the pepper id. Minimum costs apply to new hashes only.
31. Symmetric encryption is AES-256-GCM only (key exactly 32 bytes); `associatedData` is required with no default;
    decryption returns `Result` (malformed/unauthenticated = Validation, unknown key = Unexpected) and never echoes key ids.
32. Sync and async are separate interfaces (`ISynchronousSymmetricEncryptionService` over
    `ISynchronousEncryptionKeyProvider`, which never does I/O). No sync-over-async bridges or runtime capability gates.
33. Stored formats carry a version byte or PHC id (`EncryptedPayload` 0x01, `EnvelopePayload` 0x02); use their
    `ToBytes`/`ToString`/`TryParse`. Key ids from payloads are untrusted: `CachedEncryptionKeyProvider` never caches
    misses and is bounded (`maxEntries`); the Azure provider validates id shape and rate-limits forced refreshes.
34. Single-flight caching uses the internal `SingleFlightCache` (linked as source into `.KeyVault.Azure`) — never a copy.
35. Signing keys carry their algorithm (RSA ≥ 2048; ECDSA algorithm follows the curve); unknown key or malformed
    signature → `false`. Envelope unwrap accepts only configured master keys.
36. `AddSharedKernelCryptography` registers no key-dependent service; those are builder opt-ins.
37. TOTP: stateless generators; replay protection via the consumer's `ITotpReplayGuard` (`TryAcceptTimeStepAsync`,
    atomic); time from `IClock`; secrets ≥ 16 bytes; 6–8 digits; `ITotpAttemptThrottle` stays a caller-composed seam.

**Compression**

38. The frame (13-byte header, `CompressionAlgorithm` values) is a wire format: never renumber or resize; a change is a
    new frame version and readers keep every old version. Unknown version → `compression.malformed_payload`.
39. Decompression returns `Result` (catch `InvalidDataException` and `InvalidOperationException` — Brotli throws the
    latter), is bounded by bytes actually produced (`MaxDecompressedSize`), and never pre-allocates from a payload's claimed length.
40. Both compressors are sealed forwarders to the internal `CompressionCodec` — no shared public base. Raw mode cannot
    detect truncation; framed mode must. Compress before encrypting, never the reverse.

**Validation, DataPrivacy, Localization**

41. Identifiers are created only through `Create` (never throws for input), normalize, compare by `Value`, implement
    `IParsable<T>` + `ValidatedValueJsonConverter<T>`; no public constructors or string conversions.
42. One error code per failure, one `ValidationMessages` entry per code, with a Turkish line in `Localization/tr.json`.
    No message or argument contains the rejected value; FluentValidation rules never set `AttemptedValue`/`PropertyValue`.
43. `CardNumber.ToString()`/`NationalId.ToString()` are masked and must equal `PiiMasking`'s output (a test compares them).
    Currency data must stay identical to `SharedKernel.Domain`'s `CurrencyCatalog`.
44. Data classification is Microsoft's compliance model, read by the logging source generator at compile time — never
    reflectively. A classification's `TaxonomyName`/`Value` is never renamed. `PiiMasking` rules may only reveal less.
    `SetPrivacyRedactors` never sets the fallback redactor; `OnlineIdentifier` is tokenized only with a supplied `Pseudonymizer`.
45. `IDataSubjectRequestHandler`: unknown subject = success with nothing in it; repeated `RequestId` returns the first
    outcome; legally kept data is `Retained`, not a failure. No implementation ships.
46. Translation APIs return the translation or the original text, never blank or an unfilled placeholder, and never throw
    for a missing translation. Use `TryFormat` on error paths. Placeholders are named only (`{0}` rejected);
    `LocalizedMessage.Define` takes argument names explicitly and validates them at definition.
47. The catalog is immutable, built and validated at registration; exactly one `ILocalizationCatalog` (a second registration throws).

## Decisions

| Decision | Why |
|---|---|
| `ErrorCodes` are nested static string constants, not enums | Consumers add local constants without forking the kernel |
| `ValidationResult` separate from `Result<T>` | Multi-error input validation vs single-error operation outcome |
| Guards merged into `SharedKernel.Core` (namespace `SharedKernel.Guards`) | One package, no extra dependency; no separate Guards package |
| `IIdGenerator` has no `AddX()` extension | A one-line consumer registration; the generator is opt-in, never auto-replacing `Guid.NewGuid()` |
| `SharedKernel.Configuration` is deliberately not AOT-clean | Library-generic `Bind<TOptions>` cannot use the configuration-binding source generator; the requirement is declared, not hidden |
| Feature flags on OpenFeature over `Microsoft.FeatureManagement` | Vendor-neutral API, Microsoft's `feature_management` schema for targeting/variants |
| Compression is one package with keyed algorithms, not `.Brotli`/`.GZip` siblings | Small, closed, BCL-only algorithm set |
| No generic `Error` metadata bag | Declined: `Error` is a value-equal record; field details go in `Details`, placeholders in `MessageArguments` |

## Logging

Block `1000`–`1999` (`LoggingEventIdRanges.Core`); sub-blocks are `LoggingEventIdRanges.PackageSubBlockWidth` (100) wide.

| Sub-block | Package | Current EventIds |
|---|---|---|
| `1300`–`1399` | `SharedKernel.FeatureManagement` | `1301` evaluation failed (`FeatureManagementEventIds.EvaluationFailed`, Warning, logged once per failure) |

No other `01.Core` package logs today. A package that starts logging takes the next free 100-wide sub-block, declares it
in an internal `…EventIds` class derived from `LoggingEventIdRanges.Core`, and records it here.

## Cross-Domain Couplings

- **Everything references `Primitives`**; `Execution` is the ambient caller for every adapter (`13` HTTP middleware and
  `AddSharedKernelRequestContext`, `07` consume filter, `17` activity interceptor, `19` job runner, `11`/`15` outbound propagation).
- `IUnitOfWork`/`IAuditTrailWriter` are implemented by `06.Persistence` (`EfCore`, `EfCore.Auditing`) and consumed by
  `05.Application`'s transaction/auditing behaviors — never redeclared elsewhere.
- `IReadinessProbe` is implemented by every provider with an external dependency; `13.ServiceDefaults` maps probes to health checks.
- `WellKnown*` constants are consumed by `11`, `07`, `13`, `14`, `15`, `17`; `BaggageLogRecordProcessor` (13) pins the two baggage keys.
- `ErrorType` → HTTP/gRPC status maps live in `14.Presentation`; `ErrorCodes.Idempotency` is used by `05` and `14`.
- `Validation.FluentValidation` references `05.Application`'s `SharedKernel.Application` (`IRequestValidator<T>`).
- `Validation`'s currency list must match `03.Domain`'s `CurrencyCatalog`; `DataPrivacy` masks must match `Validation`'s.
- `Cryptography` is used by `06` (field encryption, audit HMAC), `07` (payload transform), `12` (TOTP), `15` (webhook signing), `17` (payload codec).
- `00.Governance` analyzers enforce this domain's rules at call sites (SK0001 time, SK0002 feature flags, SK0022 magic strings, SK0030 discarded `Result`).

## Testing

- Every package has a nested `*.Tests` project in the Unit lane (`Platform.SharedKernel.Unit.slnf`); none needs Docker.
- `SharedKernel.Consumer.Tests` restores the packed packages and runs in CI's `packaging-verify` job (see `eng/README.md`).
- Cross-transport propagation is proven end to end by `13.ServiceDefaults.Security`'s `EndToEndPropagationTests`.
- Standing rules: pin every `LoggingEventIdRanges`/`WellKnown*` value literally; test guards' `Against.*` and
  `Throw.*` paths separately with boundary theories; assert options failures at `IHost.StartAsync()`; use independently
  published vectors (RFC, SWIFT IBANs, python-stdnum VAT numbers) so tests are not circular; `.KeyVault.Azure` tests use
  SDK client subclasses (no network); README samples are compiled or run by tests.
- Consumers' fakes: `SharedKernel.Testing` (`FakeClock`, `TestRequestContext`), `SharedKernel.Cryptography.Testing`
  (`AddFakeCryptography()`), `SharedKernel.FeatureManagement.Testing` (`FakeFeatureClient`).

## Known Limitations

- `SharedKernel.Configuration` binding is reflective: options with complex nested members are not trim-safe; keep
  options flat or bind by hand where native AOT is required.
- `SharedKernel.FeatureManagement` is not trim/AOT-safe (`Microsoft.FeatureManagement` binds filter parameters by
  reflection); re-check on each upgrade. FluentValidation's and the Azure SDK's AOT status is likewise unverified.
- Raw compression modes cannot detect truncation; framed stream compression with both input and output non-seekable
  cannot record the length up front (documented in the Compression README).
- VAT numbers issued to individuals are format-only where the check digit depends on personal data (BG 10-digit,
  CZ 9/10-digit, LV personal codes).
- RSA-wrapped envelopes are confidentiality-only against public-key holders.
