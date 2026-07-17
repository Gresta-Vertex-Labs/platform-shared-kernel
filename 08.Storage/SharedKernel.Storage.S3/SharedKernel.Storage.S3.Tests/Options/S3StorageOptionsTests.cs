using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Storage.S3.Extensions;
using SharedKernel.Storage.S3.Options;

namespace SharedKernel.Storage.S3.Tests.Options;

/// <summary>
/// T-11: <see cref="S3StorageOptions"/> validation tests — valid config registers without throw;
/// missing <see cref="S3StorageOptions.AccessKeyId"/>/<see cref="S3StorageOptions.SecretAccessKey"/>
/// (and, via the custom <see cref="System.ComponentModel.DataAnnotations.IValidatableObject"/> rule,
/// a missing <see cref="S3StorageOptions.Region"/> when <see cref="S3StorageOptions.ServiceUrl"/> is
/// unset) fails eagerly on first <see cref="IOptions{TOptions}.Value"/> access — the same
/// <c>ValidateDataAnnotations()</c> mechanism <c>ValidateOnStart()</c> also triggers at
/// <c>IHost.StartAsync()</c>, so no web host is required to prove the failure mode.
/// </summary>
public sealed class S3StorageOptionsTests
{
    [Fact]
    public void ValidConfig_WithServiceUrl_BindsWithoutThrowing()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:S3:ServiceUrl"] = "http://localhost:9000",
            ["SharedKernel:Storage:S3:AccessKeyId"] = "access-key",
            ["SharedKernel:Storage:S3:SecretAccessKey"] = "secret-key",
            ["SharedKernel:Storage:S3:ForcePathStyle"] = "true",
        });

        var options = provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;

        options.ServiceUrl.Should().Be("http://localhost:9000");
        options.AccessKeyId.Should().Be("access-key");
        options.SecretAccessKey.Should().Be("secret-key");
        options.ForcePathStyle.Should().BeTrue();
    }

    [Fact]
    public void ValidConfig_WithRegion_InsteadOfServiceUrl_BindsWithoutThrowing()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:S3:Region"] = "eu-central-1",
            ["SharedKernel:Storage:S3:AccessKeyId"] = "access-key",
            ["SharedKernel:Storage:S3:SecretAccessKey"] = "secret-key",
        });

        var options = provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;

        options.Region.Should().Be("eu-central-1");
        options.ServiceUrl.Should().BeNull();
    }

    [Fact]
    public void MissingAccessKeyId_ThrowsOptionsValidationException_OnFirstAccess()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:S3:ServiceUrl"] = "http://localhost:9000",
            ["SharedKernel:Storage:S3:SecretAccessKey"] = "secret-key",
        });

        var act = () => provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void MissingSecretAccessKey_ThrowsOptionsValidationException_OnFirstAccess()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:S3:ServiceUrl"] = "http://localhost:9000",
            ["SharedKernel:Storage:S3:AccessKeyId"] = "access-key",
        });

        var act = () => provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void MissingRegion_WhenServiceUrlAlsoUnset_ThrowsOptionsValidationException()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:S3:AccessKeyId"] = "access-key",
            ["SharedKernel:Storage:S3:SecretAccessKey"] = "secret-key",
        });

        var act = () => provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(S3StorageOptions.Region)}*");
    }

    [Fact]
    public void SectionName_MatchesDocumentedConfigurationPath()
    {
        S3StorageOptions.SectionName.Should().Be("SharedKernel:Storage:S3");
    }

    private static IServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        services.AddSharedKernelS3Storage(configuration);

        return services.BuildServiceProvider();
    }
}
