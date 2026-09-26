using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.MultiTenancy.Logging;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.MultiTenancy.Middleware;

/// <summary>
/// Resolves the current request's tenant by running the configured
/// <see cref="ITenantResolutionStrategy"/> set, in <see cref="TenantResolutionOptions.StrategyOrder"/>,
/// and runs the rest of the request inside a <see cref="RequestContextScope"/> whose
/// <see cref="IRequestContext.TenantId"/> is the first non-null result.
/// </summary>
/// <remarks>
/// <para>
/// The scope wraps the request's registered <see cref="IRequestContext"/> (or
/// <see cref="AnonymousRequestContext"/> when none is registered) and replaces only its tenant, so every
/// component that reads <see cref="IRequestContextAccessor"/> — or <see cref="IRequestContext"/> registered by
/// <c>SharedKernel.ServiceDefaults.Security</c>'s <c>AddSharedKernelRequestContext()</c>, which prefers the ambient
/// context — sees the resolved tenant. If no strategy resolves a tenant, the tenant is <see langword="null"/>, even
/// when the caller's credential asserts one: a tenanted <c>DbContext</c>'s global filter then matches zero rows
/// rather than risking a cross-tenant data leak.
/// </para>
/// <para>
/// <b>Ownership.</b> <c>SharedKernel.ServiceDefaults.Security</c>'s <c>UseSharedKernelRequestContext()</c>, placed
/// first in the pipeline, owns the request's scope and its correlation id. This middleware then opens a second,
/// inner scope over that one which replaces only the tenant, so the caller and the correlation id stay those of the
/// outer scope. The nesting is intentional: one scope per concern, both disposed when the request ends. Without
/// <c>UseSharedKernelRequestContext()</c> this middleware still works; its scope then wraps the DI-registered
/// caller and carries no correlation id.
/// </para>
/// <para>
/// Must be registered <b>after</b> <c>UseAuthentication()</c> in the request pipeline, via
/// <c>app.UseMiddleware&lt;TenantResolutionMiddleware&gt;()</c>, so that
/// <c>ClaimTenantResolutionStrategy</c> has access to a populated <see cref="HttpContext.User"/>.
/// </para>
/// <para>
/// <b>Opt-in tenant-status gate:</b> after a strategy resolves a tenant, an <see cref="ITenantStatusValidator"/> is
/// resolved from <see cref="HttpContext.RequestServices"/> via <c>GetService</c> — never
/// <c>GetRequiredService</c>, since it is genuinely optional. When registered, a <see langword="false"/> result from
/// <see cref="ITenantStatusValidator.IsActiveAsync"/> routes through the exact same fail-closed path as "no strategy
/// resolved," reusing <see cref="MultiTenancyLog.TenantNotResolved"/> rather than a distinct log message.
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
    /// pipeline inside a request-context scope carrying it. Never throws when zero strategies resolve a
    /// tenant, or when zero strategies are configured.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TenantId? resolvedTenantId = null;
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

        if (resolvedTenantId is { } candidate && resolvedStrategyName is not null)
        {
            // Optional gate: "not registered" (null) always passes through
            // unchanged — only a registered validator returning false fails closed. Null-safe on
            // RequestServices itself, which is null for a bare HttpContext never routed through the
            // real ASP.NET Core hosting pipeline (e.g. a DefaultHttpContext built directly in a
            // unit test with no IServiceProvidersFeature attached).
            var statusValidator = context.RequestServices?.GetService<ITenantStatusValidator>();
            var isActive = statusValidator is null
                || await statusValidator
                    .IsActiveAsync(candidate, context.RequestAborted)
                    .ConfigureAwait(false);

            if (isActive)
            {
                MultiTenancyLog.TenantResolved(logger, candidate, resolvedStrategyName);
            }
            else
            {
                // Fail-closed: an inactive/suspended tenant is treated identically to "no tenant
                // resolved" — same null tenant, same log call site, no distinct signal.
                resolvedTenantId = null;
                MultiTenancyLog.TenantNotResolved(logger);
            }
        }
        else
        {
            MultiTenancyLog.TenantNotResolved(logger);
        }

        // Ambient enrichment: make TenantId available to every log record produced for the
        // remainder of the request via SharedKernel.ServiceDefaults's BaggageLogRecordProcessor.
        // The item is always replaced: a caller can send a TenantId baggage item itself (W3C baggage), and one that
        // survived an unresolved request would be copied onto every log record as if it were the tenant (P-562 X2,
        // merged in P-579). No tenant removes the item rather than writing a sentinel.
        if (Activity.Current is { } activity)
        {
            while (activity.GetBaggageItem(WellKnownBaggageKeys.TenantId) is not null)
                activity.SetBaggage(WellKnownBaggageKeys.TenantId, null);

            if (resolvedTenantId is { } tenant)
                activity.SetBaggage(WellKnownBaggageKeys.TenantId, tenant.ToString());
        }

        var inner = RequestContextScope.Current
            ?? context.RequestServices?.GetService<IRequestContext>()
            ?? AnonymousRequestContext.Instance;
        using (RequestContextScope.Begin(inner.WithTenant(resolvedTenantId)))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}
