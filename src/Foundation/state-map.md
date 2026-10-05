# 01.Core — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Primitives` | Foundation | ● | `Result<T>`, `Error`, `IClock`, `IIdGenerator`, SmartEnums, `LoggingEventIdRanges`, `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys`, `SharedKernel.Primitives.Health` (`IReadinessProbe`). |
| `SharedKernel.Execution` | Foundation | ● | The one execution context: `IRequestContext`, `ActorKind`, `IRequestContextAccessor` + `RequestContextScope`, `RequestContextPropagation`, `CorrelationIds`, `TenantId`/`TenantScope`, `IUnitOfWork`, `IAuditTrailWriter` (WO-086). |
| `SharedKernel.Core` | Foundation | ● | Railway extensions, `ResultTry`/`ResultCombine`, base exceptions, guard clauses (absorbed `SharedKernel.Guards`, P-505). |
| `SharedKernel.Configuration` | Foundation | ● | `AddValidatedOptions`, `ISectionBoundOptions`, strictness options. |
| `SharedKernel.FeatureManagement` | Foundation | ● | OpenFeature `IFeatureClient`, typed `FeatureFlag<T>`, tenant targeting. |
| `SharedKernel.Compression` | Foundation | ● | Framed Brotli/gzip `IPayloadCompressor` with decompression cap. |
| `SharedKernel.Validation` | Foundation | ● | IBAN, BIC, card, ISO codes, phone, VAT, LEI, ABA, SEPA creditor id, national id value types. |
| `SharedKernel.Validation.FluentValidation` | Adapter | ● | FluentValidation rules and `AddFluentValidationRequestValidators`. |
| `SharedKernel.DataPrivacy` | Foundation | ● | 23-kind personal-data taxonomy, log redactors, masking, pseudonymizer, data-subject requests. |
| `SharedKernel.Localization` | Foundation | ● | `LocalizedMessage` definitions and catalogs; localized `Error`/`ProblemDetails`. |
| `SharedKernel.Cryptography` | Foundation | ● | Hashing, AES-256-GCM with required AAD, envelope encryption, signatures, HMAC, secure random, TOTP/HOTP, key providers and rotation. |
| `SharedKernel.Cryptography.Argon2` | Adapter | ● | `.AddArgon2id(configuration)` one-way hash algorithm. |
| `SharedKernel.Cryptography.KeyVault.Azure` | Adapter | ● | Azure Key Vault encryption and signing key providers; `encryption-key-provider` readiness probe. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.01.Design`, `SK.01.Scaffold`, `SK.01.Core`, `SK.01.Tests`, `SK.01.Docs`, `SK.01.Published` | Initial build (WO-001, WO-002) | ● |
| `SK.01.P042` | `Error.BusinessRule` factory | ● |
| `SK.01.WO033Impl` | WO-033 cryptography implementation (P-207–P-209) | ● |
| `SK.01.WO034` | WO-034 `IOneWayHasher` rename (P-210–P-213) | ● |
| `SK.01.P230`, `SK.01.P236` | Application seams; reflection-free failure factory | ● |
| `SK.01.P249`, `SK.01.P259` | EventId range registry; well-known propagation constants | ● |
| `SK.01.P292`, `SK.01.P293`, `SK.01.P294`, `SK.01.P295`, `SK.01.P296`, `SK.01.P297`, `SK.01.P298` | WO-049 — result boundaries, id generator, tag keys, `TimeProvider` clock, content hashing, Compression package, flag variants | ● |
| `SK.01.P384` | `ErrorType.Forbidden` + `Error.Forbidden` | ● |
| `SK.01.P443`, `SK.01.P444` | Validation and Validation.FluentValidation packages | ● |
| `SK.01.P446`, `SK.01.P447`, `SK.01.P451` | Async key provider + envelope encryption; Key Vault package; TOTP/HOTP | ● |
| `SK.01.P474`, `SK.01.P482` | DataPrivacy and Localization packages | ● |
| `SK.01.LoggingRangesNewDomains` | EventId bases for Idempotency/Scheduling/Reporting | ● |
| `SK.01.P487` | Key Vault readiness-probe primitive | ● |
| `SK.01.P491`, `SK.01.P492`, `SK.01.P493`, `SK.01.P494`, `SK.01.P495`, `SK.01.P496` | WO-081 cryptography redesign (AAD, sync gate, async signing, Key Vault signing, Argon2, Key Vault hardening) | ● |
| `SK.01.P505`, `SK.01.P506`, `SK.01.P507` | WO-082 Guards → Core merge | ● |
| `SK.01.P510`, `SK.01.P511`, `SK.01.P512`, `SK.01.P513`, `SK.01.P514`, `SK.01.P515`, `SK.01.P516`, `SK.01.P517`, `SK.01.P518`, `SK.01.P519`, `SK.01.P520`, `SK.01.P521`, `SK.01.P522`, `SK.01.P524`, `SK.01.P525`, `SK.01.P526` | WO-083 gold-standard audit fixes | ● |
| `SK.01.P529`, `SK.01.P530`, `SK.01.P538`, `SK.01.P539` | Primitives, Configuration and Core pre-publish passes | ● |
| `SK.01.P545` | Cryptography, Argon2, KeyVault.Azure pre-first-publish redesign (published `1.0.0-alpha.0.998`) | ● |
| `SK.01.P551`, `SK.01.P552`, `SK.01.P553`, `SK.01.P554`, `SK.01.P555` | Compression, Localization, Validation, DataPrivacy, FeatureManagement pre-first-publish passes | ● |
| `SK.01.WO086` | WO-086 foundation refactor (01.Core share) | ● |

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None. (The two `⚑` rows once recorded for P-495 S-25 and P-496 P-42 — `.slnx` registration and a `Directory.Packages.props` pin — were resolved by the coordinator on 2026-09-08.)

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● Foundation refactor — `SharedKernel.Execution` package, `Primitives.Health` readiness contract, tiers, FluentValidation request-validator bridge (P-564–P-569, P-575) (2026-09-26)
- P-555 ● `SharedKernel.FeatureManagement` pre-first-publish pass on OpenFeature — published `1.0.0-alpha.0.1112` (2026-09-18)
- P-554 ● `SharedKernel.DataPrivacy` pre-first-publish pass — published `1.0.0-alpha.0.1106` (2026-09-18)
- P-553 ● `SharedKernel.Validation` + `.FluentValidation` pre-first-publish pass — published `1.0.0-alpha.0.1100` (2026-09-18)
- P-552 ● `SharedKernel.Localization` pre-first-publish pass — published `1.0.0-alpha.0.1093` (2026-09-18)
- P-551 ● `SharedKernel.Compression` pre-first-publish pass — published `1.0.0-alpha.0.1088` (2026-09-18)
- P-545 ● Cryptography, Argon2, KeyVault.Azure pre-first-publish redesign — published `1.0.0-alpha.0.998` (2026-09-16)
- P-538, P-539 ● `SharedKernel.Core` hardening; `SharedKernel.Configuration` first-publish bar (user-directed)
- P-529, P-530 ● `SharedKernel.Primitives` and `SharedKernel.Configuration` pre-publish hardening (user-directed)
- P-510–P-526 ● WO-083 — `ResultTry` redaction, cancellation leaks, PBKDF2 floor, key-length validation, atomic TOTP replay guard, SmartEnum init, freezable catalog, `TryAdd` idiom, source-generated options validation, reference tables, bounded regex cache, key zeroing, LEI/ABA/SEPA validators, FIPS posture (2026-09-09)
- P-505–P-507 ● WO-082 — `SharedKernel.Guards` merged into `SharedKernel.Core` (2026-09-10)
- P-491–P-496 ● WO-081 — cryptography AAD, sync-provider gate, async signing, Key Vault signing, Argon2 package, Key Vault hardening (2026-09-08)
- P-487 ● Key Vault readiness-probe primitive (WO-080) (2026-09-04)
- P-482 ● `SharedKernel.Localization` package (WO-078)
- P-474 ● `SharedKernel.DataPrivacy` package (WO-076)
- P-451 ● TOTP/HOTP generation, verification and replay guard (WO-069)
- P-446, P-447 ● Async key provider + envelope encryption; `SharedKernel.Cryptography.KeyVault.Azure` (WO-068)
- P-443, P-444 ● `SharedKernel.Validation` and `.FluentValidation` packages (WO-067)
- P-384 ● `ErrorType.Forbidden` + `Error.Forbidden` (WO-059) (2026-08-14)
- P-292–P-298 ● WO-049 — result boundaries, id generator, tag keys, `TimeProvider` clock, content hashing, Compression package, flag variants
- Earlier phases (P-001–P-003, P-042, P-205–P-213, P-230, P-236, P-249, P-259) — archived.

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] WO-086 recorded (`SK.01.WO086`): Execution package, Primitives.Health readiness contract, tiers, FluentValidation request-validator bridge; CLAUDE.md and every package README rewritten
- [2026-09-18] SK.01.P555 published — `SharedKernel.FeatureManagement` `1.0.0-alpha.0.1112`; every `01.Core` package now published
- [2026-09-18] SK.01.P555 complete — OpenFeature `IFeatureClient` with typed `FeatureFlag<T>`, explicit targeting, startup validation
- [2026-09-18] SK.01.P554 published — `SharedKernel.DataPrivacy` `1.0.0-alpha.0.1106`
