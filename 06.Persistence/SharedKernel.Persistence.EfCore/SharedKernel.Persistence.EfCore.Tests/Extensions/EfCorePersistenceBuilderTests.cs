using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

// Reuses the retry-forcing IExecutionStrategyFactory fixture already defined for
// PersistenceRetryDiagnosticListenerTests — see that file's own remarks. No fault injection is
// needed here (the tests below never call SaveChangesAsync against a failing operation), only the
// strategy's RetriesOnFailure=true reporting, so there is no need to join the shared
// "RetryDiagnostics" xUnit collection those other tests use.
using SharedKernel.Persistence.EfCore.Tests.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Tests.Extensions;

public sealed class EfCorePersistenceBuilderTests
{
    private static Action<DbContextOptionsBuilder> SqliteOptions() =>
        options => options.UseSqlite("DataSource=:memory:")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning));

    [Fact]
    public void Build_CalledTwice_ForTheSameContextType_ThrowsInvalidOperationException()
    {
        // Idempotency guard: RekeyLastRegistrationAsInner re-keys the LAST IDbContextFactory<TContext>
        // registration into a private, keyed slot. A second Build() call for the same TContext — direct
        // repeat call, or a second, independently-constructed AddSharedKernelEfCore<TContext>(...)
        // builder — would re-key the ALREADY-WRAPPING TenantAwareDbContextFactory<TContext> a second
        // time, producing a factory that resolves itself and stack-overflows the first time anything
        // asks for it. This must be rejected loudly instead.
        var services = new ServiceCollection();

        services.AddSharedKernelEfCore<TestDbContext>(SqliteOptions()).Build();

        var act = () => services.AddSharedKernelEfCore<TestDbContext>(SqliteOptions()).Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*was already called*")
                .Which.Message.Should().Contain(nameof(TestDbContext));
    }

    [Fact]
    public void Build_CalledTwice_ForTheSameContextType_NeverStackOverflowsResolvingTheFactory()
    {
        // Regression proof for the failure mode the guard above exists to prevent: without the guard,
        // a second Build() call would re-key an already-keyed registration, and resolving
        // IDbContextFactory<TestDbContext> afterward would recurse into a StackOverflowException
        // (unrecoverable — the process would crash, not throw a catchable exception). Proving the
        // guard actually fires (the test above) is what keeps this scenario from ever being reached;
        // this test only re-confirms the first Build() call alone still resolves cleanly.
        var services = new ServiceCollection();
        services.AddSharedKernelEfCore<TestDbContext>(SqliteOptions()).Build();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<TestDbContext>>();

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_TwoDifferentContextTypes_SecondCallThrowsInvalidOperationException()
    {
        // Multi-context guard: EfUnitOfWork resolves the unkeyed SharedKernelDbContext/IUnitOfWork
        // services. Two different TContext types sharing one IServiceCollection must never both
        // register them, or the second Build() call would silently win the unkeyed slot and every
        // IUnitOfWork/SharedKernelDbContext consumer would bind to whichever context registered last,
        // regardless of which repository/context it actually intended to commit through.
        var services = new ServiceCollection();

        services.AddSharedKernelEfCore<TestDbContext>(SqliteOptions()).Build();

        var act = () => services.AddSharedKernelEfCore<StringIncludeDbContext>(SqliteOptions()).Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unkeyed*")
                .Which.Message.Should().Contain(nameof(StringIncludeDbContext));
    }

    [Fact]
    public void Build_TwoDifferentContextTypes_FirstContextsUnitOfWorkStillResolvesToItself()
    {
        // Positive counterpart of the guard above: a single-context registration's IUnitOfWork must
        // resolve to THAT context, proving the guard is not merely rejecting every multi-Build()
        // scenario indiscriminately — only the actually-unsafe second registration.
        var services = new ServiceCollection();
        services.AddSharedKernelEfCore<TestDbContext>(SqliteOptions()).Build();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var sharedKernelContext = scope.ServiceProvider.GetRequiredService<SharedKernelDbContext>();

        sharedKernelContext.Should().BeOfType<TestDbContext>();
    }

    [Fact]
    public void Build_WithMultiTenancy_ButNonTenantedContext_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () =>
            services
                .AddSharedKernelEfCore<TestDbContext>(options =>
                    options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                        .WithMultiTenancy()
                            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WithMultiTenancy*")
                .Which.Message.Should().Contain("TenantedDbContext");
    }

    [Fact]
    public void Build_TransientFaultRetry_WithTransactionalUnitOfWork_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — EF Core forbids beginning a caller-owned transaction under a retrying execution
        // strategy, so this pairing would throw on every BeginTransactionAsync at runtime. Since
        // 13.ServiceDefaults.Persistence's WithApplicationTransactionBehavior routes every audited
        // command through that call, the failure would land on each command in production rather
        // than once at startup.
        var act = () =>
            services
                .AddSharedKernelEfCore<TestDbContext>(options =>
                    options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                .WithTransientFaultRetry()
                .WithTransactionalUnitOfWork()
                .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WithTransientFaultRetry*")
            .Which.Message.Should().Contain("ExecuteInTransactionAsync",
                "the error must name the retry-safe alternative, not just refuse the combination");
    }

    [Fact]
    public void Build_TransactionalUnitOfWork_WithoutRetry_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — the transactional unit of work on its own stays fully supported; only the pairing
        // with a retrying execution strategy is rejected.
        var act = () =>
            services
                .AddSharedKernelEfCore<TestDbContext>(options =>
                    options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                .WithTransactionalUnitOfWork()
                .Build();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Build_TransactionalUnitOfWork_WithRetryEnabledOutsideWithTransientFaultRetry_HostedServiceThrowsAtStartup()
    {
        // Arrange — a retrying execution strategy configured directly on the DbContextOptionsBuilder,
        // standing in for the PostgreSQL package's UsePostgreSQL(..., maxRetryCount:...) (Npgsql's own
        // retry, configured inside the configureDb delegate this package never inspects), WITHOUT ever
        // calling '.WithTransientFaultRetry()'. Build_TransientFaultRetry_WithTransactionalUnitOfWork_
        // ThrowsInvalidOperationException above proves the eager Build()-time guard; this proves the
        // PersistenceContextWiringValidator startup safety net that catches what that guard cannot see.
        var services = new ServiceCollection();

        services
            .AddSharedKernelEfCore<TestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
                .ReplaceService<Microsoft.EntityFrameworkCore.Storage.IExecutionStrategyFactory, AlwaysRetryStrategyFactory>())
            .WithTransactionalUnitOfWork()
            .Build();

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        hostedServices.Should().ContainSingle(hs => hs is PersistenceContextWiringValidator<TestDbContext>);

        // Act
        var act = async () =>
        {
            foreach (var hostedService in hostedServices)
                await hostedService.StartAsync(CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*retrying execution strategy*"))
            .Which.Message.Should().Contain("WithTransactionalUnitOfWork",
                "the error must name the offending combination, not just that something is wrong");
    }

    [Fact]
    public async Task Build_TransactionalUnitOfWork_WithoutRetry_HostedServiceStartsCleanly()
    {
        // Arrange — the common, default case: '.WithTransactionalUnitOfWork()' alone, no retrying
        // execution strategy configured anywhere. Proves the new startup check above does not produce
        // a false positive for every ordinary transactional-unit-of-work consumer.
        var services = new ServiceCollection();

        services
            .AddSharedKernelEfCore<TestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithTransactionalUnitOfWork()
            .Build();

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        hostedServices.Should().ContainSingle(hs => hs is PersistenceContextWiringValidator<TestDbContext>);

        // Act
        var act = async () =>
        {
            foreach (var hostedService in hostedServices)
                await hostedService.StartAsync(CancellationToken.None);
        };

        // Assert
        await act.Should().NotThrowAsync();
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

        // Placeholder ICurrentActorContext: AnonymousActorContext, resolves to the configured
        // (or default "system") service name — the IUserContext placeholder is gone.
        var actorCtx = scope.ServiceProvider.GetService<ICurrentActorContext>();
        actorCtx.Should().NotBeNull();
        actorCtx.Should().BeOfType<AnonymousActorContext>();
        actorCtx!.ActorId.Should().Be("system");
        actorCtx.ActorKind.Should().Be(ActorKind.System);
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

        // Assert — without a real tenant context the default (NullCurrentTenantContext)
        // resolves TenantId as null (fail-closed — no Guid.Empty sentinel any more).
        using var scope = provider.CreateScope();
        var tenantContext = scope.ServiceProvider.GetService<ICurrentTenantContext>();
        tenantContext.Should().NotBeNull();
        tenantContext!.TenantId.Should().BeNull();
    }

    [Fact]
    public void Build_WithExistingActorContext_DoesNotOverrideIt()
    {
        // Arrange
        var services = new ServiceCollection();
        var customId = Guid.NewGuid();

        // Register a custom ICurrentActorContext first
        services.AddScoped<ICurrentActorContext>(_ => new CustomActorContext(customId));

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                    .Build();

        var provider = services.BuildServiceProvider();

        // Assert — the custom one should win (Build() checks "if not already registered")
        using var scope = provider.CreateScope();
        var actorCtx = scope.ServiceProvider.GetService<ICurrentActorContext>();
        actorCtx.Should().NotBeNull();
        actorCtx!.ActorId.Should().Be(customId.ToString("D"));
        actorCtx.Should().NotBeOfType<AnonymousActorContext>();
    }
}

// ---------------------------------------------------------------------------
// Test helper
// ---------------------------------------------------------------------------

internal sealed class CustomActorContext(Guid userId) : ICurrentActorContext
{
    public string ActorId { get; } = userId.ToString("D");
    public ActorKind ActorKind => ActorKind.User;
}
