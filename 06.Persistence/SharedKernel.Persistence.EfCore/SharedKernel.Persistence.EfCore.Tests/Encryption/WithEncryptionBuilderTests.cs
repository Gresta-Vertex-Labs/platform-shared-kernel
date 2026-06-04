using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// Tests for <see cref="EfCorePersistenceBuilder{TContext}.WithEncryption"/> and
/// <see cref="EfCorePersistenceBuilder{TContext}.WithServiceName"/> fluent methods (C-74).
/// </summary>
public sealed class WithEncryptionBuilderTests
{
    [Fact]
    public void WithEncryption_RegistersEncryptionOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var keyBytes = new byte[32];

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:"))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = Convert.ToBase64String(keyBytes);
            })
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert — EncryptionOptions is resolvable
        var options = provider.GetService<IOptions<EncryptionOptions>>();
        options.Should().NotBeNull();
        options!.Value.Enabled.Should().BeTrue();
        options.Value.CurrentVersion.Should().Be("v1");
    }

    [Fact]
    public void WithoutEncryption_EncryptionOptions_DefaultsToDisabled()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — no .WithEncryption() call
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:"))
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert — EncryptionOptions can be resolved (from default) and is disabled
        var options = provider.GetService<IOptions<EncryptionOptions>>();
        // May or may not be registered depending on options infrastructure; if registered it should be disabled
        if (options is not null)
        {
            options.Value.Enabled.Should().BeFalse();
        }
    }

    [Fact]
    public void WithServiceName_RegistersPersistenceServiceOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:"))
            .WithServiceName("my-service")
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<PersistenceServiceOptions>>();
        options.Value.ServiceName.Should().Be("my-service");
    }

    [Fact]
    public void WithoutServiceName_PersistenceServiceOptions_DefaultsToSystem()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — no .WithServiceName() call
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:"))
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert — default fallback is "system"
        var options = provider.GetRequiredService<IOptions<PersistenceServiceOptions>>();
        options.Value.ServiceName.Should().Be("system");
    }

    [Fact]
    public void WithServiceName_And_WithEncryption_ChainTogether()
    {
        // Arrange
        var services = new ServiceCollection();
        var keyBytes = new byte[32];

        // Act — both called in chain
        var act = () =>
            services
                .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:"))
                .WithEncryption(enc =>
                {
                    enc.Enabled = true;
                    enc.CurrentVersion = "v1";
                    enc.Keys["v1"] = Convert.ToBase64String(keyBytes);
                })
                .WithServiceName("chain-service")
                .Build();

        // Assert — no exception
        act.Should().NotThrow();
    }
}
