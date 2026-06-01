---
name: project_outbox_scope
description: Outbox pattern is owned by 07.Messaging (MassTransit EF outbox) — no outbox types in 06.Persistence
metadata:
  type: project
---

The outbox pattern is entirely owned by `07.Messaging` via MassTransit's `UseEntityFrameworkOutbox`. No `OutboxMessage`, `IOutboxWriter`, or `OutboxInterceptor` types belong in `06.Persistence`. This was a correction made during WO-013 planning (2026-06-01): the initial CLAUDE.md incorrectly listed these types in both `.Abstractions` and `.EfCore`.

**Why:** Introducing a competing outbox contract in `06.Persistence` would create parallel infrastructure with no clear owner. MassTransit's EF outbox manages its own schema, persistence, and relay entirely within `07.Messaging` without any coordination from `06.Persistence`.

**How to apply:** When processing any persistence phase request that mentions outbox, domain events, or event dispatching — reject the addition and redirect to `07.Messaging`. `SharedKernelDbContext` registers exactly three interceptors (Audit, SoftDelete, Concurrency). The number four is always wrong for this constructor.
