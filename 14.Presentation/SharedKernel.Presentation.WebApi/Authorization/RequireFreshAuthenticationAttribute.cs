using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Requires the caller to have authenticated recently — a step-up gate for sensitive operations such as changing
/// payout details. An <see cref="AuthorizeAttribute"/>, so it works natively wherever
/// <see cref="RequirePermissionAttribute"/> does: minimal APIs (<c>RequireFreshAuthentication(…)</c>), MVC, SignalR
/// hubs and hub methods, and gRPC.
/// </summary>
/// <remarks>
/// <para>
/// Evaluated with <see cref="Security.Abstractions.IUserContext.IsAuthenticationFresherThan"/> against the injected
/// clock. Over HTTP a signed-in caller whose authentication is older than <see cref="MaxAge"/> (or has no
/// authentication time) is answered 401 with an RFC 9470 challenge — <c>error="insufficient_user_authentication"</c>
/// and <c>max_age</c> — and the code <c>unauthorized.step_up_required</c>, telling the client to sign in again; a
/// gRPC call gets the same challenge header and ends as <c>Unauthenticated</c>. SignalR checks the attributes of a hub
/// method before any hub filter runs, so a refused invocation fails with SignalR's own <c>HubException</c> message,
/// "Failed to invoke '…' because user is unauthorized", and the method never runs.
/// </para>
/// <para>
/// The constructor fixes the requirement: <see cref="Policy"/> and <see cref="Roles"/> are read-only here.
/// <see cref="AuthorizeAttribute.AuthenticationSchemes"/> can be set, as on <c>[Authorize]</c>, to choose the schemes
/// that authenticate the caller; it adds to the policy and never replaces the requirement.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireFreshAuthenticationAttribute : AuthorizeAttribute, IAuthorizeData
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
        base.Policy = _policy;
    }

    /// <summary>Gets the oldest acceptable authentication.</summary>
    public TimeSpan MaxAge { get; }

    /// <summary>Gets the name of the policy ASP.NET Core evaluates for this attribute, encoded from its maximum age.</summary>
    /// <remarks>Read-only: it hides the setter of <see cref="AuthorizeAttribute.Policy"/>, which would replace the requirement.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new string Policy => _policy;

    /// <summary>
    /// Always <see langword="null"/>: roles are required with <see cref="RequireRoleAttribute"/>, evaluated through
    /// <c>IUserContext</c>.
    /// </summary>
    /// <remarks>
    /// Read-only: it hides the setter of <see cref="AuthorizeAttribute.Roles"/>, whose check reads role claims directly
    /// and ignores how the authentication package maps roles.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new string? Roles => null;

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
}
