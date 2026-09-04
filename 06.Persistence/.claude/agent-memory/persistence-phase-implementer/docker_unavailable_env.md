---
name: docker-unavailable-env
description: This dev machine has no running Docker daemon — how to run 06.Persistence tests honestly
type: feedback
---

`docker info` fails on this machine (checked 2026-09-02). Several `SharedKernel.Persistence.*`
test suites are real-PostgreSQL Testcontainers integration tests (files/classes with
"Integration" in the name — `ConcurrencyIntegrationTests`, `KeysetPaginationIntegrationTests`,
`PostgreSQLIntegrationTests`, `TransientFaultRetryIntegrationTests`,
`DapperReadServiceIntegrationTests`, `CommandTimeoutIntegrationTests`,
`ReadReplicaRoutingIntegrationTests`, `VectorNearestNeighborIntegrationTests`, etc.) and will
hang or fail without a real daemon.

**Why:** Docker daemon availability is an environment property, not a code property — it varies
session to session and must be checked, never assumed either way.

**How to apply:**
1. Check first with `docker info >/dev/null 2>&1 && echo OK || echo UNAVAILABLE` before running
   any `06.Persistence`/`08.Storage`/`09.Search` etc. test suite that might contain Testcontainers
   tests.
2. If unavailable, run only the unit-test subset: `dotnet test <csproj> --filter
   "FullyQualifiedName!~Integration"` (the EfCore.Tests project's naming convention puts
   "Integration" in every Testcontainers-backed class name — verify this holds before trusting
   the filter on a new project).
3. State explicitly in the final report that Docker was unavailable and integration suites were
   NOT run — never claim they passed, and never silently skip mentioning them.
