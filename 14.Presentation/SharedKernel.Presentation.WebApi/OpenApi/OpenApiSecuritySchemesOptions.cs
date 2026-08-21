namespace SharedKernel.Presentation.WebApi.OpenApi;

/// <summary>
/// Configures which OpenAPI security schemes <see cref="OpenApiExtensions.AddSharedKernelOpenApi"/>
/// registers on the generated document(s).
/// </summary>
/// <remarks>
/// <para>
/// Each individually active scheme is registered as its <b>own separate</b> OpenAPI security
/// requirement object — OR semantics, meaning a request satisfies <i>any one</i> active mechanism —
/// never a single combined requirement object, which would mean simultaneous/AND semantics. This is
/// the opposite of this package's usual AND-across-attributes composition rule elsewhere
/// (<c>[RequireRole]</c>/<c>[RequirePermission]</c>) and is easy to get backwards.
/// </para>
/// <para>
/// <see cref="ApiKeyHeaderName"/> stays a plain configurable string — this package does not take a
/// <c>ProjectReference</c> on <c>SharedKernel.Security.ApiKey</c>/<c>.Mtls</c> merely to reuse a
/// header-name constant. The consuming service's own composition root (which already references
/// whichever concrete <c>12.Security</c> provider it uses) is responsible for passing the matching
/// value explicitly, consistent with this package's existing "<c>IUserContext</c>/Abstractions only,
/// no concrete provider reference" posture.
/// </para>
/// </remarks>
public sealed class OpenApiSecuritySchemesOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the Bearer JWT security scheme is registered.
    /// Defaults to <see langword="true"/> — unchanged from this package's original unconditional
    /// Bearer-only behavior.
    /// </summary>
    public bool Bearer { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an API-key security scheme is registered. Defaults
    /// to <see langword="false"/> (opt-in).
    /// </summary>
    public bool ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the header name documented for the API-key security scheme when
    /// <see cref="ApiKey"/> is enabled. Defaults to <c>"X-Api-Key"</c>.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Gets or sets a value indicating whether the OpenAPI 3.1 <c>mutualTLS</c> security scheme is
    /// registered. Defaults to <see langword="false"/> (opt-in).
    /// </summary>
    public bool MutualTls { get; set; }
}
