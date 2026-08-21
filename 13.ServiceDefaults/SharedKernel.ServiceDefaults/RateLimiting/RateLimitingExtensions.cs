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
/// <c>14.Presentation</c> — a consuming service wanting an RFC 9457 <c>ProblemDetails</c>-shaped
/// rejection body attaches its own <c>RateLimiterOptions.OnRejected</c> delegate via the
/// <c>configure</c> parameter on <see cref="AddSharedKernelRateLimiting"/> (below), calling
/// <c>14.Presentation.WebApi</c>'s own <c>RateLimitRejectionProblemDetails.Create(HttpContext,
/// TimeSpan?)</c> helper — the platform's only sanctioned way to shape that body. Both type names
/// are named here in documentation/example code only; this project takes no compiled reference to
/// <c>SharedKernel.Presentation.WebApi</c> in either direction.
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
    /// A service that also references <c>SharedKernel.Presentation.WebApi</c> (<c>14.Presentation</c>)
    /// and wants an RFC 9457 <c>ProblemDetails</c>-shaped rejection body sets
    /// <see cref="RateLimiterOptions.OnRejected"/> inside <paramref name="configure"/> to extract the
    /// limiter's suggested delay via <c>context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var
    /// retryAfterMetadata)</c> (BCL <c>System.Threading.RateLimiting.MetadataName</c>) and call
    /// <c>RateLimitRejectionProblemDetails.Create(context.HttpContext, retryAfterMetadata as
    /// TimeSpan?)</c> — the platform's only sanctioned way to shape that body; hand-rolling a raw
    /// <c>ProblemDetails</c> literal instead reproduces the inline-construction anti-pattern this
    /// platform forbids everywhere else. See this package's <c>README.md</c> "Rate limiting" section
    /// for the full worked recipe, proven by a compiled test
    /// (<c>RateLimitRejectionRecipeTests</c>) via a test-only reference from the test project — this
    /// production project takes no compiled reference to <c>SharedKernel.Presentation.WebApi</c> in
    /// either direction.
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
