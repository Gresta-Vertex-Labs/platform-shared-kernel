using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>
/// Sets the title, description and version of one version's document and adds the components every document shares:
/// the <c>ProblemDetails</c> schema and the declared security schemes.
/// </summary>
/// <remarks>
/// Runs after Asp.Versioning's own document transformer, which titles the document after the entry assembly and
/// writes the version's deprecation and sunset notices into the description; the title is replaced here and the
/// notices are kept after the configured description.
/// </remarks>
internal sealed class PlatformDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly string _title;
    private readonly string? _description;
    private readonly string _version;
    private readonly SecuritySchemeSet _schemes;

    public PlatformDocumentTransformer(string title, string? description, string version, SecuritySchemeSet schemes)
    {
        _title = title;
        _description = description;
        _version = version;
        _schemes = schemes;
    }

    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = _title;
        document.Info.Version = _version;
        document.Info.Description = DocumentText.ComposeDescription(_description, document.Info.Description);

        var components = document.Components ??= new OpenApiComponents();

        components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        components.Schemas[ProblemDetailsSchema.Id] = ProblemDetailsSchema.Create();

        foreach (var (name, scheme) in _schemes.CreateSchemes())
        {
            components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
            components.SecuritySchemes[name] = scheme;
        }

        return Task.CompletedTask;
    }
}
