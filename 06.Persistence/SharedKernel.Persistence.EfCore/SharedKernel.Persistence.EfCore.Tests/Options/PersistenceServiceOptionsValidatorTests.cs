using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// Tests for <see cref="PersistenceServiceOptions"/> validation registered via
/// <see cref="EfCorePersistenceBuilder{TContext}.WithServiceName"/>.
/// Validation is tested through the DI container's <c>IOptions</c> system.
/// </summary>
public sealed class PersistenceServiceOptionsValidatorTests
{
    [Fact]
    public void Default_ServiceName_System_Resolves_Successfully()
    {
        // Arrange — no WithServiceName call; defaults to "system"
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                .Build();
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<PersistenceServiceOptions>>();

        // Assert
        options.Value.ServiceName.Should().Be("system");
    }

    [Fact]
    public void Custom_ServiceName_Resolves_Correctly()
    {
        // Arrange
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithServiceName("payment-service")
            .Build();
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<PersistenceServiceOptions>>();

        // Assert
        options.Value.ServiceName.Should().Be("payment-service");
    }

    [Fact]
    public void ServiceName_MaxLength_256_Resolves_Successfully()
    {
        // Arrange — exactly 256 characters
        var serviceName = new string('x', 256);
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithServiceName(serviceName)
            .Build();
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<PersistenceServiceOptions>>();

        // Assert
        options.Value.ServiceName.Should().HaveLength(256);
    }
}
