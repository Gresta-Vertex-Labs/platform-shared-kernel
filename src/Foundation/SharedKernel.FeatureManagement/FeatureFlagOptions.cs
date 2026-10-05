using Microsoft.FeatureManagement;
using OpenFeature.Hosting;

namespace SharedKernel.FeatureManagement;

/// <summary>
/// Options for <see cref="FeatureManagementServiceCollectionExtensions.AddSharedKernelFeatureManagement"/>.
/// </summary>
public sealed class FeatureFlagOptions
{
    private readonly List<FeatureFlag> _flagsToValidate = [];
    private readonly List<Action<IFeatureManagementBuilder>> _featureManagementActions = [];
    private readonly List<Action<OpenFeatureBuilder>> _openFeatureActions = [];

    /// <summary>
    /// Whether each flag is evaluated once per dependency-injection scope (one HTTP request, one message,
    /// one job run) and the same result reused for the rest of it. Defaults to <see langword="true"/>, so a
    /// configuration reload cannot switch a flag halfway through a request.
    /// </summary>
    /// <remarks>
    /// A result is reused only for the same flag, default value and explicit evaluation context. Failed
    /// evaluations are never reused, and neither are calls that pass their own <c>FlagEvaluationOptions</c>.
    /// </remarks>
    public bool EvaluateOncePerScope { get; set; } = true;

    /// <summary>
    /// The longest time a result is reused within one scope. Defaults to one minute. It bounds how long a
    /// long-lived scope (or an <c>IFeatureClient</c> resolved from the root provider by mistake) can keep
    /// serving a flag after it was switched off.
    /// </summary>
    public TimeSpan ScopeResultLifetime { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Which evaluations add an OpenTelemetry <c>feature_flag.evaluation</c> event to the current
    /// <see cref="System.Diagnostics.Activity"/>. Defaults to <see cref="FeatureTelemetryMode.ConfiguredFlags"/>.
    /// </summary>
    public FeatureTelemetryMode Telemetry { get; set; } = FeatureTelemetryMode.ConfiguredFlags;

    /// <summary>The flags checked at startup by <see cref="ValidateOnStart"/>.</summary>
    public IReadOnlyList<FeatureFlag> FlagsToValidate => _flagsToValidate;

    internal IReadOnlyList<Action<IFeatureManagementBuilder>> FeatureManagementActions => _featureManagementActions;

    internal IReadOnlyList<Action<OpenFeatureBuilder>> OpenFeatureActions => _openFeatureActions;

    /// <summary>
    /// Checks <paramref name="flags"/> when the host starts: each must be configured, and for a string,
    /// number or object flag, every variant's value must fit the flag's type. Startup fails with a
    /// <see cref="FeatureFlagValidationException"/> listing every problem, so a misspelled key or a broken
    /// variant never silently evaluates to the default in production.
    /// </summary>
    /// <param name="flags">The flags to check. Can be called more than once.</param>
    /// <returns>These options, for chaining.</returns>
    public FeatureFlagOptions ValidateOnStart(params FeatureFlag[] flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        foreach (FeatureFlag flag in flags)
        {
            ArgumentNullException.ThrowIfNull(flag, nameof(flags));
            if (!_flagsToValidate.Exists(f => string.Equals(f.Key, flag.Key, StringComparison.Ordinal)))
            {
                _flagsToValidate.Add(flag);
            }
        }

        return this;
    }

    /// <summary>
    /// Registers a custom <c>Microsoft.FeatureManagement</c> feature filter, referenced from configuration by its
    /// name or <c>[FilterAlias]</c>. The built-in <c>Microsoft.Percentage</c>, <c>Microsoft.TimeWindow</c> and
    /// <c>Microsoft.Targeting</c> filters are always available.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <returns>These options, for chaining.</returns>
    public FeatureFlagOptions AddFeatureFilter<TFilter>()
        where TFilter : IFeatureFilterMetadata
    {
        _featureManagementActions.Add(static builder => builder.AddFeatureFilter<TFilter>());
        return this;
    }

    /// <summary>
    /// Adds to the OpenFeature registration, for example a hook such as OpenFeature's <c>MetricsHook</c>.
    /// Do not add a provider here: this package registers the <c>Microsoft.FeatureManagement</c> provider. An
    /// <c>AddContext</c> call here replaces the ambient targeting from <see cref="IFeatureTargetingContextAccessor"/>.
    /// </summary>
    /// <param name="configure">The configuration action.</param>
    /// <returns>These options, for chaining.</returns>
    public FeatureFlagOptions ConfigureOpenFeature(Action<OpenFeatureBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _openFeatureActions.Add(configure);
        return this;
    }

    internal void Validate()
    {
        if (ScopeResultLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ScopeResultLifetime), ScopeResultLifetime, "ScopeResultLifetime must be positive.");
        }

        if (!Enum.IsDefined(Telemetry))
        {
            throw new ArgumentOutOfRangeException(nameof(Telemetry), Telemetry, "Unknown telemetry mode.");
        }
    }
}

/// <summary>Which flag evaluations emit the OpenTelemetry <c>feature_flag.evaluation</c> event.</summary>
public enum FeatureTelemetryMode
{
    /// <summary>
    /// Only flags whose configuration sets <c>"telemetry": { "enabled": true }</c>. The telemetry
    /// <c>metadata</c> keys <c>version</c>, <c>flagSetId</c> and <c>contextId</c> become the event's
    /// <c>feature_flag.version</c>, <c>feature_flag.set.id</c> and <c>feature_flag.context.id</c>.
    /// </summary>
    ConfiguredFlags,

    /// <summary>Every evaluation.</summary>
    AllFlags,

    /// <summary>No evaluation events.</summary>
    Off,
}
