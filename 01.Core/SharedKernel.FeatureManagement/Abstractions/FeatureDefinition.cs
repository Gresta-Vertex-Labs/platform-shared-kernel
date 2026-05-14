namespace SharedKernel.FeatureManagement.Abstractions;

/// <summary>
/// Describes a feature flag — its name, default state, and an optional human-readable description.
/// </summary>
/// <remarks>
/// <see cref="FeatureDefinition"/> records are used to declare feature flags in a discoverable,
/// typed manner. They are not directly used by the runtime evaluation path; use
/// <see cref="IFeatureManager"/> for evaluation.
/// </remarks>
/// <param name="Name">The stable, unique name of the feature flag (matches the key in configuration).</param>
/// <param name="DefaultValue">
/// The default enabled/disabled state when no configuration is present.
/// Defaults to <c>false</c> (opt-in, safe).
/// </param>
/// <param name="Description">An optional human-readable description of what this feature controls.</param>
public sealed record FeatureDefinition(
    string Name,
    bool DefaultValue = false,
    string? Description = null);
