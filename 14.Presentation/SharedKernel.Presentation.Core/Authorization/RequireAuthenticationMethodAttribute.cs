namespace SharedKernel.Presentation.Authorization;

/// <summary>
/// Declares that an endpoint requires the caller's session to have been authenticated using at
/// least one of the specified authentication method references.
/// </summary>
/// <remarks>
/// <para>
/// Usable directly on an MVC controller/action or attached to a Minimal API endpoint via
/// <c>RouteHandlerBuilder.WithMetadata(new RequireAuthenticationMethodAttribute(...))</c> — see
/// <c>AuthorizationEndpointFilterExtensions.RequireAuthenticationMethod</c> (<c>SharedKernel.Presentation.WebApi</c>)
/// for the equivalent Minimal API sugar.
/// </para>
/// <para>
/// Methods listed within this attribute are OR'd — the caller needs any one of them, mirroring
/// <see cref="RequireRoleAttribute"/>'s composition rule. Evaluated by
/// <c>AuthorizationRequirementEndpointFilter</c> (HTTP) and <c>GrpcAuthorizationInterceptor</c> (gRPC) against
/// <c>IUserContext.WasAuthenticatedWith</c>,
/// rejecting with <see cref="SharedKernel.Primitives.Errors.Error.Forbidden(string, string)"/> (403)
/// on no match. Composes AND-across with every other attribute this filter evaluates.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireAuthenticationMethodAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequireAuthenticationMethodAttribute"/> class.
    /// </summary>
    /// <param name="methods">
    /// The set of authentication method references, any one of which satisfies this attribute
    /// (OR semantics).
    /// </param>
    public RequireAuthenticationMethodAttribute(params string[] methods)
    {
        Methods = methods;
    }

    /// <summary>
    /// Gets the authentication method references, any one of which satisfies this attribute.
    /// </summary>
    public IReadOnlyCollection<string> Methods { get; }
}
