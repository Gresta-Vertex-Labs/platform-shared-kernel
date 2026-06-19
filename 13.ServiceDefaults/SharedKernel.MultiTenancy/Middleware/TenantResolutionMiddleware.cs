using Microsoft.AspNetCore.Http;
using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Middleware;

/// <summary>
/// Resolves the current request's tenant identifier by running the configured
/// <see cref="ITenantResolutionStrategy"/> set, in <see cref="TenantResolutionOptions.StrategyOrder"/>,
/// and populating <see cref="AmbientTenantProvider"/> with the first non-null result.
/// </summary>
/// <remarks>
/// <para>
/// If no strategy resolves a tenant, <see cref="AmbientTenantProvider.TenantId"/> remains
/// <see cref="Guid.Empty"/> — consistent with the <see cref="Guid.Empty"/> no-tenant sentinel rule
/// shared with <c>06.Persistence</c>: a tenanted <c>DbContext</c>'s global filter then matches
/// zero rows rather than risking a cross-tenant data leak.
/// </para>
/// <para>
/// Must be registered <b>after</b> <c>UseAuthentication()</c> in the request pipeline, via
/// <c>app.UseMiddleware&lt;TenantResolutionMiddleware&gt;()</c>, so that
/// <c>ClaimTenantResolutionStrategy</c> has access to a populated <see cref="HttpContext.User"/>.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    IEnumerable<ITenantResolutionStrategy> strategies,
    Microsoft.Extensions.Options.IOptions<TenantResolutionOptions> options)
{
    /// <summary>
    /// Resolves the tenant for the current request and invokes the next middleware in the
    /// pipeline. Never throws when zero strategies resolve a tenant, or when zero strategies are
    /// configured.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="tenantProvider">The scoped <see cref="AmbientTenantProvider"/> to populate.</param>
    public async Task InvokeAsync(HttpContext context, AmbientTenantProvider tenantProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantProvider);

        var strategiesByName = strategies.ToDictionary(s => s.GetType().Name switch
        {
            nameof(HeaderTenantResolutionStrategy) => "Header",
            nameof(ClaimTenantResolutionStrategy) => "Claim",
            nameof(DatabaseTenantResolutionStrategy) => "Database",
            var typeName => typeName,
        });

        foreach (var strategyName in options.Value.StrategyOrder)
        {
            if (!strategiesByName.TryGetValue(strategyName, out var strategy))
            {
                continue;
            }

            var resolved = await strategy
                .TryResolveAsync(context, context.RequestAborted)
                .ConfigureAwait(false);

            if (resolved is { } tenantId)
            {
                tenantProvider.SetTenantId(tenantId);
                break;
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
