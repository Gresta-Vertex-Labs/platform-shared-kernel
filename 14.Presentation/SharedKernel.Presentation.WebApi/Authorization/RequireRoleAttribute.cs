using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Requires the caller to hold at least one of the given roles. An <see cref="AuthorizeAttribute"/>, so it works
/// natively wherever ASP.NET Core authorizes: minimal APIs (<c>RequireRole(…)</c>), MVC controllers and actions,
/// SignalR hubs and hub methods, and gRPC services and methods.
/// </summary>
/// <remarks>
/// <para>
/// A native ASP.NET Core authorization policy. Roles within one attribute are alternatives (OR); several attributes
/// on one endpoint must all be satisfied (AND).
/// </para>
/// <para>
/// Evaluated against <see cref="Security.Abstractions.IUserContext.HasRole"/>, never against
/// <see cref="System.Security.Claims.ClaimTypes.Role"/> the way <c>[Authorize(Roles = …)]</c> does, which ignores how
/// the authentication package maps roles. Names compare ordinally. Over HTTP an anonymous caller is answered 401, a
/// caller without the role 403, whose message never names the role; a gRPC call ends as <c>Unauthenticated</c> or
/// <c>PermissionDenied</c>. SignalR checks the attributes of a hub method before any hub filter runs, so a refused
/// invocation fails with SignalR's own <c>HubException</c> message, "Failed to invoke '…' because user is
/// unauthorized", and the method never runs.
/// </para>
/// <para>
/// The constructor fixes the requirement: <see cref="Policy"/> and <see cref="Roles"/> are read-only here.
/// <see cref="AuthorizeAttribute.AuthenticationSchemes"/> can be set, as on <c>[Authorize]</c>, to choose the schemes
/// that authenticate the caller; it adds to the policy and never replaces the requirement.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequireRoleAttribute : AuthorizeAttribute, IAuthorizeData
{
    private readonly string _policy;

    /// <summary>Initializes a new instance of the <see cref="RequireRoleAttribute"/> class.</summary>
    /// <param name="roles">The roles, any one of which is enough.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="roles"/> is empty, or contains a null, empty or white-space value, or a <c>|</c>.
    /// </exception>
    public RequireRoleAttribute(params string[] roles)
    {
        var values = SharedKernelPolicyNames.ValidateValues(roles, nameof(roles));
        Roles = values;
        _policy = SharedKernelPolicyNames.ForRoles(values);
        base.Policy = _policy;
    }

    /// <summary>Gets the roles, any one of which is enough.</summary>
    /// <remarks>
    /// Hides <see cref="AuthorizeAttribute.Roles"/>, which stays <see langword="null"/>: ASP.NET Core would check that
    /// one against role claims directly. The roles here are part of <see cref="Policy"/> and evaluated through
    /// <c>IUserContext</c>.
    /// </remarks>
    public new IReadOnlyCollection<string> Roles { get; }

    /// <summary>Gets the name of the policy ASP.NET Core evaluates for this attribute, encoded from its roles.</summary>
    /// <remarks>Read-only: it hides the setter of <see cref="AuthorizeAttribute.Policy"/>, which would replace the requirement.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new string Policy => _policy;

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
