namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Configures the ordered set of tenant resolution strategies attempted by
/// <see cref="Middleware.TenantResolutionMiddleware"/>.
/// </summary>
/// <remarks>
/// Bound from the configuration section <c>"SharedKernel:MultiTenancy"</c>. The first strategy in
/// <see cref="StrategyOrder"/> whose <see cref="ITenantResolutionStrategy.TryResolveAsync"/>
/// returns a non-null result wins. A service with no tenant directory database simply omits
/// <c>"Database"</c> from the configured order — no code change, no null-reference risk.
/// </remarks>
public sealed class TenantResolutionOptions
{
    /// <summary>The configuration section key for <see cref="TenantResolutionOptions"/>.</summary>
    public const string SectionName = "SharedKernel:MultiTenancy";

    /// <summary>
    /// Gets or sets the ordered list of strategy names to attempt. Defaults to
    /// <c>["Header", "Claim", "Database"]</c>.
    /// </summary>
    public IReadOnlyList<string> StrategyOrder { get; set; } = ["Header", "Claim", "Database"];
}
