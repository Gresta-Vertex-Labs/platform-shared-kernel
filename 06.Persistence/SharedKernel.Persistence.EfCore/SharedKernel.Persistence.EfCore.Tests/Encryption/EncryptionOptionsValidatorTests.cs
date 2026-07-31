using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// Tests for <see cref="EncryptionOptions"/> validation registered via
/// <see cref="EfCorePersistenceBuilder{TContext}.WithEncryption"/>.
/// Validation is tested through the DI container's <c>IOptions</c> system.
/// </summary>
public sealed class EncryptionOptionsValidatorTests
{
    private static IOptions<EncryptionOptions> BuildOptions(Action<EncryptionOptions> configure)
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configure)
            .Build();

        return services.BuildServiceProvider().GetRequiredService<IOptions<EncryptionOptions>>();
    }

    [Fact]
    public void Disabled_Options_Resolves_Without_Validation_Failure()
    {
        // Arrange — Enabled == false; no keys or version needed
        var options = BuildOptions(enc => { enc.Enabled = false; });

        // Act / Assert — should resolve without exception
        var act = () => _ = options.Value;
        act.Should().NotThrow("disabled options have no validation requirements");
    }

    [Fact]
    public void Enabled_WithValidConfig_Resolves_Successfully()
    {
        // Arrange
        var keyBytes = new byte[32];
        var options = BuildOptions(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "v1";
            enc.Keys["v1"] = Convert.ToBase64String(keyBytes);
        });

        // Act / Assert
        var act = () => _ = options.Value;
        act.Should().NotThrow();
    }

    [Fact]
    public void Disabled_Options_With_No_Keys_Is_Valid()
    {
        // Arrange — common case: encryption not yet turned on
        var options = BuildOptions(enc => enc.Enabled = false);

        // Act
        var value = options.Value;

        // Assert
        value.Enabled.Should().BeFalse();
        value.Keys.Should().BeEmpty();
    }

    [Fact]
    public void EncryptionOptions_HoldsCorrectValues_AfterConfigure()
    {
        // Arrange
        var keyBytes = new byte[32];
        var base64Key = Convert.ToBase64String(keyBytes);

        var options = BuildOptions(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "v1";
            enc.Keys["v1"] = base64Key;
        });

        // Act
        var value = options.Value;

        // Assert
        value.Enabled.Should().BeTrue();
        value.CurrentVersion.Should().Be("v1");
        value.Keys.Should().ContainKey("v1").WhoseValue.Should().Be(base64Key);
    }
}
