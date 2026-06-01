using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
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
                    options.UseSqlite("DataSource=:memory:"))
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
                    options.UseSqlite("DataSource=:memory:"))
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
                options.UseSqlite("DataSource=:memory:"))
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var uow = scope.ServiceProvider.GetService<IUnitOfWork>();
        uow.Should().NotBeNull();

        var specEval = scope.ServiceProvider.GetService(typeof(ISpecificationEvaluator<TestAggregate>));
        specEval.Should().NotBeNull();

        var userCtx = scope.ServiceProvider.GetService<IUserContext>();
        userCtx.Should().NotBeNull();
        userCtx!.UserId.Should().Be("system"); // no-op placeholder
    }

    [Fact]
    public void Build_WithExistingUserContext_DoesNotOverrideIt()
    {
        // Arrange
        var services = new ServiceCollection();

        // Register a custom IUserContext first
        services.AddScoped<IUserContext>(_ => new CustomUserContext("custom-user"));

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:"))
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert — the custom one should win (it was registered first)
        using var scope = provider.CreateScope();
        var userCtx = scope.ServiceProvider.GetService<IUserContext>();
        // The default DI behaviour returns the LAST registered service, so "system" would win
        // unless we check this explicitly. We verify the no-op placeholder does NOT add
        // when one already exists.
        // Actually in MS DI the last registration wins, but our code checks "if not already registered"
        // so the custom one should remain.
        userCtx.Should().NotBeNull();
        userCtx!.UserId.Should().Be("custom-user");
    }

    [Fact]
    public void Build_MultiTenancy_RegistersNoOpCurrentTenantService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TenantedTestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:"))
            .WithMultiTenancy()
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var tenantService = scope.ServiceProvider.GetService<ICurrentTenantService>();
        tenantService.Should().NotBeNull();
        tenantService!.TenantId.Should().BeNull(); // no-op returns null
    }
}

// ---------------------------------------------------------------------------
// Test helper
// ---------------------------------------------------------------------------

internal sealed class CustomUserContext(string userId) : IUserContext
{
    public string UserId { get; } = userId;
}
