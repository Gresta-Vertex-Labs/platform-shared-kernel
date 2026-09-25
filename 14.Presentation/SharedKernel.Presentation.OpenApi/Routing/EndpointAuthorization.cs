using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharedKernel.Presentation.OpenApi.Routing;

/// <summary>What the metadata of an endpoint says about authorizing its requests.</summary>
internal enum EndpointAccess
{
    /// <summary>No authorization metadata: a fallback policy authorizes the requests when one is set; otherwise nothing does.</summary>
    Unspecified,

    /// <summary>Authorization metadata: every request must be authorized.</summary>
    Authorized,

    /// <summary><see cref="IAllowAnonymous"/>: no request is authorized, whatever else the metadata says.</summary>
    Anonymous,
}

/// <summary>Reads endpoint metadata the way ASP.NET Core's authorization middleware does.</summary>
internal static class EndpointAuthorization
{
    /// <summary>
    /// Returns <see cref="EndpointAccess.Anonymous"/> when the metadata holds <see cref="IAllowAnonymous"/>;
    /// otherwise <see cref="EndpointAccess.Authorized"/> when it holds <see cref="IAuthorizeData"/>, an
    /// <see cref="AuthorizationPolicy"/> or <see cref="IAuthorizationRequirementData"/>; otherwise
    /// <see cref="EndpointAccess.Unspecified"/>.
    /// </summary>
    public static EndpointAccess GetAccess(IEnumerable<object> metadata)
    {
        var access = EndpointAccess.Unspecified;

        foreach (var item in metadata)
        {
            switch (item)
            {
                case IAllowAnonymous:
                    return EndpointAccess.Anonymous;
                case IAuthorizeData or AuthorizationPolicy or IAuthorizationRequirementData:
                    access = EndpointAccess.Authorized;
                    break;
            }
        }

        return access;
    }

    /// <summary>
    /// Returns <see langword="true"/> when requests to an endpoint with <paramref name="metadata"/> are authorized: it
    /// has authorization metadata, or has none and a fallback policy covers it.
    /// </summary>
    public static bool IsAuthorized(IEnumerable<object> metadata, IServiceProvider services) => GetAccess(metadata) switch
    {
        EndpointAccess.Authorized => true,
        EndpointAccess.Unspecified => HasFallbackPolicy(services),
        _ => false,
    };

    /// <summary>
    /// Returns <see langword="true"/> when a fallback policy is set, which authorizes every endpoint without
    /// authorization metadata and without <see cref="IAllowAnonymous"/>.
    /// </summary>
    public static bool HasFallbackPolicy(IServiceProvider services) =>
        services.GetService<IOptions<AuthorizationOptions>>()?.Value.FallbackPolicy is not null;
}
