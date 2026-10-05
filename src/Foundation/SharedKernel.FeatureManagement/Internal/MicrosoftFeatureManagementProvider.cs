using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using MsftFeatureDefinition = Microsoft.FeatureManagement.FeatureDefinition;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// An OpenFeature provider over <c>Microsoft.FeatureManagement</c>. The evaluation context becomes an explicit
/// <see cref="ITargetingContext"/>, so the same targeting drives both the on/off state (the
/// <c>Microsoft.Targeting</c> filter) and variant allocation.
/// </summary>
internal sealed partial class MicrosoftFeatureManagementProvider : FeatureProvider
{
    public const string ProviderName = "Microsoft.FeatureManagement";

    /// <summary>Flag metadata key set to <see langword="true"/> when the flag's configuration enables telemetry.</summary>
    public const string TelemetryEnabledMetadataKey = "telemetryEnabled";

    private const string AlwaysOnFilter = "AlwaysOn";

    private static readonly Metadata ProviderMetadata = new(ProviderName);

    private readonly IVariantFeatureManager _features;
    private readonly IFeatureDefinitionProvider _definitions;
    private readonly ILogger<MicrosoftFeatureManagementProvider> _logger;

    public MicrosoftFeatureManagementProvider(
        IVariantFeatureManager features,
        ConfiguredFeatureDefinitions definitions,
        ILogger<MicrosoftFeatureManagementProvider> logger)
    {
        _features = features;
        _definitions = definitions.Inner;
        _logger = logger;
    }

    public override Metadata GetMetadata() => ProviderMetadata;

    public override Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
        string flagKey,
        bool defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        GuardAsync(flagKey, defaultValue, async () =>
        {
            MsftFeatureDefinition? definition = await _definitions.GetFeatureDefinitionAsync(flagKey).ConfigureAwait(false);
            if (definition is null)
            {
                return NotFound(flagKey, defaultValue);
            }

            bool enabled = await _features
                .IsEnabledAsync<ITargetingContext>(flagKey, ToTargetingContext(context), cancellationToken)
                .ConfigureAwait(false);

            return new ResolutionDetails<bool>(
                flagKey, enabled, ErrorType.None, EnabledReason(definition), null, null, FlagMetadata(definition));
        });

