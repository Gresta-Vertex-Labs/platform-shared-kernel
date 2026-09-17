using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Persistence.EfCore.Tests.Extensions;

// ---------------------------------------------------------------------------
// WO-051/P-322 — EfCorePersistenceBuilder.WithDbContextPooling() correctness tests (D-78/D-79/D-80).
// ---------------------------------------------------------------------------

/// <summary>Mutable, scoped-DI-friendly <see cref="IUserContext"/> fake for pooling tests.</summary>
internal sealed class MutableTestUserContext : IUserContext
{
    public string? SubjectId { get; set; }
    public string? ClientId => null;
    public Guid? TenantId => null;
    public string? SessionId => null;
    public string? Name => null;
    public string? Email => null;
    public IReadOnlyCollection<string> Roles => [];
    public IReadOnlyCollection<string> Permissions => [];
    public string? FindClaim(string claimType) => null;
    public IReadOnlyList<string> FindClaims(string claimType) => [];
    public bool IsAuthenticated => true;
    public IdentityKind IdentityKind => IdentityKind.User;
    public bool HasRole(string role) => false;
    public bool HasPermission(string permission) => false;
    public IReadOnlyCollection<string> AuthenticationMethods => [];
    public string? AuthContextClassReference => null;
    public DateTimeOffset? AuthTime => null;
    public bool IsSenderConstrained => false;
    public bool WasAuthenticatedWith(string method) => false;
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) => false;
}

/// <summary>Mutable, scoped-DI-friendly <see cref="ITenantProvider"/> fake for pooling tests.</summary>
internal sealed class MutableTestTenantProvider : ITenantProvider
{
    public Guid TenantId { get; set; }
}

