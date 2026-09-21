using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Context;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Extensions;

// ---------------------------------------------------------------------------
// EfCorePersistenceBuilder.WithDbContextPooling() correctness tests.
// Pooling no longer resolves ANY scoped service anywhere in EF Core's pooled-context
// construction path (the options callback, or TContext's own other constructor parameters) — see
// EfCorePersistenceExtensions' PooledSeedActorContext / singleton-interceptor-registration remarks.
// The three platform interceptors (plus TenantWriteGuardInterceptor, when WithMultiTenancy() is also
// used) are registered SINGLETON when pooling is enabled; the scoped, decorated
// IDbContextFactory<TContext> (TenantAwareDbContextFactory<TContext>) attaches the REAL per-request
// IRequestContext/IRequestContext once per lease.
//
// WithMultiTenancy() + WithDbContextPooling() is now FULLY SUPPORTED — the
// former hard incompatibility guard is gone, replaced by the real fix described above
// (TenantedDbContext's constructor no longer takes IRequestContext at all; see its own class
// remarks). The SQLite-backed wiring/no-throw proof lives here; the authoritative multi-tenant
// concurrency proof (two concurrent scopes of different tenants sharing one pool, a background scope
// with no tenant failing closed, and a scope inside an active ICrossTenantScope) lives against REAL
// PostgreSQL in SharedKernel.Persistence.EfCore.Integration.Tests.Postgres — see
// PooledMultiTenancyPostgresTests.
// ---------------------------------------------------------------------------

/// <summary>Mutable, scoped-DI-friendly actor fake for pooling tests.</summary>
internal sealed class MutableTestActorContext : IRequestContext
{
    public string ActorId { get; set; } = "unset";
    public ActorKind ActorKind { get; set; } = ActorKind.User;
    public bool IsAuthenticated => true;
    public string? UserId => ActorId;
    public Guid? TenantId { get; set; }

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}

/// <summary>Mutable, scoped-DI-friendly tenant fake for pooling tests.</summary>
internal sealed class MutableTestTenantContext : IRequestContext
{
    public Guid? TenantId { get; set; }
    public bool IsAuthenticated => true;
    public string? UserId => "tenant-test";

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}

