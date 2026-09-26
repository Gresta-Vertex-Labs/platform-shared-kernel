using Microsoft.AspNetCore.Http;
using SharedKernel.Execution.Tenancy;
using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.Testing.ServiceDefaults;

/// <summary>
/// Configurable test double for <see cref="ITenantResolutionStrategy"/>.
/// </summary>
/// <remarks>
/// Resolves to a fixed <see cref="TenantId"/> (or <see langword="null"/>, meaning "this strategy does not
/// apply"), or delegates to a caller-supplied resolver. Register it like any other strategy and name it in
/// <see cref="TenantResolutionOptions.StrategyOrder"/> by its <see cref="StrategyName"/>.
/// </remarks>
public sealed class FakeTenantResolutionStrategy : ITenantResolutionStrategy
{
    private readonly Func<HttpContext, CancellationToken, Task<TenantId?>> _resolver;

    /// <summary>
    /// Initialises a new <see cref="FakeTenantResolutionStrategy"/> that always resolves to
    /// <paramref name="fixedResult"/>.
    /// </summary>
    /// <param name="fixedResult">The fixed result every call to <see cref="TryResolveAsync"/> returns.</param>
    public FakeTenantResolutionStrategy(TenantId? fixedResult = null)
        : this((_, _) => Task.FromResult(fixedResult))
    {
    }

    /// <summary>
    /// Initialises a new <see cref="FakeTenantResolutionStrategy"/> delegating resolution to
    /// <paramref name="resolver"/>.
    /// </summary>
    /// <param name="resolver">The delegate invoked by <see cref="TryResolveAsync"/>.</param>
    public FakeTenantResolutionStrategy(Func<HttpContext, CancellationToken, Task<TenantId?>> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>
    /// Gets or sets the strategy name matched against <see cref="TenantResolutionOptions.StrategyOrder"/>.
    /// Defaults to <c>"Fake"</c>.
    /// </summary>
    public string StrategyName { get; set; } = "Fake";

    /// <inheritdoc />
    public Task<TenantId?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
        _resolver(context, cancellationToken);
}
