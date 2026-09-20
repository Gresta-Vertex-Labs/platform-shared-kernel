using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.MultiTenancy.Logging;
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
/// <para>
/// <b>Opt-in tenant-status gate:</b> after a strategy resolves a non-<see cref="Guid.Empty"/>
/// tenant, an <see cref="ITenantStatusValidator"/> is resolved from
/// <see cref="HttpContext.RequestServices"/> via <c>GetService</c> — never <c>GetRequiredService</c>,
/// since it is genuinely optional. When registered, a <see langword="false"/> result from
/// <see cref="ITenantStatusValidator.IsActiveAsync"/> routes through the exact same
/// <see cref="Guid.Empty"/> fail-closed path as "no strategy resolved," reusing
/// <see cref="MultiTenancyLog.TenantNotResolved"/> rather than a distinct log message.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    IEnumerable<ITenantResolutionStrategy> strategies,
    Microsoft.Extensions.Options.IOptions<TenantResolutionOptions> options,
    ILogger<TenantResolutionMiddleware> logger)
{
    /// <summary>
    /// The <see cref="ITenantResolutionStrategy.StrategyName"/> → strategy lookup, computed once
    /// against the fixed strategy set supplied to this middleware instance rather than rebuilt as
    /// a fresh allocation on every <see cref="InvokeAsync"/> call. Matches each
    /// <see cref="TenantResolutionOptions.StrategyOrder"/> entry against each strategy's declared
    /// <see cref="ITenantResolutionStrategy.StrategyName"/> — never against the implementing
    /// type's CLR type name.
    /// </summary>
    private readonly FrozenDictionary<string, ITenantResolutionStrategy> _strategiesByName =
        strategies.ToFrozenDictionary(s => s.StrategyName);

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

        var resolvedTenantId = Guid.Empty;
        string? resolvedStrategyName = null;

        foreach (var strategyName in options.Value.EffectiveStrategyOrder)
        {
            if (!_strategiesByName.TryGetValue(strategyName, out var strategy))
            {
                continue;
            }

            var resolved = await strategy
                .TryResolveAsync(context, context.RequestAborted)
                    .ConfigureAwait(false);

            if (resolved is { } tenantId)
            {
                resolvedTenantId = tenantId;
                resolvedStrategyName = strategyName;
                break;
            }
        }

        if (resolvedStrategyName is not null)
        {
            // Optional gate: "not registered" (null) always passes through
            // unchanged — only a registered validator returning false fails closed. Null-safe on
            // RequestServices itself, which is null for a bare HttpContext never routed through the
            // real ASP.NET Core hosting pipeline (e.g. a DefaultHttpContext built directly in a
            // unit test with no IServiceProvidersFeature attached).
            var statusValidator = context.RequestServices?.GetService<ITenantStatusValidator>();
            var isActive = statusValidator is null
                || await statusValidator
                    .IsActiveAsync(resolvedTenantId, context.RequestAborted)
                        .ConfigureAwait(false);

            if (isActive)
            {
                tenantProvider.SetTenantId(resolvedTenantId);
                MultiTenancyLog.TenantResolved(logger, resolvedTenantId, resolvedStrategyName);
            }
            else
            {
                // Fail-closed: an inactive/suspended tenant is treated identically to "no tenant
                // resolved" — same Guid.Empty sentinel, same log call site, no distinct signal.
                resolvedTenantId = Guid.Empty;
                MultiTenancyLog.TenantNotResolved(logger);
            }
        }
        else
        {
            MultiTenancyLog.TenantNotResolved(logger);
        }

        // Ambient enrichment: make TenantId available to every log record produced for the
        // remainder of the request via SharedKernel.ServiceDefaults's BaggageLogRecordProcessor.
        // Set unconditionally — including the Guid.Empty no-tenant sentinel — so log aggregation
        // can distinguish "no tenant resolved for this request" from "enrichment was never wired".
        Activity.Current?.SetBaggage(TenantBaggageKeys.TenantId, resolvedTenantId.ToString());

        await next(context).ConfigureAwait(false);
    }
}
