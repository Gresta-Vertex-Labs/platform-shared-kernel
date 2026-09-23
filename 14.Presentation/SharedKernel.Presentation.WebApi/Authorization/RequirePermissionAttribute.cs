using Microsoft.AspNetCore.Authorization;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Requires the caller to hold at least one of the given permissions. Works on MVC controllers and actions, minimal
/// APIs (<c>RequirePermission(…)</c>), SignalR hubs and hub methods, and gRPC services and methods.
/// </summary>
/// <remarks>
/// <para>
/// A native ASP.NET Core authorization policy: nothing else needs registering beyond <c>AddSharedKernelWebApi()</c>
/// (or <c>AddSharedKernelAuthorization()</c>) and <c>UseAuthorization()</c>. Permissions within one attribute are
/// alternatives (OR); several attributes on one endpoint must all be satisfied (AND).
/// </para>
/// <para>
/// Evaluated against <see cref="Security.Abstractions.IUserContext.HasPermission"/> of the caller, so the
/// authentication package decides which claims carry permissions; names compare ordinally. An anonymous caller is
/// answered 401, a caller without the permission 403 <c>forbidden.insufficient_permission</c>, whose message never
/// names the permission.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : Attribute, IAuthorizeData
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
    }

    /// <summary>Gets the permissions, any one of which is enough.</summary>
    public IReadOnlyCollection<string> Permissions { get; }

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
