---
name: project_wo043_storage_buildout
description: WO-043 08.Storage full build-out (Abstractions + S3 + Obs) plus cross-domain support — phase list, contract upgrades accepted, and rationale
type: project
---

WO-043 (2026-07-16, P-265–P-271) took 08.Storage from a fully-designed-but-undispatched domain brain (Abstractions/S3/Obs packages already documented in `08.Storage/CLAUDE.md` from a prior session, Package Board all `○`) to a dispatched 7-phase backlog across 4 domains.

**Phases written:**
- P-265 (08.Storage) — Abstractions contract finalization: added CopyAsync (server-side copy), batch-delete, converted `ListAsync` from `IReadOnlyList<FileMetadata>` to a streaming `IAsyncEnumerable`, added a connectivity/reachability probe. All four bundled into one phase since nothing had shipped yet — free to redesign before `.S3`/`.Obs` exist, expensive after.
- P-266 (08.Storage) — `.S3` provider implementation, depends on P-265.
- P-267 (08.Storage) — `.Obs` (Huawei Cloud OBS) provider implementation, depends on P-265. This was the user's explicit ask ("do the obs package").
- P-268 (16.Testing) — MinIO Testcontainers fixture, shared by both provider test suites (already flagged as a pending cross-domain dependency in `08.Storage/state-map.md` before this WO).
- P-269 (16.Testing) — `InMemoryFileStorage`/`InMemoryBlobUriGenerator` fake, mirroring the `InMemoryMessageBus`/`InMemoryEventPublisher` precedent — depends on P-265 only (interface, not providers).
- P-270 (13.ServiceDefaults) — storage readiness health check adapter wrapping `IFileStorage`'s new connectivity probe, mirroring the existing `06.Persistence` DB-readiness-probe / `13.ServiceDefaults` health-check split.
- P-271 (00.Governance) — NetArchTest suite mirroring `RedisTopologyRules` (P-145): Abstractions zero-3rd-party-deps, S3/Obs never cross-reference, no raw `Amazon.S3.*` outside providers, singleton client registration.

**Key architectural upgrade applied:** [[feedback_verify_shipped_code_not_docs|see general verify-first feedback]] — caught that the domain's own stated philosophy ("Stream-first, blob bytes never buffered in memory") was violated by its own already-drafted `ListAsync` signature returning a fully-materialized list. Fixed at design time (P-265) before any provider code existed, avoiding a breaking change later. This is the kind of self-consistency check to always run before accepting a pre-drafted domain design at face value.

**Scope restraint exercised:** did NOT add an Azure Blob Storage provider (mentioned only as a hypothetical example in the storage-arch-planner agent's own description, not requested by the user) and did NOT add multipart/resumable upload as an explicit contract (AWSSDK.S3's `TransferUtility` already handles multipart transparently under `UploadAsync`) or server-side-encryption options (infra/bucket-policy concern, not application contract). Keep this restraint in mind for future storage requests — gold-standard does not mean maximal.

**Root CLAUDE.md gap found and fixed:** 08.Storage had zero "What Goes Where" rows despite existing as a domain, and the Abstractions Packages table listed only `.S3` (missing `.Obs`, which was already documented in the domain's own brain). Always check root CLAUDE.md's "What Goes Where" table for the target domain before assuming it's already covered — it can silently lag a domain's own sub-brain.
