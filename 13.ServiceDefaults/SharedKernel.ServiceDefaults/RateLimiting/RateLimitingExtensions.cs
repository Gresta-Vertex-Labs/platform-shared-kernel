using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;

namespace SharedKernel.ServiceDefaults.RateLimiting;

/// <summary>
/// Opt-in ASP.NET Core rate limiting, wrapping the BCL's own <c>Microsoft.AspNetCore.RateLimiting</c>
/// middleware with platform-conservative defaults.
/// </summary>
/// <remarks>
/// No new NuGet package — <c>Microsoft.AspNetCore.RateLimiting</c> ships inside the
/// <c>Microsoft.AspNetCore.App</c> shared framework already referenced by this project. Entirely
/// opt-in: never called from <c>AddServiceDefaults()</c>, and never references
/// <c>14.Presentation</c> — a consuming service wanting a <c>ProblemDetails</c>-shaped rejection
/// body attaches its own <c>RateLimiterOptions.OnRejected</c> delegate via the <c>configure</c>
/// parameter on <see cref="AddSharedKernelRateLimiting"/> (below).
/// </remarks>
public static class RateLimitingExtensions
{
    /// <summary>Default permit limit for the global fixed-window limiter, per remote IP, per window.</summary>
    private const int DefaultGlobalPermitLimit = 100;

    /// <summary>Default window for the global fixed-window limiter.</summary>
    private static readonly TimeSpan DefaultGlobalWindow = TimeSpan.FromMinutes(1);

    /// <summary>Default permit limit for the <see cref="RateLimitPolicyNames.Authentication"/> policy, per window.</summary>
    private const int DefaultAuthenticationPermitLimit = 10;

    /// <summary>Default window for the <see cref="RateLimitPolicyNames.Authentication"/> policy.</summary>
    private static readonly TimeSpan DefaultAuthenticationWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Registers ASP.NET Core rate limiting with a conservative global fixed-window limiter
    /// (partitioned by remote IP) and a named <see cref="RateLimitPolicyNames.Authentication"/>
    /// policy the consumer attaches to its own token/login routes via
    /// <c>[EnableRateLimiting(RateLimitPolicyNames.Authentication)]</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">
    /// Optional additional configuration, invoked <b>last</b> — after this method's own defaults —
    /// so a caller can override any threshold, add further named policies, or attach a custom
    /// <see cref="RateLimiterOptions.OnRejected"/> delegate.
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="RateLimiterOptions.RejectionStatusCode"/> is set to
    /// <see cref="StatusCodes.Status429TooManyRequests"/>; <see cref="RateLimiterOptions.OnRejected"/>
    /// is left at the BCL default (a bare 429, no response body) unless <paramref name="configure"/>
    /// sets one.
    /// </para>
    /// <para>
    /// Entirely opt-in — must be paired with <c>app.UseRateLimiter()</c> after
    /// <c>builder.Build()</c>. A host that never calls this method is byte-identical in behavior to
    /// today.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddSharedKernelRateLimiting(
        this IHostApplicationBuilder builder,
        Action<RateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = DefaultGlobalPermitLimit,
                        Window = DefaultGlobalWindow,
                        QueueLimit = 0,
                    }));

            options.AddFixedWindowLimiter(RateLimitPolicyNames.Authentication, policyOptions =>
            {
                policyOptions.PermitLimit = DefaultAuthenticationPermitLimit;
                policyOptions.Window = DefaultAuthenticationWindow;
                policyOptions.QueueLimit = 0;
            });

            configure?.Invoke(options);
        });

        return builder;
    }
}
