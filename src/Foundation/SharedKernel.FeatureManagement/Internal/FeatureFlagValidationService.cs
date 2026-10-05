using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using MsftFeatureDefinition = Microsoft.FeatureManagement.FeatureDefinition;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>The registered options, kept so validation can read the flags declared at registration.</summary>
internal sealed class FeatureFlagRegistration
{
    public FeatureFlagRegistration(FeatureFlagOptions options) => Options = options;

    public FeatureFlagOptions Options { get; }
}

/// <summary>
/// Checks the flags declared with <see cref="FeatureFlagOptions.ValidateOnStart"/> when the host starts and
/// stops startup with every problem found.
/// </summary>
internal sealed class FeatureFlagValidationService : IHostedService
{
    private readonly FeatureFlagRegistration _registration;
    private readonly IFeatureDefinitionProvider _definitions;

    public FeatureFlagValidationService(FeatureFlagRegistration registration, ConfiguredFeatureDefinitions definitions)
    {
        _registration = registration;
        _definitions = definitions.Inner;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (FeatureFlag flag in _registration.Options.FlagsToValidate)
        {
            MsftFeatureDefinition? definition = await _definitions.GetFeatureDefinitionAsync(flag.Key).ConfigureAwait(false);
            if (definition is null)
            {
                failures.Add($"'{flag.Key}' is not configured.");
                continue;
            }

            if (flag.Kind == FeatureFlagKind.Boolean)
            {
                continue;
            }

            List<VariantDefinition> variants = definition.Variants?.ToList() ?? [];
            if (variants.Count == 0)
            {
                failures.Add($"'{flag.Key}' has no variants; a FeatureFlag.{flag.Kind} flag reads its value from one.");
                continue;
            }

            foreach (VariantDefinition variant in variants)
            {
                if (flag.CheckVariant(variant.ConfigurationValue) is { } problem)
                {
                    failures.Add($"'{flag.Key}' variant '{variant.Name}': {problem}.");
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new FeatureFlagValidationException(failures);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
