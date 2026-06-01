---
name: phase-numbering-state
description: Last known phase and work order numbers in the root state-map Phase Backlog
metadata:
  type: project
---

As of 2026-06-01, the last phase written to `state-map.md` Phase Backlog is **P-076** under **WO-013**.

Next new phase must be **P-077**. Next new Work Order must be **WO-014**.

**How to apply:** Always read the current Phase Backlog before assigning new IDs — this memory is a starting point, not a substitute for reading the file.

**WO-013 context:** Full persistence layer design for 06.Persistence. 12 phases across 4 domains:
- P-065 06.Persistence: Scaffold all four packages (csproj wiring, solution entries)
- P-066 06.Persistence: Abstractions (IRepository, IReadRepository, IUnitOfWork, IDbConnectionFactory, IOutboxWriter, OutboxMessage, ISpecificationEvaluator)
- P-067 06.Persistence: EfCore Core (SharedKernelDbContext, EntityTypeConfigurationBase, StronglyTypedIdValueConverter)
- P-068 06.Persistence: EfCore Interceptors (AuditInterceptor, SoftDeleteInterceptor, OutboxInterceptor, ConcurrencyInterceptor)
- P-069 06.Persistence: EfCore Repositories & UoW (EfRepository, EfReadRepository, EfUnitOfWork, SpecificationEvaluator)
- P-070 06.Persistence: EfCore Multi-Tenancy (TenantedDbContext, ICurrentTenantService, TenantedRepository)
- P-071 06.Persistence: PostgreSQL Package (SnakeCaseNaming, JSONB, pgvector, NpgsqlDataSource DI)
- P-072 06.Persistence: Dapper Package (NpgsqlConnectionFactory, type handlers, DapperReadService)
- P-073 06.Persistence: DI Extensions (PersistenceBuilder fluent pattern, AddOutbox, startup guards)
- P-074 06.Persistence: Tests (EfCore SQLite + PostgreSQL Testcontainer + Dapper integration)
- P-075 00.Governance: Persistence architecture rules (5 rules: IUnitOfWork save boundary, no IQueryable on IRepository, no persistence in Domain, IDbConnectionFactory-only, no SQL interpolation)
- P-076 16.Testing: Persistence test helpers (PostgreSqlContainerFixture, TestSharedKernelDbContext, AggregateRootFaker, OutboxMessageFaker, OutboxAssertions)

**State-map-phase calls made:** 06.Persistence (○→◐), 00.Governance (●→◐, arch-lead explicitly requested despite rule), 16.Testing (○→◐).
