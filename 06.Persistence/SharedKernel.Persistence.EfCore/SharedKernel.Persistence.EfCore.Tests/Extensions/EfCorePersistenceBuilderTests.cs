using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Security.Abstractions.Abstractions;

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

        // No-op IUserContext: IsAuthenticated=false, UserId=Guid.Empty
        var userCtx = scope.ServiceProvider.GetService<IUserContext>();
        userCtx.Should().NotBeNull();
        userCtx!.IsAuthenticated.Should().BeFalse();
        userCtx.UserId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Build_MultiTenancy_RegistersNoOpTenantProvider()
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

        // Assert — P-092: NoOpTenantProvider returns Guid.Empty
        using var scope = provider.CreateScope();
        var tenantProvider = scope.ServiceProvider.GetService<ITenantProvider>();
        tenantProvider.Should().NotBeNull();
        tenantProvider!.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Build_WithExistingUserContext_DoesNotOverrideIt()
    {
        // Arrange
        var services = new ServiceCollection();
        var customId = Guid.NewGuid();

        // Register a custom IUserContext first
        services.AddScoped<IUserContext>(_ => new CustomUserContext(customId));

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(options =>
                options.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert — the custom one should win (Build() checks "if not already registered")
        using var scope = provider.CreateScope();
        var userCtx = scope.ServiceProvider.GetService<IUserContext>();
        userCtx.Should().NotBeNull();
        userCtx!.UserId.Should().Be(customId);
        userCtx.IsAuthenticated.Should().BeTrue();
    }
}

// ---------------------------------------------------------------------------
// Test helper
// ---------------------------------------------------------------------------

internal sealed class CustomUserContext(Guid userId) : IUserContext
{
    public Guid UserId { get; } = userId;
    public string? Email => null;
    public string? Username => null;
    public IReadOnlyCollection<string> Roles => [];
    public IReadOnlyCollection<string> Permissions => [];
    public IReadOnlyDictionary<string, string> Claims => new Dictionary<string, string>();
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
