using Microsoft.AspNetCore.Authorization;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Requires the caller to have authenticated with at least one of the given methods (<c>amr</c> values such as
/// <c>mfa</c>, <c>otp</c> or <c>hwk</c>). Works wherever <see cref="RequirePermissionAttribute"/> does; for minimal
/// APIs use <c>RequireAuthenticationMethod(…)</c>.
/// </summary>
/// <remarks>
/// Evaluated with <see cref="Security.Abstractions.IUserContext.WasAuthenticatedWith"/>; methods within the attribute
/// are alternatives (OR). A signed-in caller without any of them is answered 401 with an RFC 9470
/// <c>insufficient_user_authentication</c> challenge and the code <c>unauthorized.step_up_required</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireAuthenticationMethodAttribute : Attribute, IAuthorizeData
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
    }

    /// <summary>Gets the authentication methods, any one of which is enough.</summary>
    public IReadOnlyCollection<string> Methods { get; }

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
