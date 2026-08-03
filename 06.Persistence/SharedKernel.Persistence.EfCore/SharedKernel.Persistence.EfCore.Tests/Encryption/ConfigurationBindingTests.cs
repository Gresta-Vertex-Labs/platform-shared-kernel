using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// WO-053/P-333/P-334 (C-135/C-136/C-137): <see cref="EncryptionOptions.SectionName"/>/
/// <see cref="PersistenceServiceOptions.SectionName"/> constants and the
/// <see cref="EfCorePersistenceBuilder{TContext}.WithEncryption(IConfiguration, Action{EncryptionOptions}?)"/>/
/// <see cref="EfCorePersistenceBuilder{TContext}.WithServiceName(IConfiguration)"/>
/// configuration-binding overloads.
/// </summary>
public sealed class ConfigurationBindingTests
{
    [Fact]
    public void EncryptionOptions_SectionName_HasExpectedValue()
    {
        EncryptionOptions.SectionName.Should().Be("SharedKernel:Encryption");
    }

    [Fact]
    public void PersistenceServiceOptions_SectionName_HasExpectedValue()
    {
        PersistenceServiceOptions.SectionName.Should().Be("SharedKernel:Persistence");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> data) =>
        new ConfigurationBuilder().AddInMemoryCollection(data).Build();

