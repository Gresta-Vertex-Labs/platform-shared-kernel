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
    /// Gets the strategy order used when <see cref="StrategyOrder"/> is empty:
    /// <see cref="TenantResolutionStrategyNames.Claim"/>, <see cref="TenantResolutionStrategyNames.Header"/>,
    /// <see cref="TenantResolutionStrategyNames.Database"/>, in that order.
    /// </summary>
    /// <remarks>
    /// <b>SECURITY-MOTIVATED DEFAULT — DO NOT REORDER BACK TO <c>[Header, Claim, Database]</c>
    /// WITHOUT A SECURITY REVIEW.</b> The prior default let an unsigned, caller-supplied
    /// <c>X-Tenant-Id</c> header outrank a cryptographically-verified JWT tenant claim for the same
    /// request — a direct cross-tenant data-access vector, since the resolved tenant
    /// is what <c>06.Persistence</c>'s <c>TenantedDbContext</c> global filter trusts. Putting
    /// <see cref="TenantResolutionStrategyNames.Claim"/> first closes that vector:
    /// <see cref="ClaimTenantResolutionStrategy"/> returns <see langword="null"/> for any
    /// unauthenticated request or a token carrying no tenant claim, which is what makes this reorder
    /// provably safe for the pre-existing B2B/API-key header-only path — that path is completely
    /// unaffected, since it never has a claim to compete with. Only a request that is both
    /// authenticated with a tenant claim <b>and</b> carries a different <c>X-Tenant-Id</c> header
    /// changes behavior under this default, and it changes to the secure outcome (the claim wins).
    /// A service that already explicitly configures its own <see cref="StrategyOrder"/> is
    /// unaffected by this default entirely.
    /// </remarks>
    public static IReadOnlyList<string> DefaultStrategyOrder { get; } =
    [
        TenantResolutionStrategyNames.Claim,
        TenantResolutionStrategyNames.Header,
        TenantResolutionStrategyNames.Database,
    ];

    /// <summary>
    /// Gets or sets the ordered list of strategy names to attempt. Empty (the default) means
    /// <see cref="DefaultStrategyOrder"/>; a configured list replaces it.
    /// </summary>
    /// <remarks>
    /// The list is empty by default because configuration binding appends to a collection that
    /// already has items: a non-empty default plus a configured <c>["Header"]</c> would bind as
    /// <c>[Claim, Header, Database, Header]</c> instead of <c>["Header"]</c>.
    /// </remarks>
    public IReadOnlyList<string> StrategyOrder
    {
        get;
        set => field = value ?? [];
    } = [];

    /// <summary>Gets the order actually used: <see cref="StrategyOrder"/>, or the default when it is empty.</summary>
    internal IReadOnlyList<string> EffectiveStrategyOrder =>
        StrategyOrder.Count == 0 ? DefaultStrategyOrder : StrategyOrder;
}
