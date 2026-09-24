using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using SharedKernel.Presentation.WebApi.Authorization;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Requires the caller to have authenticated with at least one of the given methods (<c>amr</c> values such as
/// <c>mfa</c>, <c>otp</c> or <c>hwk</c>). An <see cref="AuthorizeAttribute"/>, so it works natively wherever
/// <see cref="RequirePermissionAttribute"/> does: minimal APIs (<c>RequireAuthenticationMethod(…)</c>), MVC, SignalR
/// hubs and hub methods, and gRPC.
/// </summary>
/// <remarks>
/// <para>
/// Evaluated with <see cref="Security.Abstractions.IUserContext.WasAuthenticatedWith"/>; methods within the attribute
/// are alternatives (OR). Over HTTP a signed-in caller without any of them is answered 401 with an RFC 9470
/// <c>insufficient_user_authentication</c> challenge and the code <c>unauthorized.step_up_required</c>; a gRPC call
/// gets the same challenge header and ends as <c>Unauthenticated</c>. SignalR checks the attributes of a hub method
/// before any hub filter runs, so a refused invocation fails with SignalR's own <c>HubException</c> message, "Failed
/// to invoke '…' because user is unauthorized", and the method never runs.
/// </para>
/// <para>
/// The constructor fixes the requirement: <see cref="Policy"/> and <see cref="Roles"/> are read-only here.
/// <see cref="AuthorizeAttribute.AuthenticationSchemes"/> can be set, as on <c>[Authorize]</c>, to choose the schemes
/// that authenticate the caller; it adds to the policy and never replaces the requirement.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireAuthenticationMethodAttribute : AuthorizeAttribute, IAuthorizeData
{
    private readonly string _policy;

    /// <summary>Initializes a new instance of the <see cref="RequireAuthenticationMethodAttribute"/> class.</summary>
    /// <param name="methods">The authentication methods, any one of which is enough.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="methods"/> is empty, or contains a null, empty or white-space value, or a <c>|</c>.
    /// </exception>
    public RequireAuthenticationMethodAttribute(params string[] methods)
    {
        var values = SharedKernelPolicyNames.ValidateValues(methods, nameof(methods));
        Methods = values;
        _policy = SharedKernelPolicyNames.ForAuthenticationMethods(values);
        base.Policy = _policy;
    }

    /// <summary>Gets the authentication methods, any one of which is enough.</summary>
    public IReadOnlyCollection<string> Methods { get; }

    /// <summary>Gets the name of the policy ASP.NET Core evaluates for this attribute, encoded from its methods.</summary>
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