    [Fact]
    public void WithEncryption_IConfigurationOverload_BindsFromSectionName()
    {
        // Arrange
        var keyBytes = new byte[32];
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SharedKernel:Encryption:Enabled"] = "true",
            ["SharedKernel:Encryption:CurrentVersion"] = "v1",
            ["SharedKernel:Encryption:Keys:v1"] = Convert.ToBase64String(keyBytes),
        });

        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configuration)
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<EncryptionOptions>>();
        options.Value.Enabled.Should().BeTrue();
        options.Value.CurrentVersion.Should().Be("v1");
        options.Value.Keys.Should().ContainKey("v1");
    }

    [Fact]
    public void WithEncryption_IConfigurationOverload_ComposesWithCodeBasedConfigure()
    {
        // Arrange — configuration supplies CurrentVersion; the code-based configure layers ServiceName-
        // unrelated additional key material on top, proving both paths compose under normal
        // IOptions<T> later-registration-wins semantics.
        var v1Key = new byte[32];
        var v2Key = new byte[32];
        Array.Fill(v2Key, (byte)0x7);

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SharedKernel:Encryption:Enabled"] = "true",
            ["SharedKernel:Encryption:CurrentVersion"] = "v1",
            ["SharedKernel:Encryption:Keys:v1"] = Convert.ToBase64String(v1Key),
        });

        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configuration, enc => enc.Keys["v2"] = Convert.ToBase64String(v2Key))
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<EncryptionOptions>>();
        options.Value.CurrentVersion.Should().Be("v1");
        options.Value.Keys.Should().ContainKey("v1");
        options.Value.Keys.Should().ContainKey("v2");
    }

    [Fact]
    public void WithEncryption_IConfigurationOverload_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));

        Action act = () => builder.WithEncryption((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithServiceName_IConfigurationOverload_BindsFromSectionName()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SharedKernel:Persistence:ServiceName"] = "order-service",
        });

        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithServiceName(configuration)
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<PersistenceServiceOptions>>();
        options.Value.ServiceName.Should().Be("order-service");
    }

    [Fact]
    public void WithServiceName_IConfigurationOverload_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));

        Action act = () => builder.WithServiceName((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithServiceName_StringThenIConfiguration_LaterRegistrationWins()
    {
        // Arrange — string overload first, IConfiguration overload second: later wins.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SharedKernel:Persistence:ServiceName"] = "from-configuration",
        });

        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithServiceName("from-code")
            .WithServiceName(configuration)
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<PersistenceServiceOptions>>();
        options.Value.ServiceName.Should().Be("from-configuration");
    }

    [Fact]
    public void WithEncryption_IConfigurationOverload_StartupValidation_FiresAgainstInvalidBoundShape()
    {
        // Arrange — Enabled=true but no CurrentVersion/Keys bound at all.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SharedKernel:Encryption:Enabled"] = "true",
        });

        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configuration)
            .Build();

        var provider = services.BuildServiceProvider();

        // Act
        Action act = () => _ = provider.GetRequiredService<IOptions<EncryptionOptions>>().Value;

        // Assert — ValidateOnStart-style eager validation rejects the incomplete shape.
        act.Should().Throw<OptionsValidationException>();
    }

    // -------------------------------------------------------------------------
    // T-104 (WO-053/P-334, D-92) — composition precedence: a later code-override call wins over
    // an earlier configuration-binding call, per normal IOptions<T> layering semantics. Reversing
    // the call order reverses precedence — this is documented IOptions<T> behavior, not a fixed
    // rule this domain enforces.
    // -------------------------------------------------------------------------

    [Fact]
    public void WithEncryption_ConfigurationThenCodeOverride_LaterCodeCallWins()
    {
        // Arrange — configuration binds CurrentVersion = "v1"; a SEPARATE, later .WithEncryption(...)
        // call (the Action<T>-only overload) overrides CurrentVersion to "v2".
        var v1Key = new byte[32];
        var v2Key = new byte[32];
        Array.Fill(v2Key, (byte)0x9);

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SharedKernel:Encryption:Enabled"] = "true",
            ["SharedKernel:Encryption:CurrentVersion"] = "v1",
            ["SharedKernel:Encryption:Keys:v1"] = Convert.ToBase64String(v1Key),
        });

        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configuration)
            .WithEncryption(configure: enc =>
            {
                enc.CurrentVersion = "v2";
                enc.Keys["v2"] = Convert.ToBase64String(v2Key);
            })
            .Build();

        var provider = services.BuildServiceProvider();

        // Assert — the LATER call (code-only override) wins.
        var options = provider.GetRequiredService<IOptions<EncryptionOptions>>();
        options.Value.CurrentVersion.Should().Be("v2");
        options.Value.Keys.Should().ContainKey("v1");
        options.Value.Keys.Should().ContainKey("v2");
    }

    // -------------------------------------------------------------------------
    // T-105 (WO-053/P-334, C-136/C-137) — startup validation still fires (and fires exactly once)
    // for a config-bound invalid shape, identically to the pre-existing Action<T>-based path.
    // -------------------------------------------------------------------------

    [Fact]
    public void WithEncryption_IConfigurationOverload_CurrentVersionNotInKeys_ThrowsSameValidationExceptionAsCodeOverload()
    {
        // Arrange — configuration-bound path: CurrentVersion references a key absent from Keys.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SharedKernel:Encryption:Enabled"] = "true",
            ["SharedKernel:Encryption:CurrentVersion"] = "v2",
            ["SharedKernel:Encryption:Keys:v1"] = Convert.ToBase64String(new byte[32]),
        });

        var configServices = new ServiceCollection();
        configServices
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configuration)
            .Build();
        var configProvider = configServices.BuildServiceProvider();

        // The IDENTICAL invalid shape, bound purely via the pre-existing Action<T>-only overload.
        var codeServices = new ServiceCollection();
        codeServices
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v2";
                enc.Keys["v1"] = Convert.ToBase64String(new byte[32]);
            })
            .Build();
        var codeProvider = codeServices.BuildServiceProvider();

        // Act
        Action configAct = () => _ = configProvider.GetRequiredService<IOptions<EncryptionOptions>>().Value;
        Action codeAct = () => _ = codeProvider.GetRequiredService<IOptions<EncryptionOptions>>().Value;

        // Assert — both throw the SAME eager startup-validation exception type for the identical
        // invalid shape (CurrentVersion not present in Keys).
        var configException = configAct.Should().Throw<OptionsValidationException>().Which;
        var codeException = codeAct.Should().Throw<OptionsValidationException>().Which;
        configException.Failures.Should().NotBeEmpty();
        codeException.Failures.Should().NotBeEmpty();
    }

    [Fact]
    public void WithEncryption_ConfigurationThenCodeOverride_RegistersValidatorExactlyOnce_NeverDuplicated()
    {
        // Arrange — chain BOTH the configuration-binding overload and a code-override call in the
        // same builder; the eager validation registration performed by
        // EnsureEncryptionInfrastructureRegistered() must be idempotent across both calls.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SharedKernel:Encryption:Enabled"] = "true",
            ["SharedKernel:Encryption:CurrentVersion"] = "v1",
            ["SharedKernel:Encryption:Keys:v1"] = Convert.ToBase64String(new byte[32]),
        });

        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configuration)
            .WithEncryption(configure: enc => enc.Keys["v2"] = Convert.ToBase64String(new byte[32]))
            .Build();

        // Assert — exactly one IValidateOptions<EncryptionOptions> registration exists, never one
        // per .WithEncryption(...) call chained onto the same builder.
        services.Count(d => d.ServiceType == typeof(IValidateOptions<EncryptionOptions>)).Should().Be(1);

        // And resolving the (valid, in this case) options succeeds without throwing — validation
        // ran, found nothing wrong, and did not fire twice/produce duplicated failures.
        var provider = services.BuildServiceProvider();
        Action act = () => _ = provider.GetRequiredService<IOptions<EncryptionOptions>>().Value;
        act.Should().NotThrow();
    }
}
