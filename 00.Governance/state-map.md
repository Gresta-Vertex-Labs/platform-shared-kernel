# 00.Governance — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Analyzers` | Tooling | ● | Roslyn rules (`SK0001`…): `[LoggerMessage]`-only logging, `Result` never discarded, magic strings, raw SDK clients, workflow determinism, pipeline-marker response shape, parameterized Dapper SQL, … Targets `netstandard2.0`. |
| `SharedKernel.ArchitectureTests` | Tooling | ● | NetArchTest purity rules the tiers cannot express, `DependencyGraphRulesTests` over every csproj, `RuleExecutionCoverageTests` (every public rule needs a test). |
| `SharedKernel.Linter` | Tooling | ● | Content-only package: EditorConfig + CSharpier enforcement. |
| `SharedKernel.Benchmarks` | — (not packable) | ● | Dev-only BenchmarkDotNet harness; never published. |

The tier check itself is MSBuild (`eng/SharedKernelTiers.targets`, `SKTIER000`–`SKTIER006`), owned by `PLATFORM.md`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.00.Design`, `SK.00.Scaffold`, `SK.00.Core`, `SK.00.Tests`, `SK.00.Docs`, `SK.00.Published` | Initial build — analyzers, architecture tests, benchmarks, linter | ● |
| `SK.00.GuardPurity`, `SK.00.CachingEnforcement`, `SK.00.DomainLayerPurity`, `SK.00.DomainGoldStandard`, `SK.00.ContractsPurity` | Early purity rules (Guards, Caching, Domain, Contracts) | ● |
| `SK.00.PersistenceEnforcement`, `SK.00.PersistenceEnforcement2`, `SK.00.PersistenceContractCompleteness`, `SK.00.EfCorePackageHygiene`, `SK.00.TenantedDbContextGuard`, `SK.00.EncryptionPatternGuard`, `SK.00.EfPropertyUsageGuard` | Persistence rules | ● |
| `SK.00.MessagingArchRules`, `SK.00.ExtendedMessagingArchRules`, `SK.00.EventEnvelopeConstructionGuard` | Messaging and envelope rules | ● |
| `SK.00.RedisTopology`, `SK.00.StorageTopology`, `SK.00.SearchTopology`, `SK.00.IntelligenceTopology`, `SK.00.WorkflowTopology` | Package-topology rules (numbered-layer parts replaced by the WO-086 tier check) | ● |
| `SK.00.ReflectionGuard`, `SK.00.DomainEventDispatcherReflectionExemption` | Reflection guard and its exemption registry | ● |
| `SK.00.CommunicationArchRules`, `SK.00.WO026CommunicationQuality` | Communication rules | ● |
| `SK.00.ServiceDefaultsGovernance`, `SK.00.HealthCheckConstantsGuard`, `SK.00.ServiceDefaultsSplitRepoint` | ServiceDefaults rules (layering grants deleted by WO-086 P-569) | ● |
| `SK.00.PresentationArchRules`, `SK.00.CorsWildcardCredentialsGuard`, `SK.00.GrpcErrorMappingGuard` | Presentation rules | ● |
| `SK.00.ApplicationPipelineArchRules`, `SK.00.MetricsOutcomeTagAndMisregistrationGuard`, `SK.00.MarkerInterfaceMisuseGuard`, `SK.00.PipelineMarkerResponseShapeGuard` | Application pipeline rules | ● |
| `SK.00.CryptoDelegationAndUowSeamGuard`, `SK.00.SyncCryptoGateAndArgon2ConfinementLock`, `SK.00.CoreDiRegistrationConventionLock` | Cryptography and 01.Core convention locks | ● |
| `SK.00.LoggingStandardEnforcement`, `SK.00.MagicStringGuard`, `SK.00.ResultDiscardGuard`, `SK.00.DataPrivacyLoggingGuard` | Platform conventions (logging, magic strings, discarded `Result`, classified data in logs) | ● |
| `SK.00.SecurityContextGuard`, `SK.00.SenderConstrainedCredentialGuard`, `SK.00.SecureDefaultsLock`, `SK.00.TenantAndMtlsBoundaryLock`, `SK.00.CorrelationIdValidationGuard`, `SK.00.WebhookSsrfGuardLock`, `SK.00.CacheEncryptionAndRedisValidationLock` | Secure-default locks | ● |
| `SK.00.MapperEnforcement`, `SK.00.MoneyCurrencyAdvisory`, `SK.00.DomainFactoryAndValueObjectValidationGuard` | Mapperly endorsement, `Money` advisory, aggregate factory / `EnsureValid` | ● |

## Open Work

None — every phase in this domain is complete. `SharedKernel.Analyzers`, `.ArchitectureTests` and `.Linter` ship with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● Foundation refactor — tier check replaces the numbered-layer rules, `DependencyGraphRulesTests` and meta-tests added, tier baseline emptied and `SKTIER` made an error, SK0015 removed (P-563, P-574, P-575) (2026-09-26)
- P-544 ● `SK.00.PipelineMarkerResponseShapeGuard` — SK0040 (2026-09-15)
- P-542 ● `SK.00.DomainFactoryAndValueObjectValidationGuard` — aggregate factories and SK0037 `EnsureValid` (2026-09-15)
- P-535 ● `SK.00.ServiceDefaultsSplitRepoint` (WO-084) (2026-09-14)
- P-523 ● `SK.00.CoreDiRegistrationConventionLock` (WO-083) (2026-09-09)
- P-508 ● Re-point architecture/analyzer tests from `SharedKernel.Guards` to `SharedKernel.Core` (WO-082) (2026-09-10)
- P-504 ● `SK.00.SyncCryptoGateAndArgon2ConfinementLock` (WO-081) (2026-09-08)
- P-489, P-490 ● Cache-invalidation ordering lock; ServiceDefaults → Workflows readiness grant lock (WO-080)
- P-486 ● `SK.00.MapperEnforcement` (WO-079)
- P-476 ● `SK.00.DataPrivacyLoggingGuard` (WO-076)
- P-469 ● `SK.00.GrpcErrorMappingGuard` (WO-074)
- P-442 ● `SK.00.MoneyCurrencyAdvisory` (WO-066)
- P-437 ● `SK.00.CacheEncryptionAndRedisValidationLock` (WO-065)
- P-432 ● `SK.00.WebhookSsrfGuardLock` (WO-064)
- P-420 ● `SK.00.CorrelationIdValidationGuard` (WO-063)
- P-410 ● `SK.00.CorsWildcardCredentialsGuard` (WO-062)
- P-401 ● `SK.00.TenantAndMtlsBoundaryLock` (WO-061)
- P-390 ● `SK.00.SecureDefaultsLock` (WO-060)
- P-383 ● `SK.00.SenderConstrainedCredentialGuard` (WO-058)
- P-373 ● `SK.00.SecurityContextGuard` (WO-057)
- P-350 ● `SK.00.EventEnvelopeConstructionGuard` (WO-054)
- P-327 ● `SK.00.EfPropertyUsageGuard` (WO-051)
- P-299 ● `SK.00.ResultDiscardGuard` (WO-049)
- P-271, P-278, P-286, P-290 ● Storage, Search, Intelligence and Workflows topology rules (WO-043–WO-046)
- P-264 ● `SK.00.MagicStringGuard` (WO-042)
- P-250 ● `SK.00.LoggingStandardEnforcement` (WO-041)
- P-225–P-248 ● Application pipeline, crypto-delegation, metrics, reflection-exemption and marker-misuse rules (WO-036–WO-040)
- P-199 ● `SK.00.PresentationArchRules` (WO-031)
- P-173, P-178 ● ServiceDefaults governance; health-check constants guard (WO-027, WO-028)
- Earlier phases (P-004, P-009, P-034, P-056, P-063, P-075, P-083, P-096, P-103, P-110, P-114, P-123, P-133, P-145, P-153, P-159, P-167) — archived.

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] WO-086 recorded (P-563, P-574, P-575): tier check replaces numbered-layer rules, meta-tests added, SK0015 removed, CLAUDE.md cut to the final state, READMEs updated
- [2026-09-18] P-554 — SK0035 retargeted to Microsoft's `DataClassificationAttribute` model (classified `[LoggerMessage]` parameters are safe under redaction)
- [2026-09-15] `SK.00.PipelineMarkerResponseShapeGuard` opened and closed (13/13, root P-544) — SK0040
- [2026-09-15] `SharedKernel.Analyzers` (SK0037–SK0039) and `SharedKernel.ArchitectureTests` republished as `1.0.0-alpha.0.935` from `9f3ee5f`
