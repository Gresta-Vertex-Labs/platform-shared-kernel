---
name: provider-agnostic-adapter-pattern
description: When wiring a health check for a domain with multiple swappable providers behind one abstraction, take required config as an explicit parameter — never reach into a concrete provider's options type
metadata:
  type: project
---

> WO-086 (2026-09): `AddStorageReadinessCheck` and `SharedKernel.ServiceDefaults.Storage` were deleted; each named store registers its own `IReadinessProbe` (`storage-{store}`) mapped by `AddSharedKernelReadiness()`. The provider-agnostic lesson still applies to any future integration.

Decided while designing `AddStorageReadinessCheck` (WO-043/P-270, 2026-07-16) for `08.Storage`, which
ships `IFileStorage` behind two sibling, mutually-non-referencing providers (`SharedKernel.Storage.S3`,
`SharedKernel.Storage.Obs`) — a different shape than `06.Persistence` (one `SharedKernelDbContext`
generic parameter) or `02.Caching`/`07.Messaging` (single active provider per service, connection
details read from that provider's already-resolved options object, per the existing Implementation
Rules bullet: "Messaging and cache health checks must read their connection details from the
already-resolved options objects... never require the caller to duplicate a connection string").

**The distinction that matters:** that existing rule assumes there is exactly one concrete options
type to read from for a given service. `08.Storage` breaks that assumption — a health check adapter
for `IFileStorage` must work whether the caller registered `.S3` or `.Obs`, and `S3StorageOptions`/
`ObsStorageOptions` are deliberately independent, non-shared types (sibling-package rule, `08.Storage`
brain). Reaching into either one from `13.ServiceDefaults` to read a default bucket would require a
branch ("if S3 registered, read S3StorageOptions.DefaultBucket, else ObsStorageOptions.DefaultBucket")
— exactly the provider-specific branching the P-270 acceptance criteria forbid, and a violation of
"13.ServiceDefaults only wires already-existing abstractions, no provider-specific knowledge."

**Resolution:** `AddStorageReadinessCheck(this IHealthChecksBuilder, string bucket, ...)` takes `bucket`
as a required explicit parameter at the call site, exactly the way `AddRedisHealthCheck` takes an
explicit `connectionString` rather than reaching into a Redis-specific options type it doesn't own.
The caller (a consuming microservice's `Program.cs`) already knows its own bucket name from its own
configuration — supplying it explicitly costs nothing and keeps the adapter genuinely provider-agnostic.

**How to apply to future domains:** when a health check adapter targets an abstraction with more than
one sibling, non-shared concrete provider (any domain following the `.{Provider}` sibling-package split
— currently `08.Storage`; potentially a future `09.Search`/`10.Intelligence` readiness check), default to
"take the minimal identifying parameter explicitly" rather than "read it from a resolved options
object" — the options-object shortcut only holds when there is exactly one provider type in play for a
given service (as in `02.Caching`/`07.Messaging` today).

See [[health_check_tag_calibration]] for where this check's `Unhealthy` (not `Degraded`) calibration
sits in the domain's tag/status table, and [[feedback_verify_dependency_claims]] for why this whole
phase's Core/Tests/Docs tasks are recorded as `⚑` Blocked rather than implemented immediately.
