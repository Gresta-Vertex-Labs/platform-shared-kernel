# 01.Core — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

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

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None. (The two `⚑` rows once recorded for P-495 S-25 and P-496 P-42 — `.slnx` registration and a `Directory.Packages.props` pin — were resolved by the coordinator on 2026-09-08.)

## Cross-Domain Dependencies

None open.
