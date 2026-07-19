---
name: testing-fixture-blocker
description: 16.Testing has no Meilisearch or Elasticsearch Testcontainers fixture — the one real inbound blocker for 09.Search's Tests phase
metadata:
  type: project
---

Verified on disk 2026-07-19: `16.Testing/SharedKernel.Testing/Containers/` contains exactly four
fixtures (PostgreSQL, Redis, RabbitMQ, MinIO). Zero search-related rows exist anywhere in
`16.Testing/state-map.md`, `CLAUDE.md`, or `README.md` — this is a true greenfield gap, not a
"planned but not yet built" situation.

**Two distinct sub-problems:**
1. Meilisearch — no `Testcontainers.Meilisearch` NuGet package exists at all (nuget.org 404,
   Meilisearch absent from the official Testcontainers-for-.NET module list). Must be hand-rolled on
   the generic `ContainerBuilder`: image `getmeili/meilisearch`, port 7700, `MEILI_MASTER_KEY`
   (>=16 bytes), `MEILI_NO_ANALYTICS=true`, wait strategy on `GET /health` (the only route
   unprotected by the master key).
2. Elasticsearch — `Testcontainers.Elasticsearch` 4.13.0 exists but defaults to image
   `elasticsearch:8.6.1`, which is an **unsupported pairing** with the `Elastic.Clients.Elasticsearch`
   9.x client (9.x client does not support 8.x server). Must explicitly `.WithImage("...9.x.y")`, use
   `CertificateValidations.AllowAll` (module runs ES secure-by-default over HTTPS with a self-signed
   cert), and add an explicit ping-poll wait (known readiness race, testcontainers-dotnet#955).

**Version-alignment decision required before either fixture lands:** the four existing fixtures pin
`Testcontainers.*` at 4.1.0. Adding `Testcontainers.Elasticsearch` 4.13.0 lifts the transitive floor
for the whole shared `SharedKernel.Testing` package — must be an explicit decision (bump all four, or
pin Elasticsearch to a 4.1.x release), never an incidental NuGet resolution.

**Why this matters:** this is tracked as P-275 (a `16.Testing`-domain phase, not mine to implement),
but it directly blocks `09.Search`'s real-backend Tests-phase tasks (T-13 through T-17,
T-21 through T-26). The `09.Search/state-map.md` Blocked section already documents the standing
instruction from the `08.Storage` precedent: when `SK.09.Tests` is dispatched, implement every
genuinely container-free task and mark only the real-backend tasks `⚑` Blocked — never hand-roll a
competing ad-hoc container setup inside a `.Tests` project.

**How to apply:** when dispatching or implementing `09.Search`'s Tests phase, re-verify this blocker
directly on disk first (don't trust this memory as still-current) — it's exactly the kind of thing
that could have been resolved by a `16.Testing` session in between.
