using Asp.Versioning;
using SharedKernel.Configuration;

namespace SharedKernel.Presentation.OpenApi;

/// <summary>
/// Settings for the API versioning and OpenAPI documents set up by
/// <see cref="OpenApiHostBuilderExtensions.AddSharedKernelOpenApi"/>, bound from
/// <c>SharedKernel:Presentation:OpenApi</c> and validated at startup.
/// </summary>
/// <remarks>
/// <para>
/// The defaults document a service authenticated with bearer tokens, and serve the documents only in the Development
/// environment: an API description is a map of the attack surface, so publishing it is a decision, not a default.
/// </para>
/// <para>
/// The <c>configure</c> callback of <see cref="OpenApiHostBuilderExtensions.AddSharedKernelOpenApi"/> runs after
/// binding, so code can override configuration. <see cref="Versioning"/> can only be set in code.
/// </para>
/// </remarks>
public sealed class SharedKernelOpenApiOptions : ISectionBoundOptions
{
    /// <summary>Gets the configuration section these settings bind from: <c>SharedKernel:Presentation:OpenApi</c>.</summary>
    public static string SectionName => "SharedKernel:Presentation:OpenApi";

    /// <summary>
    /// Gets or sets the title of every document and of the API reference page. Defaults to the application name
    /// (<c>IHostEnvironment.ApplicationName</c>).
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the description of every document, in Markdown. The deprecation and sunset notices of a version
    /// are appended to it in that version's document.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a callback that adjusts API versioning after the platform defaults are applied: the default
    /// version (1.0), the version readers (URL segment, then the <c>X-Api-Version</c> header), and the sunset and
    /// deprecation policies, as in <c>options.Policies.Sunset(1.0).Effective(date).Link(url)</c>.
    /// </summary>
    /// <remarks>
    /// A version with a sunset policy is answered with an RFC 8594 <c>Sunset</c> HTTP-date header, a deprecated
    /// version with an RFC 9745 <c>Deprecation: @&lt;epoch seconds&gt;</c> header, and each policy link with a
    /// <c>Link</c> header. The documents describe the same policies. Code only; configuration cannot set a callback.
    /// </remarks>
    public Action<ApiVersioningOptions>? Versioning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the documents declare an HTTP bearer (JWT) security scheme. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool Bearer { get; set; } = true;

    /// <summary>
    /// Gets or sets the request header an API key is sent in, such as <c>X-Api-Key</c>, which the documents then
    /// declare as an API-key security scheme; <see langword="null"/> or empty (the default) declares none.
    /// </summary>
    public string? ApiKeyHeaderName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the documents declare a mutual TLS (client certificate) security scheme,
    /// an OpenAPI 3.1 scheme type. Defaults to <see langword="false"/>.
    /// </summary>
    public bool MutualTls { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the documents and the API reference are served outside the Development
    /// environment. Defaults to <see langword="false"/>: <c>MapSharedKernelOpenApi()</c> maps nothing there.
    /// </summary>
    /// <remarks>
    /// When the documents are exposed, protect them unless they are meant to be public, for example with
    /// <c>app.MapSharedKernelOpenApi().RequireEndpointPermission("docs.read")</c>; documents meant to be public say so with
    /// <c>.AllowAnonymous()</c>. When neither is applied and no fallback authorization policy is set, the host logs a
    /// warning when it starts.
    /// </remarks>
    public bool ExposeInProduction { get; set; }
}
