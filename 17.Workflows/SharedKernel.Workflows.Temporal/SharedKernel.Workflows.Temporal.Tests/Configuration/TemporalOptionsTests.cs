using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Workflows.Temporal.Configuration;

namespace SharedKernel.Workflows.Temporal.Tests.Configuration;

/// <summary>
/// T-06 — <see cref="TemporalOptions"/> validation: valid config binds; a missing
/// <see cref="TemporalOptions.TargetHost"/>/<see cref="TemporalOptions.Namespace"/> fails at startup.
/// Resolving <c>IOptions&lt;TemporalOptions&gt;.Value</c> directly is sufficient to trigger
/// <c>ValidateDataAnnotations()</c> — no <c>IHost</c> needed.
/// </summary>
public sealed class TemporalOptionsTests
{
    private static IOptions<TemporalOptions> BuildOptions(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddValidatedOptions<TemporalOptions>(configuration.GetSection(TemporalOptions.SectionName));
        return services.BuildServiceProvider().GetRequiredService<IOptions<TemporalOptions>>();
    }

    [Fact]
    public void ValidConfig_Binds_AndResolvesWithoutThrowing()
    {
        var values = new Dictionary<string, string?>
        {
            [$"{TemporalOptions.SectionName}:{nameof(TemporalOptions.TargetHost)}"] = "localhost:7233",
            [$"{TemporalOptions.SectionName}:{nameof(TemporalOptions.Namespace)}"] = "default",
        };

        IOptions<TemporalOptions> options = BuildOptions(values);

        Action act = () => _ = options.Value;

        act.Should().NotThrow();
        options.Value.TargetHost.Should().Be("localhost:7233");
        options.Value.Namespace.Should().Be("default");
    }

    [Fact]
    public void MissingTargetHost_FailsOnResolve()
    {
        var values = new Dictionary<string, string?>
        {
            [$"{TemporalOptions.SectionName}:{nameof(TemporalOptions.Namespace)}"] = "default",
        };

        IOptions<TemporalOptions> options = BuildOptions(values);

        Action act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void MissingNamespace_FailsOnResolve()
    {
        var values = new Dictionary<string, string?>
        {
            [$"{TemporalOptions.SectionName}:{nameof(TemporalOptions.TargetHost)}"] = "localhost:7233",
        };

        IOptions<TemporalOptions> options = BuildOptions(values);

        Action act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void MissingBoth_FailsOnResolve()
    {
        IOptions<TemporalOptions> options = BuildOptions([]);

        Action act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Defaults_ApplyWhenNotConfigured()
    {
        var values = new Dictionary<string, string?>
        {
            [$"{TemporalOptions.SectionName}:{nameof(TemporalOptions.TargetHost)}"] = "localhost:7233",
            [$"{TemporalOptions.SectionName}:{nameof(TemporalOptions.Namespace)}"] = "default",
        };

        TemporalOptions options = BuildOptions(values).Value;

        options.DefaultActivityStartToCloseTimeoutSeconds.Should().Be(30);
        options.DefaultRetryMaximumAttempts.Should().Be(5);
        options.ValidateNamespaceOnStart.Should().BeTrue();
        options.Tls.Should().BeFalse();
    }

    [Fact]
    public void SectionName_IsWorkflowsTemporal()
    {
        TemporalOptions.SectionName.Should().Be("Workflows:Temporal");
    }
}