    public override Task<ResolutionDetails<string>> ResolveStringValueAsync(
        string flagKey,
        string defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        ResolveVariantAsync(flagKey, defaultValue, context, VariantValues.TryGetString, cancellationToken);

    public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
        string flagKey,
        int defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        ResolveVariantAsync(flagKey, defaultValue, context, VariantValues.TryGetInteger, cancellationToken);

    public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
        string flagKey,
        double defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        ResolveVariantAsync(flagKey, defaultValue, context, VariantValues.TryGetDouble, cancellationToken);

    public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
        string flagKey,
        Value defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        ResolveVariantAsync(flagKey, defaultValue, context, TryGetStructure, cancellationToken);

    private static bool TryGetStructure(IConfigurationSection? section, out Value value, out string? error)
    {
        value = ConfigurationValues.ToValue(section);
        error = value.IsNull ? "it has no configuration_value" : null;
        return error is null;
    }

    private delegate bool VariantReader<T>(IConfigurationSection? section, out T value, out string? error);

    private Task<ResolutionDetails<T>> ResolveVariantAsync<T>(
        string flagKey,
        T defaultValue,
        EvaluationContext? context,
        VariantReader<T> read,
        CancellationToken cancellationToken) =>
        GuardAsync(flagKey, defaultValue, async () =>
        {
            MsftFeatureDefinition? definition = await _definitions.GetFeatureDefinitionAsync(flagKey).ConfigureAwait(false);
            if (definition is null)
            {
                return NotFound(flagKey, defaultValue);
            }

            Variant? variant = await _features
                .GetVariantAsync(flagKey, ToTargetingContext(context), cancellationToken)
                .ConfigureAwait(false);

            if (variant is null)
            {
                string reason = IsOff(definition) ? Reason.Disabled : Reason.Default;
                return new ResolutionDetails<T>(flagKey, defaultValue, ErrorType.None, reason, null, null, FlagMetadata(definition));
            }

            if (!read(variant.Configuration, out T value, out string? error))
            {
                return new ResolutionDetails<T>(
                    flagKey,
                    defaultValue,
                    ErrorType.TypeMismatch,
                    Reason.Error,
                    variant.Name,
                    $"Variant '{variant.Name}' of flag '{flagKey}' cannot be read: {error}.",
                    FlagMetadata(definition));
            }

            return new ResolutionDetails<T>(
                flagKey, value, ErrorType.None, VariantReason(definition), variant.Name, null, FlagMetadata(definition));
        });

    private async Task<ResolutionDetails<T>> GuardAsync<T>(string flagKey, T defaultValue, Func<Task<ResolutionDetails<T>>> resolve)
    {
        try
        {
            return await resolve().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEvaluationFailed(flagKey, ex);
            return new ResolutionDetails<T>(
                flagKey,
                defaultValue,
                ErrorType.General,
                Reason.Error,
                null,
                $"Flag '{flagKey}' could not be evaluated ({ex.GetType().Name}); the default value was used.");
        }
    }

    private static ResolutionDetails<T> NotFound<T>(string flagKey, T defaultValue) =>
        new(flagKey, defaultValue, ErrorType.FlagNotFound, Reason.Error, null, $"Flag '{flagKey}' is not configured.");

    // Microsoft.FeatureManagement targets users and groups. The tenant joins the groups so it can be targeted too.
    private static TargetingContext ToTargetingContext(EvaluationContext? context)
    {
        if (context is null)
        {
            return new TargetingContext { UserId = null, Groups = [] };
        }

        var groups = new List<string>();
        if (context.TryGetValue(FeatureContextKeys.Groups, out Value? groupValue) && groupValue?.AsList is { } list)
        {
            groups.AddRange(list.Where(static v => v.IsString).Select(static v => v.AsString!));
        }

        if (context.TryGetValue(FeatureContextKeys.TenantId, out Value? tenantValue) && tenantValue?.AsString is { Length: > 0 } tenant)
        {
            groups.Add(tenant);
        }

        return new TargetingContext
        {
            UserId = string.IsNullOrEmpty(context.TargetingKey) ? null : context.TargetingKey,
            Groups = groups.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    private static bool IsOff(MsftFeatureDefinition definition) =>
        definition.Status == FeatureStatus.Disabled || definition.EnabledFor?.Any() != true;

    private static bool IsStatic(MsftFeatureDefinition definition) =>
        definition.EnabledFor!.All(static f => string.Equals(f.Name, AlwaysOnFilter, StringComparison.OrdinalIgnoreCase));

    private static string EnabledReason(MsftFeatureDefinition definition) =>
        IsOff(definition) ? Reason.Disabled
        : IsStatic(definition) ? Reason.Static
        : Reason.TargetingMatch;

    private static string VariantReason(MsftFeatureDefinition definition)
    {
        if (IsOff(definition))
        {
            return Reason.Disabled;
        }

        Allocation? allocation = definition.Allocation;
        bool targetsUsersOrGroups = allocation?.User?.Any() == true || allocation?.Group?.Any() == true;
        bool splits = allocation?.Percentile?.Any() == true;

        return targetsUsersOrGroups || !IsStatic(definition) ? Reason.TargetingMatch
            : splits ? Reason.Split
            : Reason.Static;
    }

    private static ImmutableMetadata? FlagMetadata(MsftFeatureDefinition definition)
    {
        TelemetryConfiguration? telemetry = definition.Telemetry;
        if (telemetry is not { Enabled: true })
        {
            return null;
        }

        var metadata = new Dictionary<string, object>(StringComparer.Ordinal) { [TelemetryEnabledMetadataKey] = true };
        if (telemetry.Metadata is not null)
        {
            foreach (KeyValuePair<string, string> entry in telemetry.Metadata)
            {
                metadata.TryAdd(entry.Key, entry.Value);
            }
        }

        return new ImmutableMetadata(metadata);
    }

    [LoggerMessage(
        EventId = FeatureManagementEventIds.EvaluationFailed,
        Level = LogLevel.Warning,
        Message = "Feature flag {FlagKey} could not be evaluated; the caller's default value was used")]
    private partial void LogEvaluationFailed(string flagKey, Exception exception);
}
