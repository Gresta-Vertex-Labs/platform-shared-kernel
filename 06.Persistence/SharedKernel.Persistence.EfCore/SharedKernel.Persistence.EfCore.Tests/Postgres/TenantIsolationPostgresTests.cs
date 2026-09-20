using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Postgres;

/// <summary>
/// Proves tenant isolation (query filter, keyset, bulk
/// ExecuteUpdate/ExecuteDelete) and the write-side <see cref="TenantWriteGuardInterceptor"/> against
/// REAL PostgreSQL. Every named Critical/High tenant-isolation scenario
/// lives in this one class.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class TenantIsolationPostgresTests
{
    private const string DatabaseName = "sk_p557_tenant_isolation";

    private readonly PostgreSqlContainerFixture _fixture;

    public TenantIsolationPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    // Every test in this suite proves write-side tenant isolation, so TenantWriteGuardInterceptor is
    // always wired in — mirroring how WithMultiTenancy() always adds it in production. Callers that
    // also need a repository-level ICrossTenantScope pass the SAME instance so Enter() on the
    // repository's scope is visible to the guard interceptor's own scope check (ICrossTenantScope is
    // AsyncLocal-backed PER INSTANCE, not process-wide — two different instances never share state).
    private PgTestDbContext CreateContext(Guid tenantId, string actorId = "actor", ICrossTenantScope? crossTenantScope = null) =>
        PgTestDbContextFactory.Create(
            ConnectionString,
            new FakeAuditActorContext(actorId),
            new FakeAuditActorContext(actorId, tenantId),
            additionalInterceptors: [new TenantWriteGuardInterceptor(crossTenantScope ?? new CrossTenantScope())]);

    private static PgOrderAggregate NewOrder(Guid tenantId, string code) =>
        new(PgOrderId.New(), tenantId, $"Order-{code}", code, "Main St", "Springfield", new SharedKernel.Primitives.Clocks.SystemClock());

    [Fact]
    public async Task PlainDbSet_TenantedSoftDeletableEntity_BothFiltersActive()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var codePrefix = $"plain-{Guid.NewGuid():N}";

        await using (var setup = CreateContext(tenantA))
            await setup.Database.EnsureCreatedAsync();

        var visibleId = PgOrderId.New();
        await using (var ctxA = CreateContext(tenantA))
        {
            ctxA.Orders.Add(new PgOrderAggregate(visibleId, tenantA, "Visible", $"{codePrefix}-visible", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            var deletedId = PgOrderId.New();
            ctxA.Orders.Add(new PgOrderAggregate(deletedId, tenantA, "ToDelete", $"{codePrefix}-deleted", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctxA.SaveChangesAsync();

            ctxA.ChangeTracker.Clear();
            var toDelete = await ctxA.Orders.FirstAsync(o => o.Id == deletedId);
            ctxA.Orders.Remove(toDelete);
            await ctxA.SaveChangesAsync();
        }

        await using (var ctxB = CreateContext(tenantB))
        {
            // Plain DbSet query — no repository, no explicit IgnoreQueryFilters. Tenant B never sees
            // Tenant A's rows (tenant filter), and no caller ever sees the soft-deleted row
            // (soft-delete filter) — both named filters combine with AND.
            var visibleToB = await ctxB.Orders.Where(o => o.Code.StartsWith(codePrefix)).ToListAsync();
            visibleToB.Should().BeEmpty("Tenant B must see none of Tenant A's rows through a plain DbSet query");
        }

        await using (var ctxA2 = CreateContext(tenantA))
        {
            var visibleToA = await ctxA2.Orders.Where(o => o.Code.StartsWith(codePrefix)).ToListAsync();
            visibleToA.Should().ContainSingle(o => o.Id == visibleId,
                "the soft-deleted row must be excluded even for the OWNING tenant, through a plain DbSet query");
        }
    }

    [Fact]
    public async Task IncludeDeleted_Query_StaysInsideTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var codePrefix = $"incdel-q-{Guid.NewGuid():N}";

        await using (var setup = CreateContext(tenantA))
            await setup.Database.EnsureCreatedAsync();

        var deletedAId = PgOrderId.New();
        await using (var ctxA = CreateContext(tenantA))
        {
            ctxA.Orders.Add(new PgOrderAggregate(deletedAId, tenantA, "A", $"{codePrefix}-a", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctxA.SaveChangesAsync();
            ctxA.ChangeTracker.Clear();
            ctxA.Orders.Remove(await ctxA.Orders.FirstAsync(o => o.Id == deletedAId));
            await ctxA.SaveChangesAsync();
        }

        var deletedBId = PgOrderId.New();
        await using (var ctxB = CreateContext(tenantB))
        {
            ctxB.Orders.Add(new PgOrderAggregate(deletedBId, tenantB, "B", $"{codePrefix}-b", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctxB.SaveChangesAsync();
            ctxB.ChangeTracker.Clear();
            ctxB.Orders.Remove(await ctxB.Orders.FirstAsync(o => o.Id == deletedBId));
            await ctxB.SaveChangesAsync();
        }

        // Tenant A, IncludeDeleted (IgnoreQueryFilters([SoftDelete]) only) — sees ITS OWN
        // soft-deleted row, never Tenant B's — proving IncludeDeleted never also drops the tenant filter.
        await using var readCtxA = CreateContext(tenantA);
        var readRepoA = new PgOrderReadRepository(readCtxA);
        var resultA = await readRepoA.ListAsync(new PgOrdersByCodePrefixSpecification(codePrefix, includeDeleted: true));
        resultA.Select(o => o.Id).Should().Contain(deletedAId).And.NotContain(deletedBId);
    }

    [Fact]
    public async Task IncludeDeleted_Keyset_StaysInsideTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var setup = CreateContext(tenantA))
            await setup.Database.EnsureCreatedAsync();

        var deletedAId = PgOrderId.New();
        await using (var ctxA = CreateContext(tenantA))
        {
            ctxA.Orders.Add(new PgOrderAggregate(deletedAId, tenantA, "A", $"keyset-a-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctxA.SaveChangesAsync();
            ctxA.ChangeTracker.Clear();
            ctxA.Orders.Remove(await ctxA.Orders.FirstAsync(o => o.Id == deletedAId));
            await ctxA.SaveChangesAsync();
        }

        var deletedBId = PgOrderId.New();
        await using (var ctxB = CreateContext(tenantB))
        {
            ctxB.Orders.Add(new PgOrderAggregate(deletedBId, tenantB, "B", $"keyset-b-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctxB.SaveChangesAsync();
            ctxB.ChangeTracker.Clear();
            ctxB.Orders.Remove(await ctxB.Orders.FirstAsync(o => o.Id == deletedBId));
            await ctxB.SaveChangesAsync();
        }

        await using var readCtxA = CreateContext(tenantA);
        var readRepoA = new PgOrderReadRepository(readCtxA);

        var allIdsSeenByA = new List<PgOrderId>();
        DateTimeOffset? afterKey = null;
        object? afterId = null;
        bool hasMore;
        do
        {
            var page = await readRepoA.ListKeysetAsync(
                new PgOrdersByCreatedOnKeysetSpecification(afterKey, afterId, take: 50, includeDeleted: true));
            allIdsSeenByA.AddRange(page.Items.Select(o => o.Id));
            if (page.NextCursor is null) { hasMore = false; }
            else
            {
                var decoded = PageCursor.Decode<DateTimeOffset, PgOrderId>(page.NextCursor);
                decoded.IsSuccess.Should().BeTrue();
                afterKey = decoded.Value.Key;
                afterId = decoded.Value.Id;
                hasMore = page.HasMore;
            }
        } while (hasMore);

        allIdsSeenByA.Should().Contain(deletedAId).And.NotContain(deletedBId,
            "an IncludeDeleted keyset walk must stay inside the owning tenant, never leak another tenant's soft-deleted rows");
    }

    [Fact]
    public async Task IncludeDeleted_BulkExecuteUpdate_StaysInsideTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var prefix = $"bulk-upd-{Guid.NewGuid():N}";

        await using (var setup = CreateContext(tenantA))
            await setup.Database.EnsureCreatedAsync();

        await using (var ctxA = CreateContext(tenantA))
        {
            ctxA.Orders.Add(NewOrder(tenantA, $"{prefix}-a"));
            await ctxA.SaveChangesAsync();
        }
        await using (var ctxB = CreateContext(tenantB))
        {
            ctxB.Orders.Add(NewOrder(tenantB, $"{prefix}-b"));
            await ctxB.SaveChangesAsync();
        }

        await using var ctx = CreateContext(tenantA);
        var repo = new PgOrderRepository(ctx, new CrossTenantScope());
        var updated = await repo.ExecuteUpdateAsync(
            new PgOrdersByCodePrefixSpecification(prefix), s => s.SetProperty(o => o.Name, "BulkRenamed"));

        updated.Should().Be(1, "the bulk ExecuteUpdate must only touch Tenant A's own row, never Tenant B's");

        await using var verifyB = CreateContext(tenantB);
        var stillOriginal = await verifyB.Orders.FirstAsync(o => o.Code == $"{prefix}-b");
        stillOriginal.Name.Should().NotBe("BulkRenamed");
    }

    [Fact]
    public async Task IncludeDeleted_BulkExecuteDelete_StaysInsideTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var prefix = $"bulk-del-{Guid.NewGuid():N}";

        await using (var setup = CreateContext(tenantA))
            await setup.Database.EnsureCreatedAsync();

        await using (var ctxA = CreateContext(tenantA))
        {
            ctxA.Orders.Add(NewOrder(tenantA, $"{prefix}-a"));
            await ctxA.SaveChangesAsync();
        }
        await using (var ctxB = CreateContext(tenantB))
        {
            ctxB.Orders.Add(NewOrder(tenantB, $"{prefix}-b"));
            await ctxB.SaveChangesAsync();
        }

        await using var ctx = CreateContext(tenantA);
        var repo = new PgOrderRepository(ctx, new CrossTenantScope());
        var deleted = await repo.ExecuteDeleteAsync(new PgOrdersByCodePrefixSpecification(prefix));

        deleted.Should().Be(1, "the bulk ExecuteDelete must only remove Tenant A's own row, never Tenant B's");

        await using var verifyB = CreateContext(tenantB);
        (await verifyB.Orders.CountAsync(o => o.Code == $"{prefix}-b")).Should().Be(1);
    }

    [Fact]
    public async Task CrossTenantInsert_Rejected()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var setup = CreateContext(tenantA);
        await setup.Database.EnsureCreatedAsync();

        // ctx is scoped to Tenant A, but the entity being inserted claims Tenant B.
        await using var ctx = CreateContext(tenantA);
        ctx.Orders.Add(NewOrder(tenantB, $"cross-insert-{Guid.NewGuid():N}"));

        var act = () => ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<ForbiddenException>(
            "TenantWriteGuardInterceptor must reject an insert whose TenantId does not match the current tenant");
    }

    [Fact]
    public async Task CrossTenantUpdate_Detached_Rejected()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var setup = CreateContext(tenantA);
        await setup.Database.EnsureCreatedAsync();

        var order = NewOrder(tenantB, $"cross-update-{Guid.NewGuid():N}");
        await using (var ctxB = CreateContext(tenantB))
        {
            ctxB.Orders.Add(order);
            await ctxB.SaveChangesAsync();
        }

        // A NEW scope, bound to Tenant A, re-attaches (Update()) a DETACHED entity that actually
        // belongs to Tenant B — the classic "re-attach a modified detached entity" write path.
        await using var ctxA = CreateContext(tenantA);
        order.Rename("HackedFromTenantA");
        ctxA.Orders.Update(order);

        var act = () => ctxA.SaveChangesAsync();
        await act.Should().ThrowAsync<ForbiddenException>(
            "re-attaching a detached entity belonging to another tenant must be rejected, not silently written");
    }

    [Fact]
    public async Task CrossTenantDelete_Rejected()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var setup = CreateContext(tenantA);
        await setup.Database.EnsureCreatedAsync();

        var orderId = PgOrderId.New();
        await using (var ctxB = CreateContext(tenantB))
        {
            ctxB.Orders.Add(new PgOrderAggregate(orderId, tenantB, "ToDelete", $"cross-delete-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctxB.SaveChangesAsync();
        }

        // Tenant A's own scope cannot even SEE Tenant B's row through the query filter, but a
        // caller with a raw id (e.g. from an external system) can still try to construct+delete a
        // stub entity — the write guard is the last line of defense, independent of the read filter.
        await using var ctxA = CreateContext(tenantA);
        var stub = new PgOrderAggregate(orderId, tenantB, "Stub", "stub-code", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock());
        ctxA.Orders.Attach(stub);
        ctxA.Orders.Remove(stub);

        var act = () => ctxA.SaveChangesAsync();
        await act.Should().ThrowAsync<ForbiddenException>(
            "deleting a row that does not belong to the current tenant must be rejected");
    }

    [Fact]
    public async Task CrossTenantBulkSetPropertyTenantId_Rejected()
    {
        var tenantA = Guid.NewGuid();

        await using var setup = CreateContext(tenantA);
        await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantA);
        ctx.Orders.Add(NewOrder(tenantA, $"bulk-settenant-{Guid.NewGuid():N}"));
        await ctx.SaveChangesAsync();

        var repo = new PgOrderRepository(ctx, new CrossTenantScope());

        // A bulk ExecuteUpdate that tries to move rows to a DIFFERENT tenant via SetProperty must be
        // rejected at the guard level — before any SQL is even issued — never silently executed.
        // AllRowsSpecification<T> is the explicit opt-in BulkSpecificationGuard requires for a
        // criteria-less bulk mutation — using it here isolates the assertion to the SetProperty(TenantId)
        // rejection specifically, not the separate "no criteria" rejection.
        var act = () => repo.ExecuteUpdateAsync(
            new SharedKernel.Persistence.EfCore.Repositories.AllRowsSpecification<PgOrderAggregate>(),
            s => s.SetProperty(o => o.TenantId, Guid.NewGuid()));

        await act.Should().ThrowAsync<SharedKernel.Persistence.EfCore.Repositories.UnsupportedSpecificationException>(
            "BulkSpecificationGuard must reject a SetProperty targeting TenantId");
    }

    [Fact]
    public async Task CrossTenantBulkSetPropertyTenantId_ViaEfPropertyStringName_Rejected()
    {
        var tenantA = Guid.NewGuid();

        await using var setup = CreateContext(tenantA);
        await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantA);
        ctx.Orders.Add(NewOrder(tenantA, $"bulk-efprop-{Guid.NewGuid():N}"));
        await ctx.SaveChangesAsync();

        var repo = new PgOrderRepository(ctx, new CrossTenantScope());

        // Same attack as the sibling test above, but naming the column through EF.Property's STRING
        // overload instead of a direct member access. UpdateSettersInspector cannot resolve that
        // shape to a CLR member name and reports it as an empty string, so before the fail-closed
        // check in BulkSpecificationGuard.ValidateSetters this walked straight past the
        // protected-name comparisons and moved the rows into another tenant. A selector the
        // inspector cannot name must be rejected outright, not assumed harmless.
        var act = () => repo.ExecuteUpdateAsync(
            new SharedKernel.Persistence.EfCore.Repositories.AllRowsSpecification<PgOrderAggregate>(),
            s => s.SetProperty(o => EF.Property<Guid>(o, "TenantId"), Guid.NewGuid()));

        await act.Should().ThrowAsync<SharedKernel.Persistence.EfCore.Repositories.UnsupportedSpecificationException>(
            "a setter whose target the inspector cannot resolve must fail closed, since it can name "
                + "a protected column the name-based checks never see");
    }

    [Fact]
    public async Task CrossTenantUpdate_Detached_AttackerTenantClaimedWithVictimPrimaryKey_Rejected()
    {
        // The INVERTED shape the sibling CrossTenantUpdate_Detached_Rejected test above does NOT
        // cover: that test's stub claims the VICTIM's tenant id, which TenantWriteGuardInterceptor's
        // in-memory check catches trivially. Here the stub claims the ATTACKER's OWN, legitimate
        // current tenant (tenantA) — passing that same in-memory check — but carries a row that
        // actually belongs to tenantB. Only the TenantId concurrency token (enforced at the real
        // Postgres UPDATE statement's WHERE clause) can catch this.
        //
        // PgOrderAggregate is TenantedFullAuditableAggregateRoot, so it ALSO implements
        // IHasConcurrency (a real xmin-backed RowVersion) — ConcurrencyInterceptor.TryTranslate checks
        // IHasConcurrency before IHasTenant, and a DbUpdateConcurrencyException from Postgres carries
        // no way to tell which of the WHERE clause's columns (TenantId, RowVersion, or both — the
        // stub's default RowVersion never matches the real row's xmin either) caused the zero-row
        // match, so this shape is reported as ConflictException here, not ForbiddenException. Both are
        // a REJECTED write — the security guarantee under test is that the victim's row survives
        // untouched, proven below regardless of which typed exception surfaces. The SQLite suite
        // (TenantWriteGuardInterceptorTests, using a plain TenantedTestAggregate with NO RowVersion)
        // proves ForbiddenException specifically for a tenanted aggregate that carries no concurrency
        // token of its own — the concurrency-token mechanism itself is provider-neutral, so that
        // coverage is not Postgres-specific.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var setup = CreateContext(tenantA);
        await setup.Database.EnsureCreatedAsync();

        var victimId = PgOrderId.New();
        await using (var ctxB = CreateContext(tenantB))
        {
            ctxB.Orders.Add(new PgOrderAggregate(victimId, tenantB, "Victim", $"inverted-update-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctxB.SaveChangesAsync();
        }

        await using var ctxA = CreateContext(tenantA);
        var stub = new PgOrderAggregate(victimId, tenantA, "HackedFromTenantA", $"attacker-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock());
        ctxA.Orders.Update(stub);

        var act = () => ctxA.SaveChangesAsync();
        await act.Should().ThrowAsync<ConflictException>(
            "a detached stub claiming the attacker's own tenant id but the victim's primary key must " +
                "still be rejected, not silently rewrite the victim's row");

        await using var verifyB = CreateContext(tenantB);
        var stillThere = await verifyB.Orders.FirstAsync(o => o.Id == victimId);
        stillThere.Name.Should().Be("Victim", "the victim's row must be completely untouched");
    }

    [Fact]
    public async Task CrossTenantDelete_AttackerTenantClaimedWithVictimPrimaryKey_Rejected()
    {
        // Same inverted shape as above, for the delete path (Attach()+Remove()) — see the sibling
        // update test's remarks for why PgOrderAggregate's own RowVersion means this surfaces as
        // ConflictException here, never ForbiddenException.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var setup = CreateContext(tenantA);
        await setup.Database.EnsureCreatedAsync();

        var victimId = PgOrderId.New();
        await using (var ctxB = CreateContext(tenantB))
        {
            ctxB.Orders.Add(new PgOrderAggregate(victimId, tenantB, "Victim", $"inverted-delete-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctxB.SaveChangesAsync();
        }

        await using var ctxA = CreateContext(tenantA);
        var stub = new PgOrderAggregate(victimId, tenantA, "Stub", $"attacker-del-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock());
        ctxA.Orders.Attach(stub);
        ctxA.Orders.Remove(stub);

        var act = () => ctxA.SaveChangesAsync();
        await act.Should().ThrowAsync<ConflictException>(
            "a detached delete claiming the attacker's own tenant id but the victim's primary key " +
                "must still be rejected");

        await using var verifyB = CreateContext(tenantB);
        (await verifyB.Orders.CountAsync(o => o.Id == victimId)).Should().Be(1, "the victim's row must survive");
    }

    [Fact]
    public async Task CrossTenantScopeBypass_WorksAndIsExplicit()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var setup = CreateContext(tenantA);
        await setup.Database.EnsureCreatedAsync();

        var orderId = PgOrderId.New();
        await using (var ctxB = CreateContext(tenantB))
        {
            ctxB.Orders.Add(new PgOrderAggregate(orderId, tenantB, "Admin", $"scope-bypass-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctxB.SaveChangesAsync();
        }

        // Without an active scope: rejected.
        await using (var ctxA1 = CreateContext(tenantA))
        {
            var repo1 = new PgOrderRepository(ctxA1, new CrossTenantScope());
            var act = () => repo1.GetByIdForTenantAsync(orderId, tenantB);
            await act.Should().ThrowAsync<InvalidOperationException>(
                "cross-tenant lookup must be rejected with no active ICrossTenantScope");
        }

        // With an EXPLICIT, entered scope: succeeds and reads the other tenant's row.
        await using (var ctxA2 = CreateContext(tenantA))
        {
            var crossTenantScope = new CrossTenantScope();
            var repo2 = new PgOrderRepository(ctxA2, crossTenantScope);

            using (crossTenantScope.Enter())
            {
                var found = await repo2.GetByIdForTenantAsync(orderId, tenantB);
                found.Should().NotBeNull("an explicitly entered ICrossTenantScope must allow the cross-tenant read");
                found!.TenantId.Should().Be(tenantB);
            }

            // Scope disposed — bypass is deactivated again.
            var act = () => repo2.GetByIdForTenantAsync(orderId, tenantB);
            await act.Should().ThrowAsync<InvalidOperationException>(
                "the bypass must deactivate once the scope handle is disposed");
        }
    }
}
