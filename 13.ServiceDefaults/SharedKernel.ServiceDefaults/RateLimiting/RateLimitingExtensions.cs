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
/// <c>14.Presentation</c>. A service that also calls <c>SharedKernel.Presentation.WebApi</c>'s
/// <c>AddSharedKernelWebApi()</c> gets the platform's RFC 9457 rejection body without writing any
/// code: that package fills <see cref="RateLimiterOptions.OnRejected"/> when nothing else has, so a
/// rejection becomes a 429 <c>application/problem+json</c> response with <c>errorCode</c>
/// <c>rate_limit.exceeded</c> and a <c>Retry-After</c> header whenever the limiter suggests a delay.
/// That package is named here in documentation only; this project takes no compiled reference to it
/// in either direction.
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
    /// <see cref="StatusCodes.Status429TooManyRequests"/>. <see cref="RateLimiterOptions.OnRejected"/>
    /// is deliberately left unset, which decides the rejection body:
    /// </para>
    /// <list type="bullet">
    ///   <item>With <c>SharedKernel.Presentation.WebApi</c>'s <c>AddSharedKernelWebApi()</c>
    ///   (<c>14.Presentation</c>): the platform's RFC 9457 body — 429 <c>application/problem+json</c>,
    ///   <c>errorCode</c> <c>rate_limit.exceeded</c>, and <c>Retry-After</c> in whole seconds when the
    ///   rejected lease carries <c>MetadataName.RetryAfter</c>, as the fixed-window limiters this method
    ///   installs do. Nothing to write.</item>
    ///   <item>Without it: ASP.NET Core's default, a bare 429 with no body.</item>
    ///   <item>An <see cref="RateLimiterOptions.OnRejected"/> set in <paramref name="configure"/> replaces
    ///   both; <c>AddSharedKernelWebApi()</c> keeps a handler a service wrote itself.</item>
    /// </list>
    /// <para>
    /// The limiter must also be in the request pipeline. <c>UseSharedKernelWebApi()</c> adds
    /// <c>UseRateLimiter()</c> itself whenever rate limiting is registered — after authentication, so a
    /// policy can partition by the caller, and before authorization, so refused requests count too — so a
    /// service using it calls nothing more. Without it, call <c>app.UseRateLimiter()</c> after <c>builder.Build()</c>, after
    /// <c>UseRouting()</c> when endpoints name a policy.
    /// </para>
    /// <para>
    /// Both compositions are proven by <c>RateLimitRejectionRecipeTests</c> through a test-only
    /// reference; this production project takes no compiled reference to
    /// <c>SharedKernel.Presentation.WebApi</c> in either direction. A host that never calls this method
    /// is byte-identical in behavior to one without it.
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
