namespace SharedKernel.ServiceDefaults.Localization;

/// <summary>
/// Configures <see cref="LocalizationExtensions.AddSharedKernelLocalization"/>'s
/// precedence-ordered culture resolution.
/// </summary>
public sealed class LocalizationResolutionOptions
{
    /// <summary>
    /// Gets the order used when <see cref="StrategyOrder"/> is empty:
    /// <c>[UserPreference, TenantDefault, AcceptLanguageHeader]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default: <c>[UserPreference, TenantDefault, AcceptLanguageHeader]</c> — signed-signal-
    /// before-unsigned-header, deliberately mirroring WO-061/P-393's corrected
    /// <c>[Claim, Header, Database]</c> tenant-resolution order: an authenticated user's own
    /// stored preference must outrank a browser's <c>Accept-Language</c> default for the identical
    /// reason a signed JWT tenant claim outranks an unsigned <c>X-Tenant-Id</c> header. Do not
    /// reorder this default without a security review — see WO-061's original finding of what went
    /// wrong the first time an unsigned signal was allowed to outrank a signed one.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<LocalizationResolutionStrategy> DefaultStrategyOrder { get; } =
    [
        LocalizationResolutionStrategy.UserPreference,
        LocalizationResolutionStrategy.TenantDefault,
        LocalizationResolutionStrategy.AcceptLanguageHeader,
    ];

    /// <summary>
    /// The order in which <see cref="LocalizationResolutionStrategy"/> steps are tried; the first
    /// step that resolves a culture wins. Empty (the default) means <see cref="DefaultStrategyOrder"/>;
    /// a configured list replaces it.
    /// </summary>
    /// <remarks>
    /// The list is empty by default because configuration binding appends to a collection that
    /// already has items, so a bound order would otherwise be added after the default instead of
    /// replacing it.
    /// </remarks>
    public IReadOnlyList<LocalizationResolutionStrategy> StrategyOrder
    {
        get;
        set => field = value ?? [];
    } = [];

    /// <summary>Gets the order actually used: <see cref="StrategyOrder"/>, or the default when it is empty.</summary>
    internal IReadOnlyList<LocalizationResolutionStrategy> EffectiveStrategyOrder =>
        StrategyOrder.Count == 0 ? DefaultStrategyOrder : StrategyOrder;

    /// <summary>
    /// The claim type read for the <see cref="LocalizationResolutionStrategy.UserPreference"/>
    /// step. Defaults to <see langword="null"/> — <b>no default tied to any one identity
    /// provider's claim-naming convention</b>, mirroring <c>MtlsForwardedHeaderOptions.HeaderName</c>'s
    /// "never a vendor-guessed default" rule. When <see langword="null"/>, the
    /// <see cref="LocalizationResolutionStrategy.UserPreference"/> step is skipped cleanly — it
    /// never throws.
    /// </summary>
    public string? UserPreferenceClaimType { get; set; }
}
