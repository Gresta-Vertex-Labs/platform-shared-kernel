using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using SharedKernel.Presentation.WebApi.Authorization;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Requires the caller to hold at least one of the given permissions. An <see cref="AuthorizeAttribute"/>, so it works
/// natively wherever ASP.NET Core authorizes: minimal APIs (<c>RequirePermission(…)</c>), MVC controllers and actions,
/// SignalR hubs and hub methods, and gRPC services and methods.
/// </summary>
/// <remarks>
/// <para>
/// A native ASP.NET Core authorization policy: nothing else needs registering beyond <c>AddSharedKernelWebApi()</c>
/// (or <c>AddSharedKernelAuthorization()</c>) and <c>UseAuthorization()</c>. Permissions within one attribute are
/// alternatives (OR); several attributes on one endpoint must all be satisfied (AND).
/// </para>
/// <para>
/// Evaluated against <see cref="Security.Abstractions.IUserContext.HasPermission"/> of the caller, so the
/// authentication package decides which claims carry permissions; names compare ordinally. Over HTTP an anonymous
/// caller is answered 401, a caller without the permission 403 <c>forbidden.insufficient_permission</c>, whose message
/// never names the permission; a gRPC call ends as <c>Unauthenticated</c> or <c>PermissionDenied</c>. SignalR checks
/// the attributes of a hub method before any hub filter runs, so a refused invocation fails with SignalR's own
/// <c>HubException</c> message, "Failed to invoke '…' because user is unauthorized", and the method never runs.
/// </para>
/// <para>
/// The constructor fixes the requirement: <see cref="Policy"/> and <see cref="Roles"/> are read-only here.
/// <see cref="AuthorizeAttribute.AuthenticationSchemes"/> can be set, as on <c>[Authorize]</c>, to choose the schemes
/// that authenticate the caller; it adds to the policy and never replaces the requirement.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute, IAuthorizeData
{
    private readonly string _policy;

    /// <summary>Initializes a new instance of the <see cref="RequirePermissionAttribute"/> class.</summary>
    /// <param name="permissions">The permissions, any one of which is enough.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="permissions"/> is empty, or contains a null, empty or white-space value, or a <c>|</c>.
    /// </exception>
    public RequirePermissionAttribute(params string[] permissions)
    {
        var values = SharedKernelPolicyNames.ValidateValues(permissions, nameof(permissions));
        Permissions = values;
        _policy = SharedKernelPolicyNames.ForPermissions(values);
        base.Policy = _policy;
    }

    /// <summary>Gets the permissions, any one of which is enough.</summary>
    public IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Gets the name of the policy ASP.NET Core evaluates for this attribute, encoded from its permissions.</summary>
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
