using System.Runtime.CompilerServices;
using Microsoft.FeatureManagement;
using MsftFeatureDefinition = Microsoft.FeatureManagement.FeatureDefinition;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// The flag definitions exactly as configured, including their telemetry settings. The provider and startup
/// validation read these; <c>Microsoft.FeatureManagement</c>'s evaluator reads
/// <see cref="EvaluationFeatureDefinitionProvider"/>.
/// </summary>
internal sealed class ConfiguredFeatureDefinitions(IFeatureDefinitionProvider inner)
{
    public IFeatureDefinitionProvider Inner { get; } = inner;
}

/// <summary>
/// The definitions <c>Microsoft.FeatureManagement</c> evaluates, with telemetry turned off. Its own
/// <c>FeatureFlag</c> activity event records the targeting id (a user id) in traces; this package emits the
/// OpenTelemetry <c>feature_flag.evaluation</c> event instead, which does not.
/// </summary>
internal sealed class EvaluationFeatureDefinitionProvider(IFeatureDefinitionProvider inner) : IFeatureDefinitionProvider
{
    private readonly ConditionalWeakTable<MsftFeatureDefinition, MsftFeatureDefinition> _withoutTelemetry = new();

    public async Task<MsftFeatureDefinition> GetFeatureDefinitionAsync(string featureName) =>
        WithoutTelemetry(await inner.GetFeatureDefinitionAsync(featureName).ConfigureAwait(false));

    public async IAsyncEnumerable<MsftFeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        await foreach (MsftFeatureDefinition definition in inner.GetAllFeatureDefinitionsAsync().ConfigureAwait(false))
        {
            yield return WithoutTelemetry(definition);
        }
    }

    private MsftFeatureDefinition WithoutTelemetry(MsftFeatureDefinition definition) =>
        definition?.Telemetry is not { Enabled: true }
            ? definition!
            : _withoutTelemetry.GetValue(definition, static d => new MsftFeatureDefinition
            {
                Name = d.Name,
                EnabledFor = d.EnabledFor,
                RequirementType = d.RequirementType,
                Status = d.Status,
                Allocation = d.Allocation,
                Variants = d.Variants,
                Telemetry = null,
            });
}