public sealed class DbContextPoolingTests
{
    // -------------------------------------------------------------------------
    // GATING: pooled cross-request isolation (poolSize = 1 guarantees instance reuse)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WithDbContextPooling_PoolSizeOne_RefreshesActorContext_PerLease_NoCrossRequestLeak()
    {
        // Arrange — a single already-open SQLite connection shared by every pooled instance so
        // data persists across leases regardless of which physical instance services which scope.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddScoped<MutableTestActorContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<MutableTestActorContext>());

        services
            .AddSharedKernelEfCore<TestDbContext>(options => options
                .UseSqlite(connection)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithDbContextPooling(poolSize: 1)
            .Build();

        // ValidateScopes = true (H-A6): a pooled DbContext's OPTIONS/interceptors/other constructor
        // parameters must never resolve a scoped service from what is effectively the root provider —
        // this would throw here if the fix regressed.
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        using (var setupScope = provider.CreateScope())
        {
            var setupCtx = setupScope.ServiceProvider.GetRequiredService<TestDbContext>();
            await setupCtx.Database.EnsureCreatedAsync();
        }

        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        TestDbContext requestAInstance;

        // Act — Request A: scope 1, User A.
        using (var scopeA = provider.CreateScope())
        {
            var actorContextA = scopeA.ServiceProvider.GetRequiredService<MutableTestActorContext>();
            actorContextA.ActorId = userAId.ToString("D");

            var ctxA = scopeA.ServiceProvider.GetRequiredService<TestDbContext>();
            requestAInstance = ctxA;

            ctxA.AuditableAggregates.Add(new AuditableTestAggregate(TestId.New(), "FromRequestA", new SystemClock()));
            await ctxA.SaveChangesAsync();
        }

        // Act — Request B: scope 2, User B. poolSize = 1 guarantees the same underlying pooled
        // instance leased to Request A is reused here (sequential, not concurrent, usage — no
        // second slot was ever needed).
        using (var scopeB = provider.CreateScope())
        {
            var actorContextB = scopeB.ServiceProvider.GetRequiredService<MutableTestActorContext>();
            actorContextB.ActorId = userBId.ToString("D");

            var ctxB = scopeB.ServiceProvider.GetRequiredService<TestDbContext>();

            ReferenceEquals(ctxB, requestAInstance).Should().BeTrue(
                "poolSize = 1 guarantees the same underlying pooled instance is reused across sequential scopes");

            ctxB.AuditableAggregates.Add(new AuditableTestAggregate(TestId.New(), "FromRequestB", new SystemClock()));
            await ctxB.SaveChangesAsync();

            // Assert — CreatedBy is attributed to User B, never the stale User A.
            var addedRow = await ctxB.AuditableAggregates.AsNoTracking()
                .FirstAsync(a => a.Name == "FromRequestB");
            addedRow.CreatedBy.Should().Be(userBId.ToString("D"));
        }
    }

    // -------------------------------------------------------------------------
    // GATING: ValidateScopes + ValidateOnBuild must not throw even without any explicit actor
    // registration (the builder's own defaults must be pool-safe).
    // -------------------------------------------------------------------------

    [Fact]
    public void WithDbContextPooling_DefaultRegistration_ValidateScopesAndValidateOnBuild_DoesNotThrow()
    {
        var services = new ServiceCollection();

        services
            .AddSharedKernelEfCore<TestDbContext>(options => options
                .UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithDbContextPooling(poolSize: 4)
            .Build();

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        act.Should().NotThrow(
            "the default IRequestContext registration, the singleton platform interceptors, and " +
            "the pooled options callback must never resolve a scoped service from the root provider");
    }

    // -------------------------------------------------------------------------
    // GATING: WithDbContextPooling() + WithMultiTenancy() is now FULLY SUPPORTED — the former
    // hard incompatibility guard is gone. ValidateScopes proves neither
    // TenantedDbContext's constructor nor TenantWriteGuardInterceptor's constructor resolves a
    // genuinely Scoped service from EF Core's pool-level activator.
    // -------------------------------------------------------------------------

    [Fact]
    public void WithDbContextPooling_CombinedWithMultiTenancy_ValidateScopesAndValidateOnBuild_DoesNotThrow()
    {
        var services = new ServiceCollection();

        services
            .AddSharedKernelEfCore<TenantedTestDbContext>(options => options
                .UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithMultiTenancy()
            .WithDbContextPooling(poolSize: 4)
            .Build();

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        act.Should().NotThrow(
            "TenantedDbContext no longer takes IRequestContext in its constructor, and " +
            "TenantWriteGuardInterceptor's only dependency (ICrossTenantScope) is singleton-safe — " +
            "neither the pooled options callback nor TContext's own other constructor parameters " +
            "resolve a genuinely Scoped service from the pool-level activator");
    }

    [Fact]
    public async Task WithDbContextPooling_PoolSizeOne_MultiTenancy_RefreshesTenantContext_PerLease_NoCrossTenantLeak()
    {
        // Arrange — a single already-open SQLite connection shared by every pooled instance so
        // data persists across leases regardless of which physical instance services which scope.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddScoped<MutableTestTenantContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<MutableTestTenantContext>());

        services
            .AddSharedKernelEfCore<TenantedTestDbContext>(options => options
                .UseSqlite(connection)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithMultiTenancy()
            .WithDbContextPooling(poolSize: 1)
            .Build();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        using (var setupScope = provider.CreateScope())
        {
            await setupScope.ServiceProvider.GetRequiredService<TenantedTestDbContext>()
                .Database.EnsureCreatedAsync();
        }

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        TenantedTestDbContext requestAInstance;

        // Act — Request A: scope 1, Tenant A.
        using (var scopeA = provider.CreateScope())
        {
            scopeA.ServiceProvider.GetRequiredService<MutableTestTenantContext>().TenantId = tenantAId;

            var ctxA = scopeA.ServiceProvider.GetRequiredService<TenantedTestDbContext>();
            requestAInstance = ctxA;

            ctxA.TenantedAggregates.Add(new TenantedTestAggregate(
                TenantedTestId.New(), "FromTenantA", tenantAId, new SystemClock()));
            await ctxA.SaveChangesAsync();
        }

        // Act — Request B: scope 2, Tenant B. poolSize = 1 guarantees the same underlying pooled
        // instance leased to Request A is reused here (sequential, not concurrent, usage).
        using (var scopeB = provider.CreateScope())
        {
            scopeB.ServiceProvider.GetRequiredService<MutableTestTenantContext>().TenantId = tenantBId;

            var ctxB = scopeB.ServiceProvider.GetRequiredService<TenantedTestDbContext>();

            ReferenceEquals(ctxB, requestAInstance).Should().BeTrue(
                "poolSize = 1 guarantees the same underlying pooled instance is reused across sequential scopes");

            ctxB.TenantedAggregates.Add(new TenantedTestAggregate(
                TenantedTestId.New(), "FromTenantB", tenantBId, new SystemClock()));
            await ctxB.SaveChangesAsync();

            // Assert — Request B's tenant filter sees ONLY Tenant B's row — the pooled instance's
            // filter is bound to THIS lease's tenant, never Tenant A's (the prior lease).
            var visibleToB = await ctxB.TenantedAggregates.ToListAsync();
            visibleToB.Should().ContainSingle(r => r.Name == "FromTenantB");
            visibleToB.Should().NotContain(r => r.Name == "FromTenantA");
        }
    }

    // -------------------------------------------------------------------------
    // GATING (non-pooled): pre-existing model-cache staleness defect regression proof
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TenantedDbContext_TwoSequentialNonPooledInstances_DifferentTenantProviders_CorrectlyIsolate()
    {
        // Proves the CONFIRMED pre-existing defect (independent of pooling) is fixed: EF Core's
        // model cache is keyed only by DbContext TYPE and is shared process-wide, so two
        // SEQUENTIAL, non-pooled SoftDeletableTenantedDbContext instances reuse the identical
        // cached compiled model (and its filter expression tree). Under the old
        // Expression.Constant(specificProviderObject,...) design, the second instance would
        // silently see the FIRST instance's tenant filter forever. The corrected
        // Expression.Constant(this, GetType()) binding makes each instance see its OWN
        // tenant context despite sharing the cached model.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SoftDeletableTenantedDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        // First instance — actor context bound to Tenant A. Constructing and using it populates
        // EF Core's process-wide model cache for SoftDeletableTenantedDbContext.
        var actorContextA = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenantAId);
        using (var ctxA = new SoftDeletableTenantedDbContext(
            options,
            PersistenceContextDependencies.Create(actorContextA, new SystemClock())))
        {
            ctxA.RefreshRequestContext(actorContextA);
            await ctxA.Database.EnsureCreatedAsync();
            ctxA.SdAggregates.Add(new SoftDeletableTenantedAggregate(
                TenantedTestId.New(), "TenantARow", tenantAId, new SystemClock()));
            await ctxA.SaveChangesAsync();
        }

        // Second instance — a DIFFERENT SoftDeletableTenantedDbContext instance, constructed
        // AFTER ctxA is disposed, bound to a DIFFERENT tenant. Reuses EF Core's cached model.
        var actorContextB = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenantBId);
        using var ctxB = new SoftDeletableTenantedDbContext(
            options,
            PersistenceContextDependencies.Create(actorContextB, new SystemClock()));
        ctxB.RefreshRequestContext(actorContextB);

        ctxB.SdAggregates.Add(new SoftDeletableTenantedAggregate(
            TenantedTestId.New(), "TenantBRow", tenantBId, new SystemClock()));
        await ctxB.SaveChangesAsync();

        // Assert — ctxB (bound to tenant context B) sees ONLY Tenant B's row, never Tenant A's.
        var visibleToB = await ctxB.SdAggregates.ToListAsync();
        visibleToB.Should().ContainSingle(r => r.Name == "TenantBRow");
        visibleToB.Should().NotContain(r => r.Name == "TenantARow");
    }

    // -------------------------------------------------------------------------
    // Existing non-pooled isolation tests continue to pass unmodified — spot-checked here via
    // a fresh single-instance round trip (the broader suite's own TenantedDbContext/Audit/
    // SoftDelete tests provide the full regression coverage).
    // -------------------------------------------------------------------------

    [Fact]
    public void WithDbContextPooling_Alone_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var act = () =>
            services
                .AddSharedKernelEfCore<TestDbContext>(options => options
                    .UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                .WithDbContextPooling()
                .Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void WithDbContextPooling_CombinedWithDbContextFactory_DoesNotThrow_RegistersOnePooledFactory()
    {
        // WithDbContextFactory()/RequireDbContextFactory() are no-ops now —
        // IDbContextFactory<TContext> is always registered by WithDbContextPooling() itself (via
        // TenantAwareDbContextFactory<TContext>), so calling both no longer registers two
        // conflicting factories.
        var services = new ServiceCollection();

        var act = () =>
            services
                .AddSharedKernelEfCore<TestDbContext>(options => options
                    .UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                .WithDbContextPooling()
                .WithDbContextFactory()
                .Build();

        act.Should().NotThrow();

        // Exactly one UNKEYED (public) IDbContextFactory<TContext> registration — the
        // TenantAwareDbContextFactory<TContext> decorator. The real pooled factory it wraps is also
        // registered under ServiceType == typeof(IDbContextFactory<TestDbContext>), but KEYED (a
        // private key), so it is excluded by the !IsKeyedService filter — this is what proves there
        // is no leftover SECOND, conflicting PUBLIC factory registration.
        services.Count(sd => sd.ServiceType == typeof(IDbContextFactory<TestDbContext>) && !sd.IsKeyedService)
            .Should().Be(1,
                "exactly one public IDbContextFactory<TContext> registration should exist — the " +
                "TenantAwareDbContextFactory<TContext> decorator, wrapping the single pooled factory " +
                "under its private key");
    }

    // WithDbContextPooling_CombinedWithEncryption_ThrowsAtBuild moved: relocated
    // .WithEncryption() to the sibling SharedKernel.Persistence.EfCore.Encryption package, which this
    // project does not (and must not) reference. The pooling+encryption guard now lives in that
    // package's own EnsureEncryptionInfrastructureRegistered (checks builder.IsDbContextPoolingEnabled)
    // and its proof belongs in SharedKernel.Persistence.EfCore.Encryption.Tests.

    [Fact]
    public void WithDbContextPooling_Registers_PooledIDbContextFactory()
    {
        var services = new ServiceCollection();

        services
            .AddSharedKernelEfCore<TestDbContext>(options => options
                .UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithDbContextPooling(poolSize: 8)
            .Build();

        // The PUBLIC (unkeyed) registration is now TenantAwareDbContextFactory<TContext>,
        // wrapping the real AddPooledDbContextFactory-registered factory under a private key.
        var descriptor = services.FirstOrDefault(sd =>
            sd.ServiceType == typeof(IDbContextFactory<TestDbContext>) && !sd.IsKeyedService);
        descriptor.Should().NotBeNull("WithDbContextPooling() should register IDbContextFactory<TContext> (decorated) via AddPooledDbContextFactory");
    }

    // -------------------------------------------------------------------------
    // Allocation comparison — pooled vs. default, generous tolerance (CI-timing-safe).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WithDbContextPooling_RepeatedResolveDispose_AllocatesLessThanDefaultRegistration()
    {
        const int iterations = 50;

        // Default (non-pooled) baseline.
        using var defaultConnection = new SqliteConnection("DataSource=:memory:");
        defaultConnection.Open();
        var defaultServices = new ServiceCollection();
        defaultServices
            .AddSharedKernelEfCore<TestDbContext>(options => options
                .UseSqlite(defaultConnection)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();
        var defaultProvider = defaultServices.BuildServiceProvider();

        using (var warmupScope = defaultProvider.CreateScope())
        {
            await warmupScope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        GC.Collect();
        var defaultBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            using var scope = defaultProvider.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        }
        var defaultAllocated = GC.GetAllocatedBytesForCurrentThread() - defaultBefore;

        // Pooled.
        using var pooledConnection = new SqliteConnection("DataSource=:memory:");
        pooledConnection.Open();
        var pooledServices = new ServiceCollection();
        pooledServices
            .AddSharedKernelEfCore<TestDbContext>(options => options
                .UseSqlite(pooledConnection)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithDbContextPooling(poolSize: 32)
            .Build();
        var pooledProvider = pooledServices.BuildServiceProvider();

        using (var warmupScope = pooledProvider.CreateScope())
        {
            await warmupScope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        GC.Collect();
        var pooledBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            using var scope = pooledProvider.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        }
        var pooledAllocated = GC.GetAllocatedBytesForCurrentThread() - pooledBefore;

        // Generous tolerance — this demonstrates the intended benefit direction without being a
        // precise, CI-timing-sensitive micro-benchmark. Pooling reuses the underlying DbContext
        // instance instead of constructing a fresh one (plus its interceptor graph) every time.
        pooledAllocated.Should().BeLessThan(defaultAllocated,
            "pooled resolution should allocate measurably less than constructing a fresh DbContext per scope");
    }
}
