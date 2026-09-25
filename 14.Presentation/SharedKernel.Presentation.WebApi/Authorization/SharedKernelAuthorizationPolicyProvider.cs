using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Builds the policy behind each <c>SharedKernel:</c> policy name the attributes produce, and leaves every other name
/// — policies a service registers itself, the default and the fallback policy — to the provider it decorates: the
/// service's own <see cref="IAuthorizationPolicyProvider"/> when one was registered first, otherwise ASP.NET Core's
/// default provider.
/// </summary>
/// <remarks>
/// Every generated policy requires an authenticated user, so an anonymous caller is challenged (401) before any
/// permission, role or freshness check runs.
/// </remarks>
internal sealed class SharedKernelAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly IAuthorizationPolicyProvider _inner;
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new(StringComparer.Ordinal);

    public SharedKernelAuthorizationPolicyProvider(IAuthorizationPolicyProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The platform's policies never change, so caching is allowed exactly when the decorated provider allows it.
    /// </remarks>
    public bool AllowsCachingPolicies => _inner.AllowsCachingPolicies;

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!SharedKernelPolicyNames.IsSharedKernelPolicy(policyName))
        {
            return _inner.GetPolicyAsync(policyName);
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
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _inner.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _inner.GetFallbackPolicyAsync();
}
