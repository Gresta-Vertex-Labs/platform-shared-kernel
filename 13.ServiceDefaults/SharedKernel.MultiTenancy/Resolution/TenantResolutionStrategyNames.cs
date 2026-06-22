namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Well-known <see cref="ITenantResolutionStrategy.StrategyName"/> values for the three
/// platform-shipped tenant resolution strategies.
/// </summary>
/// <remarks>
/// Mirrors the <c>HealthCheckTags</c> constants-class pattern used in
/// <c>SharedKernel.ServiceDefaults</c>. <see cref="HeaderTenantResolutionStrategy"/>,
/// <see cref="ClaimTenantResolutionStrategy"/>, and <see cref="DatabaseTenantResolutionStrategy"/>
/// all set their <see cref="ITenantResolutionStrategy.StrategyName"/> from these constants, and
/// <see cref="TenantResolutionOptions.StrategyOrder"/>'s default array references the same
/// constants — zero duplicated bare string literals across strategies, options, and tests. A
/// custom fourth-strategy implementation is free to use its own string literal for
/// <see cref="ITenantResolutionStrategy.StrategyName"/>; this constants class only covers the
/// three platform-shipped strategies.
/// </remarks>
public static class TenantResolutionStrategyNames
{
    /// <summary>The <see cref="HeaderTenantResolutionStrategy"/> resolution-order key.</summary>
    public const string Header = "Header";

    /// <summary>The <see cref="ClaimTenantResolutionStrategy"/> resolution-order key.</summary>
    public const string Claim = "Claim";

    /// <summary>The <see cref="DatabaseTenantResolutionStrategy"/> resolution-order key.</summary>
    public const string Database = "Database";
}
