using Microsoft.AspNetCore.Authorization;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Requires the caller to hold at least one of the given roles. Works on MVC controllers and actions, minimal APIs
/// (<c>RequireRole(…)</c>), SignalR hubs and hub methods, and gRPC services and methods.
/// </summary>
/// <remarks>
/// <para>
/// A native ASP.NET Core authorization policy. Roles within one attribute are alternatives (OR); several attributes
/// on one endpoint must all be satisfied (AND).
/// </para>
/// <para>
/// Evaluated against <see cref="Security.Abstractions.IUserContext.HasRole"/>, never against
/// <see cref="System.Security.Claims.ClaimTypes.Role"/> the way <c>[Authorize(Roles = …)]</c> does, which ignores how
/// the authentication package maps roles. Names compare ordinally. An anonymous caller is answered 401, a caller
/// without the role 403, whose message never names the role.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequireRoleAttribute : Attribute, IAuthorizeData
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
    }

    /// <summary>Gets the roles, any one of which is enough.</summary>
    public IReadOnlyCollection<string> Roles { get; }

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
