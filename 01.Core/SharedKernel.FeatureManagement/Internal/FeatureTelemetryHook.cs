using OpenFeature;
using OpenFeature.Hooks;
using OpenFeature.Model;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// Adds OpenTelemetry's <c>feature_flag.evaluation</c> event to the current activity, through OpenFeature's
/// <see cref="TraceEnricherHook"/>, for the flags selected by <see cref="FeatureTelemetryMode"/>.
/// </summary>
internal sealed class FeatureTelemetryHook : Hook
{
    private readonly FeatureTelemetryMode _mode;
    private readonly TraceEnricherHook _events = new();

    public FeatureTelemetryHook(FeatureTelemetryMode mode) => _mode = mode;

    public override ValueTask FinallyAsync<T>(
        HookContext<T> context,
        FlagEvaluationDetails<T> evaluationDetails,
        IReadOnlyDictionary<string, object>? hints = null,
        CancellationToken cancellationToken = default)
    {
        bool emit = _mode switch
        {
            FeatureTelemetryMode.AllFlags => true,
            FeatureTelemetryMode.ConfiguredFlags =>
                evaluationDetails.FlagMetadata?.GetBool(MicrosoftFeatureManagementProvider.TelemetryEnabledMetadataKey) == true,
            _ => false,
        };

        return emit
            ? _events.FinallyAsync(context, evaluationDetails, hints, cancellationToken)
            : ValueTask.CompletedTask;
    }
}