public sealed class DbContextPoolingTests
{
    // -------------------------------------------------------------------------
    // GATING: pooled cross-request isolation (poolSize = 1 guarantees instance reuse)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WithDbContextPooling_PoolSizeOne_RefreshesUserAndTenantContext_PerLease_NoCrossRequestLeak()
    {
        // Arrange — a single already-open SQLite connection shared by every pooled instance so
        // data persists across leases regardless of which physical instance services which scope.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddScoped<IUserContext, MutableTestUserContext>();
        services.AddScoped<ITenantProvider, MutableTestTenantProvider>();

        // Deliberately NOT calling .WithMultiTenancy() here: this test supplies its own ITenantProvider
        // directly, which is exactly the scenario .WithMultiTenancy()'s default exists to be overridden for.
        services
            .AddSharedKernelEfCore<SoftDeletableTenantedDbContext>(options => options
                .UseSqlite(connection)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithDbContextPooling(poolSize: 1)
            .Build();

        var provider = services.BuildServiceProvider();

        using (var setupScope = provider.CreateScope())
        {
            var setupCtx = setupScope.ServiceProvider.GetRequiredService<SoftDeletableTenantedDbContext>();
            await setupCtx.Database.EnsureCreatedAsync();
        }

        var tenantAId = Guid.NewGuid();
        var userAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        SoftDeletableTenantedDbContext requestAInstance;

        // Act — Request A: scope 1, Tenant A / User A.
        using (var scopeA = provider.CreateScope())
        {
            ((MutableTestUserContext)scopeA.ServiceProvider.GetRequiredService<IUserContext>()).SubjectId = userAId.ToString("D");
            ((MutableTestTenantProvider)scopeA.ServiceProvider.GetRequiredService<ITenantProvider>()).TenantId = tenantAId;

            var ctxA = scopeA.ServiceProvider.GetRequiredService<SoftDeletableTenantedDbContext>();
            requestAInstance = ctxA;

            ctxA.SdAggregates.Add(new SoftDeletableTenantedAggregate(
                TenantedTestId.New(), "FromRequestA", tenantAId, new SystemClock()));
            await ctxA.SaveChangesAsync();
        }

        // Act — Request B: scope 2, Tenant B / User B. poolSize = 1 guarantees the same
        // underlying pooled instance leased to Request A is reused here (sequential, not
        // concurrent, usage — no second slot was ever needed).
        using (var scopeB = provider.CreateScope())
        {
            ((MutableTestUserContext)scopeB.ServiceProvider.GetRequiredService<IUserContext>()).SubjectId = userBId.ToString("D");
            ((MutableTestTenantProvider)scopeB.ServiceProvider.GetRequiredService<ITenantProvider>()).TenantId = tenantBId;

            var ctxB = scopeB.ServiceProvider.GetRequiredService<SoftDeletableTenantedDbContext>();

            ReferenceEquals(ctxB, requestAInstance).Should().BeTrue(
                "poolSize = 1 guarantees the same underlying pooled instance is reused across sequential scopes");

            ctxB.SdAggregates.Add(new SoftDeletableTenantedAggregate(
                TenantedTestId.New(), "FromRequestB", tenantBId, new SystemClock()));
            await ctxB.SaveChangesAsync();

            // Assert — Request B sees ONLY Tenant B's row through the (refreshed) tenant filter.
            var visibleRows = await ctxB.SdAggregates.ToListAsync();
            visibleRows.Should().ContainSingle(r => r.Name == "FromRequestB");
            visibleRows.Should().NotContain(r => r.Name == "FromRequestA");

            // Assert — CreatedBy is attributed to User B, never the stale User A.
            var addedRow = visibleRows.Single(r => r.Name == "FromRequestB");
            addedRow.CreatedBy.Should().Be(userBId.ToString("D"));
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
        // Expression.Constant(specificProviderObject, ...) design, the second instance would
        // silently see the FIRST instance's tenant filter forever. The corrected
        // Expression.Constant(this, GetType()) binding makes each instance see its OWN
        // TenantProvider despite sharing the cached model.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SoftDeletableTenantedDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        // First instance — TenantProvider A. Constructing and using it populates EF Core's
        // process-wide model cache for SoftDeletableTenantedDbContext.
        var tenantProviderA = TestDbContextFactory.CreateTenantProvider(tenantAId);
        using (var ctxA = new SoftDeletableTenantedDbContext(
            options,
            new AuditInterceptor(
                TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid()),
                new SystemClock(),
                TestDbContextFactory.DefaultServiceOptions()),
            new SoftDeleteInterceptor(
                TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid()),
                new SystemClock(),
                TestDbContextFactory.DefaultServiceOptions()),
            new ConcurrencyInterceptor(),
            tenantProviderA))
        {
            await ctxA.Database.EnsureCreatedAsync();
            ctxA.SdAggregates.Add(new SoftDeletableTenantedAggregate(
                TenantedTestId.New(), "TenantARow", tenantAId, new SystemClock()));
            await ctxA.SaveChangesAsync();
        }

        // Second instance — a DIFFERENT SoftDeletableTenantedDbContext instance, constructed
        // AFTER ctxA is disposed, with a DIFFERENT TenantProvider. Reuses EF Core's cached model.
        var tenantProviderB = TestDbContextFactory.CreateTenantProvider(tenantBId);
        using var ctxB = new SoftDeletableTenantedDbContext(
            options,
            new AuditInterceptor(
                TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid()),
                new SystemClock(),
                TestDbContextFactory.DefaultServiceOptions()),
            new SoftDeleteInterceptor(
                TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid()),
                new SystemClock(),
                TestDbContextFactory.DefaultServiceOptions()),
            new ConcurrencyInterceptor(),
            tenantProviderB);

        ctxB.SdAggregates.Add(new SoftDeletableTenantedAggregate(
            TenantedTestId.New(), "TenantBRow", tenantBId, new SystemClock()));
        await ctxB.SaveChangesAsync();

        // Assert — ctxB (bound to TenantProvider B) sees ONLY Tenant B's row, never Tenant A's.
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
    public void WithDbContextPooling_CombinedWithDbContextFactory_ThrowsAtBuild()
    {
        var services = new ServiceCollection();

        var act = () =>
            services
                .AddSharedKernelEfCore<TestDbContext>(options => options
                    .UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                .WithDbContextPooling()
                .WithDbContextFactory()
                .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WithDbContextFactory*");
    }

    [Fact]
    public void WithDbContextPooling_CombinedWithEncryption_ThrowsAtBuild()
    {
        var services = new ServiceCollection();

        var act = () =>
            services
                .AddSharedKernelEfCore<TestDbContext>(options => options
                    .UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                .WithDbContextPooling()
                .WithEncryption(o =>
                {
                    o.Enabled = true;
                    o.CurrentVersion = "v1";
                    o.Keys["v1"] = Convert.ToBase64String(new byte[32]);
                })
                .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WithEncryption*");
    }

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

        var descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(IDbContextFactory<TestDbContext>));
        descriptor.Should().NotBeNull("WithDbContextPooling() should register IDbContextFactory<TContext> via AddPooledDbContextFactory");
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
