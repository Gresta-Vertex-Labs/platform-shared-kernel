---
name: project_data_seeder_migration_hosted_service
description: IDataSeeder<TContext> + MigrationAndSeedHostedService opt-in pattern, PostgreSQL advisory lock (no 02.Caching ref), idempotency-as-contract
metadata:
  type: project
---

P-151 introduced `IDataSeeder<TContext> where TContext : DbContext` with a single
`SeedAsync(TContext context, CancellationToken ct) → Task`. Idempotency is a
**contract, not enforced** — `SeedAsync` may run on every startup; implementations
must check for existing data or use upserts.

`EfCorePersistenceBuilder<TContext>` gains two opt-in fluent methods:
- `.WithMigrationsOnStartup()` — marks that `Database.MigrateAsync` should run at
  startup.
- `.AddSeeder<TSeeder>() where TSeeder : class, IDataSeeder<TContext>` — registers
  scoped, accumulates an ordered list, executed in call order.

`.Build()` registers `MigrationAndSeedHostedService<TContext> : IHostedService`
**only if** either of the above was called. If neither was called, zero overhead —
no hosted service registered.

**StartAsync sequence:**
1. Acquire PostgreSQL advisory lock: `pg_advisory_lock(hashtext(lockKey))` via the
   EXISTING `IDbConnectionFactory` — lock key derived from the context type's full
   name, so multiple replicas racing on startup serialize to one instance.
2. If migrations-on-startup requested: `Database.MigrateAsync(ct)`. Compiled models
   (P-106 `.WithCompiledModel()`) do not change this — `MigrateAsync` applies SQL
   DDL independently of the runtime model.
3. Run each registered seeder in order, each in its own DI scope + own `TContext`
   instance via `IDbContextFactory<TContext>` (seeder registration implicitly
   requires `.WithDbContextFactory()` — `.Build()` enables it automatically if a
   seeder is registered and the factory wasn't already requested).
4. Release advisory lock via `pg_advisory_unlock` in `finally`, regardless of
   success/failure of steps 2-3.

**No new `02.Caching` reference — deliberate decision:** The advisory lock uses
the existing `IDbConnectionFactory`, not `02.Caching.Redis.DistributedLocking`.
Consumers wanting a stronger/cross-database lock may wrap their own startup logic
with `SharedKernel.Caching.Redis.DistributedLocking` themselves — documented as an
option, not implemented here.

**Explicit non-goals:** Not a migration-authoring tool (use `dotnet ef migrations
add` as normal). Does not replace `.WithCompiledModel()` (P-106) — compiled models
and migrations operate independently.

**Why:** Multi-replica K8s deployments racing to migrate/seed on startup is a
common failure mode (duplicate-key errors, migration conflicts). The advisory lock
serializes without adding a new cross-domain dependency.

**How to apply:** Any future "run X once at startup across replicas" feature in
`06.Persistence` should reuse this PostgreSQL advisory-lock-via-IDbConnectionFactory
pattern rather than reaching for `02.Caching`.
