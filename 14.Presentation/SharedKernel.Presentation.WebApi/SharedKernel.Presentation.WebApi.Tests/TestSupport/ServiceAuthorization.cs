using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>A service's own policy provider: answers <c>dynamic:</c> names and knows nothing of the platform's.</summary>
internal sealed class DynamicPolicyProvider(bool allowsCaching = true) : IAuthorizationPolicyProvider
{
    public const string Prefix = "dynamic:";

    public AuthorizationPolicy DynamicPolicy { get; } =
        new AuthorizationPolicyBuilder().RequireClaim(TestAuthentication.RoleClaim, "operator").Build();

    public AuthorizationPolicy DefaultPolicy { get; } = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    public bool AllowsCachingPolicies => allowsCaching;

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        Task.FromResult(policyName.StartsWith(Prefix, StringComparison.Ordinal) ? DynamicPolicy : null);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => Task.FromResult(DefaultPolicy);

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => Task.FromResult<AuthorizationPolicy?>(null);
}

/// <summary>A service's own result handler: marks every response it sees, then behaves like the framework's.</summary>
internal sealed class MarkingResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string Header = "X-Service-Handler";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        context.Response.Headers[Header] = "seen";
        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
