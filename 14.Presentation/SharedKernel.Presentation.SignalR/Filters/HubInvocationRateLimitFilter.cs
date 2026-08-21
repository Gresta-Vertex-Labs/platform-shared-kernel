using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// Enforces per-connection hub-method invocation rate limiting and argument-payload shape
/// validation, per <see cref="HubInvocationRateLimitOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Built on <see cref="System.Threading.RateLimiting"/> primitives — the same underlying library
/// <c>13.ServiceDefaults</c>'s <c>AddSharedKernelRateLimiting()</c> wraps for HTTP, used here
/// directly against a hub connection since ASP.NET Core's HTTP rate-limiting middleware does not
/// apply to SignalR invocations. Confirmed via a real build probe (Scaffold, S-27) that
/// <c>System.Threading.RateLimiting</c> ships transitively via the existing
/// <c>FrameworkReference Microsoft.AspNetCore.App</c> on <c>net10.0</c> — no new
/// <c>PackageReference</c> was required.
/// </para>
/// <para>
/// A <see cref="TokenBucketRateLimiter"/> is created lazily per connection — stored in
/// <see cref="HubCallerContext.Items"/>, mirroring <see cref="TenantContextHubFilter"/>'s existing
/// per-connection storage pattern — and consulted in <see cref="InvokeMethodAsync"/> before the
/// target method body runs. The limiter is disposed when the connection disconnects to avoid
/// leaking its internal replenishment timer.
/// </para>
/// <para>
/// A rejected invocation throws a <see cref="HubException"/> carrying a specific, caller-safe
/// "too many requests" message. <see cref="HubExceptionMappingFilter"/> was extended (P-417, D-64)
/// with a <c>catch (HubException) { throw; }</c> branch, checked first, so this filter's specific
/// message always survives unchanged regardless of hub-filter registration order.
/// </para>
/// </remarks>
public sealed class HubInvocationRateLimitFilter : IHubFilter
{
    private const string RateLimiterItemsKey = "SharedKernel.HubInvocationRateLimiter";
    private const string RateLimitExceededMessage = "Too many requests. Please slow down.";

    private readonly HubInvocationRateLimitOptions _options;

    /// <summary>
    /// Initialises a new <see cref="HubInvocationRateLimitFilter"/>.
    /// </summary>
    /// <param name="options">
    /// The rate-limit/argument-validation configuration. When no
    /// <see cref="HubInvocationRateLimitOptions"/> is registered in the container, a fresh default
    /// (fully disabled) instance is used instead.
    /// </param>
    public HubInvocationRateLimitFilter(HubInvocationRateLimitOptions? options = null)
    {
        _options = options ?? new HubInvocationRateLimitOptions();
    }

    /// <summary>
    /// Validates argument shape and enforces the per-connection invocation rate limit before
    /// invoking the target hub method.
    /// </summary>
    /// <param name="invocationContext">The context for the current hub method invocation.</param>
    /// <param name="next">The next delegate in the invocation pipeline.</param>
    /// <returns>The hub method's result, or throws a <see cref="HubException"/> on rejection.</returns>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ValidateArguments(invocationContext);

        if (_options.PermitLimit is { } permitLimit && permitLimit > 0)
        {
            var limiter = GetOrCreateLimiter(invocationContext.Context, permitLimit);
            using var lease = limiter.AttemptAcquire();

            if (!lease.IsAcquired)
            {
                throw new HubException(RateLimitExceededMessage);
            }
        }

        return await next(invocationContext).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the per-connection rate limiter, if one was created, when the connection
    /// disconnects.
    /// </summary>
    /// <param name="context">The hub lifetime context for the disconnecting client.</param>
    /// <param name="exception">The exception that caused the disconnect, if any.</param>
    /// <param name="next">The next delegate in the disconnect pipeline.</param>
    /// <returns>A task that completes when the disconnect pipeline has finished.</returns>
    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        if (context.Context.Items.TryGetValue(RateLimiterItemsKey, out var value) && value is RateLimiter limiter)
        {
            limiter.Dispose();
            context.Context.Items.Remove(RateLimiterItemsKey);
        }

        return next(context, exception);
    }

    private void ValidateArguments(HubInvocationContext invocationContext)
    {
        if (_options.MaxStringArgumentLength is { } maxLength)
        {
            foreach (var argument in invocationContext.HubMethodArguments)
            {
                if (argument is string stringArgument && stringArgument.Length > maxLength)
                {
                    throw new HubException(
                        $"An argument to '{invocationContext.HubMethodName}' exceeds the maximum allowed length of {maxLength} characters.");
                }
            }
        }

        foreach (var validator in _options.ArgumentValidators)
        {
            var rejectionMessage = validator(invocationContext);

            if (rejectionMessage is not null)
            {
                throw new HubException(rejectionMessage);
            }
        }
    }

    private RateLimiter GetOrCreateLimiter(HubCallerContext context, int permitLimit)
    {
        if (context.Items.TryGetValue(RateLimiterItemsKey, out var existing) && existing is RateLimiter existingLimiter)
        {
            return existingLimiter;
        }

        var created = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = permitLimit,
            TokensPerPeriod = permitLimit,
            ReplenishmentPeriod = _options.Window,
            AutoReplenishment = true,
            QueueLimit = 0,
        });

        context.Items[RateLimiterItemsKey] = created;
        return created;
    }
}
