---
name: project_database_readiness
description: DatabaseReadinessResult + CheckReadinessAsync are BCL-only, never-throwing probes; no IHealthCheck in 06.Persistence
metadata:
  type: project
---

P-150 introduced a sealed record `DatabaseReadinessResult(bool IsHealthy, TimeSpan
Latency, string Provider, string? ErrorMessage)` in
`SharedKernel.Persistence.Abstractions/Diagnostics/` — BCL types only.

Two probe extension methods, both NEVER throw (exceptions are caught and reported
as `IsHealthy = false` with `ErrorMessage` populated):

- `IDbConnectionFactory.CheckReadinessAsync(ct)` in `.Abstractions` — opens a
  connection, runs `SELECT 1` via `IDbCommand.ExecuteScalar`, times with
  `System.Diagnostics.Stopwatch`. `Provider` derived from the connection's runtime
  type. Only `System.Data`/`System.Diagnostics` types — zero new deps.
- `SharedKernelDbContext.CheckReadinessAsync(ct)` in `.EfCore` — uses
  `Database.CanConnectAsync` + `Stopwatch`, `Provider` from `Database.ProviderName`.

**Hard rule — no IHealthCheck in this domain:** `06.Persistence` does NOT implement
`Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck` anywhere, and does NOT
take a dependency on that package. `13.ServiceDefaults` wraps these extension
methods inside its own `IHealthCheck` adapter for ASP.NET Core health check
middleware.

**Why:** Keeps `06.Persistence` a pure data-access layer — health check wiring,
probe registration, and middleware composition are `13.ServiceDefaults`'s job
(host composition layer). This mirrors the existing "no messaging concerns in
06.Persistence" boundary.

**How to apply:** If asked to add health/readiness features to `06.Persistence`,
the answer is always "expose a BCL result type + never-throwing probe method, do
NOT implement IHealthCheck, do NOT add the HealthChecks NuGet package." Point
consumers at `13.ServiceDefaults` for the adapter.

Choose `IDbConnectionFactory.CheckReadinessAsync` for Dapper-only services with no
DbContext; choose `SharedKernelDbContext.CheckReadinessAsync` when a DbContext is
already in scope.
