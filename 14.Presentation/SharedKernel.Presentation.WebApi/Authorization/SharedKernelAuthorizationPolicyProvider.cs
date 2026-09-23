using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Builds the policy behind each <c>SharedKernel:</c> policy name the attributes produce, and leaves every other name
/// — policies a service registers itself, the default and the fallback policy — to ASP.NET Core's default provider.
/// </summary>
/// <remarks>
/// Every generated policy requires an authenticated user, so an anonymous caller is challenged (401) before any
/// permission, role or freshness check runs.
/// </remarks>
internal sealed class SharedKernelAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new(StringComparer.Ordinal);

    public SharedKernelAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    /// <inheritdoc />
    public bool AllowsCachingPolicies => true;

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!SharedKernelPolicyNames.IsSharedKernelPolicy(policyName))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        if (_policies.TryGetValue(policyName, out var cached))
        {
            return Task.FromResult<AuthorizationPolicy?>(cached);
        }

        if (!SharedKernelPolicyNames.TryCreateRequirement(policyName, out var requirement))
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(requirement)
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(_policies.GetOrAdd(policyName, policy));
    }

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
