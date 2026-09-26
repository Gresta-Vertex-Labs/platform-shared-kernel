---
name: project_wo043_dispatch_context
description: WO-043 cross-domain phase map (P-265–P-271) — dependency ordering and what blocks what across 08.Storage/16.Testing/13.ServiceDefaults/00.Governance
metadata:
  type: project
---

> WO-086 (2026-09): `IBlobUriGenerator` and `CheckHealthAsync` are gone (presigning moved onto `IFileStorage` in P-559); the 13.ServiceDefaults storage readiness adapter was deleted — each registered store self-registers an `IReadinessProbe` named `storage-{store}`; the ".S3/.Obs never cross-reference" rule is now the one declared adapter edge `Obs → S3`; `.Abstractions` is Abstractions tier (NuGet limited to `Microsoft.Extensions.*.Abstractions`). The phase map below is history.

WO-043 is the 08.Storage first build-out, spanning four domains, dispatched 2026-07-16. Only
P-265/P-266/P-267 are this agent's own jurisdiction; the other three are recorded here purely as
cross-domain context so future 08.Storage planning passes don't have to re-derive the dependency
graph from the root state-map.

**Phase map:**
- P-265 (08.Storage, this domain) — `IFileStorage`/`IBlobUriGenerator` Abstractions contract
  finalization: `CopyAsync`, `DeleteManyAsync`, `ListAsync` redesigned to streaming
  `IAsyncEnumerable<FileMetadata>`, `CheckHealthAsync` connectivity probe. No dependency.
- P-266 (08.Storage, this domain) — `.S3` provider implementation. Depends on P-265.
- P-267 (08.Storage, this domain) — `.Obs` provider implementation. Depends on P-265.
- P-268 (16.Testing, outside jurisdiction) — MinIO Testcontainers fixture shared by both `.S3` and
  `.Obs` provider test suites (OBS test suite stands the same MinIO container in for the real OBS
  endpoint — OBS is never available in CI). All provider round-trip Tests-phase tasks in this
  domain's own state-map are blocked on this shipping.
- P-269 (16.Testing, outside jurisdiction) — `InMemoryFileStorage`/`InMemoryBlobUriGenerator` fake,
  depends on P-265 only (the interface, not either provider).
- P-270 (13.ServiceDefaults, outside jurisdiction) — storage readiness `IHealthCheck` adapter
  wrapping `IFileStorage.CheckHealthAsync`. Depends on P-265/266/267.
- P-271 (00.Governance, outside jurisdiction) — `StorageTopologyRules`/SK0023 architecture-test
  suite mirroring `RedisTopologyRules` (P-145): Abstractions zero-3rd-party-deps, `.S3`/`.Obs`
  never cross-reference, no raw `Amazon.S3.*` outside providers, singleton `IAmazonS3`
  registration. This was DESIGNED before any `08.Storage` code existed — its own state-map flags
  itself as unverifiable against real assemblies until P-266/P-267 both ship (contrived
  `CSharpCompilation` fixtures used instead). If a future `08.Storage` phase changes package names,
  namespaces, or the `IAmazonS3`-singleton registration shape, that governance phase's design
  assumptions need re-checking too — it hard-codes `"SharedKernel.Storage.S3"`,
  `"SharedKernel.Storage.Obs"`, and the bare `"Amazon"`/`"Amazon.S3"` namespace-prefix terms.

**Why this matters for future planning:** when the next 08.Storage phase is dispatched, check
whether it's additive to P-265's locked contract (safe, no cross-domain ripple) or a further
signature change to `IFileStorage`/`IBlobUriGenerator` (ripples into P-269's fake, P-270's health
adapter, and P-271's architecture-test design assumptions — flag this in the phase's own
Cross-Domain Dependencies notes even though fixing those isn't this agent's job).
