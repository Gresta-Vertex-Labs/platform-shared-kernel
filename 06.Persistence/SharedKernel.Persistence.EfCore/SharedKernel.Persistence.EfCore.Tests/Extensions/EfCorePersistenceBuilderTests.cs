using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Extensions;

public sealed class EfCorePersistenceBuilderTests
{
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
