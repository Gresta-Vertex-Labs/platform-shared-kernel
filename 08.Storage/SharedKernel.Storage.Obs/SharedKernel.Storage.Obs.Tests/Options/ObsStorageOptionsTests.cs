using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Storage.Obs.Extensions;
using SharedKernel.Storage.Obs.Options;

namespace SharedKernel.Storage.Obs.Tests.Options;

/// <summary>
/// T-15: <see cref="ObsStorageOptions"/> validation tests — mirrors
/// <c>SharedKernel.Storage.S3.Tests</c>' <c>S3StorageOptionsTests</c> (T-11). Valid config binds
/// without throw; missing <see cref="ObsStorageOptions.Endpoint"/>/<see cref="ObsStorageOptions.AccessKeyId"/>/
/// <see cref="ObsStorageOptions.SecretAccessKey"/> fails eagerly on first
/// <see cref="IOptions{TOptions}.Value"/> access — the same <c>ValidateDataAnnotations()</c>
/// mechanism <c>ValidateOnStart()</c> also triggers at <c>IHost.StartAsync()</c>.
/// </summary>
public sealed class ObsStorageOptionsTests
{
    [Fact]
    public void ValidConfig_BindsWithoutThrowing()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:Obs:Endpoint"] = "obs.ap-southeast-1.myhuaweicloud.com",
            ["SharedKernel:Storage:Obs:AccessKeyId"] = "access-key",
            ["SharedKernel:Storage:Obs:SecretAccessKey"] = "secret-key",
            ["SharedKernel:Storage:Obs:ForcePathStyle"] = "true",
        });

        var options = provider.GetRequiredService<IOptions<ObsStorageOptions>>().Value;

        options.Endpoint.Should().Be("obs.ap-southeast-1.myhuaweicloud.com");
        options.AccessKeyId.Should().Be("access-key");
        options.SecretAccessKey.Should().Be("secret-key");
        options.ForcePathStyle.Should().BeTrue();
    }

    [Fact]
    public void MissingEndpoint_ThrowsOptionsValidationException_OnFirstAccess()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:Obs:AccessKeyId"] = "access-key",
            ["SharedKernel:Storage:Obs:SecretAccessKey"] = "secret-key",
        });

        var act = () => provider.GetRequiredService<IOptions<ObsStorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void MissingAccessKeyId_ThrowsOptionsValidationException_OnFirstAccess()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:Obs:Endpoint"] = "obs.ap-southeast-1.myhuaweicloud.com",
            ["SharedKernel:Storage:Obs:SecretAccessKey"] = "secret-key",
        });

        var act = () => provider.GetRequiredService<IOptions<ObsStorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void MissingSecretAccessKey_ThrowsOptionsValidationException_OnFirstAccess()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:Obs:Endpoint"] = "obs.ap-southeast-1.myhuaweicloud.com",
            ["SharedKernel:Storage:Obs:AccessKeyId"] = "access-key",
        });

        var act = () => provider.GetRequiredService<IOptions<ObsStorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void SectionName_MatchesDocumentedConfigurationPath()
    {
        ObsStorageOptions.SectionName.Should().Be("SharedKernel:Storage:Obs");
    }

    private static IServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        services.AddSharedKernelObsStorage(configuration);

        return services.BuildServiceProvider();
    }
}
