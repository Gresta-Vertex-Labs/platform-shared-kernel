using SharedKernel.Application.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Application.Transactions;
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
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeOfType<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork>();
        scope.ServiceProvider.GetRequiredService<SharedKernel.Persistence.Abstractions.Coordination.IAmbientDbTransaction>()
            .Current.Should().BeNull("no transaction is open outside ExecuteInTransactionAsync");
    }

    [Fact]
    public void Build_TransientFaultRetry_IsCompatibleWithTransactions_DoesNotThrow()
    {
        // Arrange — the former retry-vs-transaction Build() guard is gone: every transaction runs
        // inside the execution strategy (ExecuteInTransactionAsync), so a retrying strategy can replay it.
        var services = new ServiceCollection();

        var act = () =>
            services
                .AddSharedKernelEfCore<TestDbContext>(options =>
                    options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                .WithTransientFaultRetry()
                .Build();

        // Assert
        act.Should().NotThrow();
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
    public Guid? TenantId => null;
    public ActorKind ActorKind => ActorKind.User;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
