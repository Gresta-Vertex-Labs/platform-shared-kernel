using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Transactions;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

// Reuses the retry-forcing IExecutionStrategyFactory fixture already defined for
// PersistenceRetryDiagnosticListenerTests — see that file's own remarks. No fault injection is
// needed here (the tests below never call SaveChangesAsync against a failing operation), only the
// strategy's RetriesOnFailure=true reporting, so there is no need to join the shared
// "RetryDiagnostics" xUnit collection those other tests use.
using SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

namespace SharedKernel.Persistence.EfCore.Tests.Extensions;

public sealed class EfCorePersistenceBuilderTests
{
    private static Action<DbContextOptionsBuilder> SqliteOptions() =>
        options => options.UseSqlite("DataSource=:memory:")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning));

    [Fact]
    public void Register_SameContextTypeTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelEfCore<TestDbContext>(SqliteOptions()).Build();

        var act = () => services.AddSharedKernelEfCore<TestDbContext>(SqliteOptions()).Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    [Fact]
    public void Register_TwoContexts_EachGetsItsOwnUnitOfWork_UnkeyedIsTheFirst()
    {
        // Multiple contexts per service: the former "second TContext throws" guard is gone.
        var services = new ServiceCollection();
        services.AddSharedKernelEfCore<TestDbContext>(SqliteOptions()).Build();
        services.AddSharedKernelEfCore<StringIncludeDbContext>(SqliteOptions()).Build();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IUnitOfWork>().Should().BeOfType<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork<TestDbContext>>();
        sp.GetRequiredService<SharedKernelDbContext>().Should().BeOfType<TestDbContext>();

        sp.GetRequiredService<SharedKernel.Persistence.EfCore.UnitOfWork.IUnitOfWork<StringIncludeDbContext>>()
            .Should().BeOfType<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork<StringIncludeDbContext>>();
        sp.GetRequiredKeyedService<IUnitOfWork>(typeof(StringIncludeDbContext))
            .Should().BeSameAs(sp.GetRequiredService<SharedKernel.Persistence.EfCore.UnitOfWork.IUnitOfWork<StringIncludeDbContext>>());
        sp.GetRequiredKeyedService<SharedKernelDbContext>(typeof(StringIncludeDbContext))
            .Should().BeSameAs(sp.GetRequiredService<StringIncludeDbContext>());
        sp.GetRequiredKeyedService<IUnitOfWork>(typeof(TestDbContext))
            .Should().BeSameAs(sp.GetRequiredService<IUnitOfWork>());
    }

    [Fact]
    public async Task Register_TwoContexts_EachUnitOfWorkCommitsItsOwnContext()
    {
        var services = new ServiceCollection();
        var first = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        var second = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services.AddSharedKernelEfCore<TestDbContext>(o => o.UseSqlite(first)).Build();
        services.AddSharedKernelEfCore<StringIncludeDbContext>(o => o.UseSqlite(second)).Build();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var testDb = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var otherDb = scope.ServiceProvider.GetRequiredService<StringIncludeDbContext>();
        await testDb.Database.OpenConnectionAsync();
        await testDb.Database.EnsureCreatedAsync();
        await otherDb.Database.OpenConnectionAsync();
        await otherDb.Database.EnsureCreatedAsync();

        testDb.TestAggregates.Add(new TestAggregate(TestId.New(), "first", new SharedKernel.Primitives.Clocks.SystemClock()));
        await scope.ServiceProvider.GetRequiredService<SharedKernel.Persistence.EfCore.UnitOfWork.IUnitOfWork<StringIncludeDbContext>>().SaveChangesAsync();
        testDb.ChangeTracker.HasChanges().Should().BeTrue("the other context's unit of work must not commit this context");

        await scope.ServiceProvider.GetRequiredService<SharedKernel.Persistence.EfCore.UnitOfWork.IUnitOfWork<TestDbContext>>().SaveChangesAsync();
        testDb.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Fact]
    public void Build_AlwaysRegistersTheOneUnitOfWork_AndTheAmbientTransaction()
    {
        // Arrange — P-558: there is no '.WithTransactionalUnitOfWork()' any more; the single
        // EfUnitOfWork (retry-safe ExecuteInTransactionAsync, pre-commit hook) is always registered.
        var services = new ServiceCollection();

        services
            .AddSharedKernelEfCore<TestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Assert
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeOfType<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork<TestDbContext>>();
        scope.ServiceProvider.GetRequiredService<SharedKernel.Persistence.Abstractions.Coordination.IAmbientDbTransaction>()
            .Current.Should().BeNull("no transaction is open outside ExecuteInTransactionAsync");
    }

    [Fact]
    public void Build_AlwaysRegistersThePostgreSqlExceptionClassifier_First_AndOnlyOnce()
    {
        // P-558: SQLSTATE classification is part of the setup itself — there is no path without it.
        var services = new ServiceCollection();
        services.AddSingleton<SharedKernel.Persistence.EfCore.Extensibility.IDbUpdateExceptionClassifier, NoOpClassifier>();

        services
            .AddSharedKernelEfCore<TestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        using var provider = services.BuildServiceProvider();
        var classifiers = provider.GetServices<SharedKernel.Persistence.EfCore.Extensibility.IDbUpdateExceptionClassifier>().ToList();

        classifiers.Should().HaveCount(2);
        classifiers[0].Should().BeOfType<SharedKernel.Persistence.EfCore.Exceptions.PostgresDbUpdateExceptionClassifier>();
    }

    private sealed class NoOpClassifier : SharedKernel.Persistence.EfCore.Extensibility.IDbUpdateExceptionClassifier
    {
        public Exception? TryClassify(DbUpdateException exception) => null;
    }

    [Fact]
    public void Build_WithMultiTenancy_AndTenantedContext_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — TenantedTestDbContext extends TenantedDbContext
        var act = () =>
            services
                .AddSharedKernelEfCore<TenantedTestDbContext>(options =>
                    options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                        .WithMultiTenancy()
                            .Build();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_SingleTenant_RegistersCoreServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                    .Build();

        var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var uow = scope.ServiceProvider.GetService<IUnitOfWork>();
        uow.Should().NotBeNull();

        var specEval = scope.ServiceProvider.GetService(typeof(ISpecificationEvaluator<TestAggregate>));
        specEval.Should().NotBeNull();

        // Default IRequestContext: the fail-closed AnonymousRequestContext (no user, no tenant) —
        // audit columns then fall back to the configured (default "system") service name.
        var actorCtx = scope.ServiceProvider.GetService<IRequestContext>();
        actorCtx.Should().NotBeNull();
        actorCtx.Should().BeSameAs(AnonymousRequestContext.Instance);
        actorCtx!.UserId.Should().BeNull();
        actorCtx.ActorKind.Should().Be(ActorKind.Anonymous);
    }

    [Fact]
    public void Build_MultiTenancy_DefaultTenantContext_ResolvesNullTenant()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TenantedTestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                    .WithMultiTenancy()
                        .Build();

        var provider = services.BuildServiceProvider();

        // Assert — without a real request context the default (AnonymousRequestContext)
        // resolves TenantId as null (fail-closed — no Guid.Empty sentinel any more).
        using var scope = provider.CreateScope();
        var tenantContext = scope.ServiceProvider.GetService<IRequestContext>();
        tenantContext.Should().NotBeNull();
        tenantContext!.TenantId.Should().BeNull();
    }

    [Fact]
    public void Build_WithExistingActorContext_DoesNotOverrideIt()
    {
        // Arrange
        var services = new ServiceCollection();
        var customId = Guid.NewGuid();

        // Register a custom IRequestContext first
        services.AddScoped<IRequestContext>(_ => new CustomActorContext(customId));

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                    .Build();

        var provider = services.BuildServiceProvider();

        // Assert — the custom one should win (Build() checks "if not already registered")
        using var scope = provider.CreateScope();
        var actorCtx = scope.ServiceProvider.GetService<IRequestContext>();
        actorCtx.Should().NotBeNull();
        actorCtx!.UserId.Should().Be(customId.ToString("D"));
        actorCtx.Should().NotBeSameAs(AnonymousRequestContext.Instance);
    }
}

// ---------------------------------------------------------------------------
// Test helper
// ---------------------------------------------------------------------------

internal sealed class CustomActorContext(Guid userId) : IRequestContext
{
    public bool IsAuthenticated => true;
    public string? UserId { get; } = userId.ToString("D");
    public TenantId? TenantId => null;
    public ActorKind ActorKind => ActorKind.User;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
