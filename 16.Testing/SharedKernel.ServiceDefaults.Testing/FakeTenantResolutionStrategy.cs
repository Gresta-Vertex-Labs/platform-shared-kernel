using Microsoft.AspNetCore.Http;

namespace SharedKernel.Testing.ServiceDefaults;

/// <summary>
/// Configurable test double structurally compatible with
/// <c>SharedKernel.MultiTenancy.ITenantResolutionStrategy</c> (<c>13.ServiceDefaults</c>), without
/// taking a project reference to <c>SharedKernel.MultiTenancy</c>.
/// </summary>
/// <remarks>
/// This package takes no project reference to <c>SharedKernel.MultiTenancy</c> (scope lock) — a
/// direct interface implementation would require that reference. Member shapes
/// (<see cref="StrategyName"/>, <see cref="TryResolveAsync"/>) match
/// <c>ITenantResolutionStrategy</c> exactly, so a consuming service's test project that does take
/// that reference can still use this type as a drop-in (duck-typed) substitute.
/// </remarks>
public sealed class FakeTenantResolutionStrategy
{
    private readonly Func<HttpContext, CancellationToken, Task<Guid?>> _resolver;

    /// <summary>
    /// Initialises a new <see cref="FakeTenantResolutionStrategy"/> that always resolves to
    /// <paramref name="fixedResult"/>.
    /// </summary>
    /// <param name="fixedResult">The fixed result every call to <see cref="TryResolveAsync"/> returns.</param>
    public FakeTenantResolutionStrategy(Guid? fixedResult = null)
        : this((_, _) => Task.FromResult(fixedResult))
    {
    }

    /// <summary>
    /// Initialises a new <see cref="FakeTenantResolutionStrategy"/> delegating resolution to
    /// <paramref name="resolver"/>.
    /// </summary>
    /// <param name="resolver">The delegate invoked by <see cref="TryResolveAsync"/>.</param>
    public FakeTenantResolutionStrategy(Func<HttpContext, CancellationToken, Task<Guid?>> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>
    /// Gets or sets the strategy name, mirroring <c>ITenantResolutionStrategy.StrategyName</c>'s shape.
    /// </summary>
    public string StrategyName { get; set; } = "Fake";

    /// <summary>
    /// Resolves a tenant id for <paramref name="context"/>, mirroring
    /// <c>ITenantResolutionStrategy.TryResolveAsync</c>'s signature exactly.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved tenant id, or <see langword="null"/> when this strategy cannot resolve one.</returns>
    public Task<Guid?> TryResolveAsync(HttpContext context, CancellationToken ct) => _resolver(context, ct);
}
