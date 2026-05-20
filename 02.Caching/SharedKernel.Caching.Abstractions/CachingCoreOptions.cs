namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Shared caching options consumed by all provider packages
/// (<c>SharedKernel.Caching.FusionCache</c> and <c>SharedKernel.Caching.Redis</c>).
/// </summary>
/// <remarks>
/// <para>
/// This class is the authoritative source of options that must be available to both
/// the FusionCache L1 provider and the Redis L2 provider without either package
/// referencing the other. It lives in <c>SharedKernel.Caching.Abstractions</c> so
/// that both sibling provider packages can depend on it via their shared Abstractions
/// reference.
/// </para>
/// <para>
/// <b>Registration:</b> <c>AddSharedKernelCaching</c> registers and configures
/// <see cref="CachingCoreOptions"/> as part of its DI setup, copying
/// <c>ServiceName</c> from <c>CachingOptions</c> so both types remain in sync.
/// Consumers resolve <c>IOptions&lt;CachingCoreOptions&gt;</c> from the DI container —
/// they must not construct this class directly.
/// </para>
/// </remarks>
public sealed class CachingCoreOptions
{
    /// <summary>
    /// The logical name of the owning service. Used to derive cache invalidation channel
    /// names and cache key prefixes across all provider packages.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"app"</c>. In production this value is set by
    /// <c>AddSharedKernelCaching</c> by copying from <c>CachingOptions.ServiceName</c>,
    /// which enforces a non-null / non-whitespace validation. The Redis provider packages
    /// trust this value is valid and do not re-validate it.
    /// </remarks>
    public string ServiceName { get; set; } = "app";
}
