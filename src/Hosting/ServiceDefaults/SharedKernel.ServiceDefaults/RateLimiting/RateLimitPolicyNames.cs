namespace SharedKernel.ServiceDefaults.RateLimiting;

/// <summary>
/// Well-known rate-limiting policy names used throughout this domain.
/// </summary>
/// <remarks>
/// Mirrors the <c>HealthCheckNames</c>/<c>TenantResolutionStrategyNames</c> constants-class
/// pattern.
/// </remarks>
public static class RateLimitPolicyNames
{
    /// <summary>
    /// The named policy registered by <see cref="RateLimitingExtensions.AddSharedKernelRateLimiting"/>
    /// for authentication/token/login-shaped routes. The consuming service attaches it explicitly
    /// via <c>[EnableRateLimiting(RateLimitPolicyNames.Authentication)]</c> — this domain never
    /// knows the concrete route.
    /// </summary>
    public const string Authentication = "authentication";
}
