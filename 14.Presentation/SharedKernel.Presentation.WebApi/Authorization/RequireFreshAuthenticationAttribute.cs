using Microsoft.AspNetCore.Authorization;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Requires the caller to have authenticated recently — a step-up gate for sensitive operations such as changing
/// payout details. Works wherever <see cref="RequirePermissionAttribute"/> does; for minimal APIs use
/// <c>RequireFreshAuthentication(…)</c>.
/// </summary>
/// <remarks>
/// Evaluated with <see cref="Security.Abstractions.IUserContext.IsAuthenticationFresherThan"/> against the injected
/// clock. A signed-in caller whose authentication is older than <see cref="MaxAge"/> (or has no authentication time)
/// is answered 401 with an RFC 9470 challenge — <c>error="insufficient_user_authentication"</c> and
/// <c>max_age</c> — and the code <c>unauthorized.step_up_required</c>, telling the client to sign in again.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireFreshAuthenticationAttribute : Attribute, IAuthorizeData
{
    private readonly string _policy;

    /// <summary>Initializes a new instance of the <see cref="RequireFreshAuthenticationAttribute"/> class.</summary>
    /// <param name="maxAgeSeconds">The oldest acceptable authentication, in seconds; greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAgeSeconds"/> is zero or negative.</exception>
    public RequireFreshAuthenticationAttribute(int maxAgeSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAgeSeconds);

        MaxAge = TimeSpan.FromSeconds(maxAgeSeconds);
        _policy = SharedKernelPolicyNames.ForFreshAuthentication(maxAgeSeconds);
    }

    /// <summary>Gets the oldest acceptable authentication.</summary>
    public TimeSpan MaxAge { get; }

    /// <inheritdoc />
    string? IAuthorizeData.Policy
    {
        get => _policy;
        set => throw new NotSupportedException(AuthorizeDataMessages.Fixed);
    }

    /// <inheritdoc />
    string? IAuthorizeData.Roles
    {
        get => null;
        set => throw new NotSupportedException(AuthorizeDataMessages.Fixed);
    }

    /// <inheritdoc />
    string? IAuthorizeData.AuthenticationSchemes
    {
        get => null;
        set => throw new NotSupportedException(AuthorizeDataMessages.Fixed);
    }
}
