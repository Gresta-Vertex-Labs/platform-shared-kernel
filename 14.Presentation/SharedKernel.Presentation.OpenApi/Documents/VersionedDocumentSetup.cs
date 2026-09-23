using Asp.Versioning.OpenApi;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.OpenApi.Options;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>
/// Adds the platform transformers to the document of each API version. Asp.Versioning runs every
/// <see cref="IConfigureOptions{TOptions}"/> of <see cref="VersionedOpenApiOptions"/> once per version, after adding
/// its own transformers, so these run after Asp.Versioning's.
/// </summary>
internal sealed class VersionedDocumentSetup : IConfigureOptions<VersionedOpenApiOptions>
{
    private readonly IOptions<SharedKernelOpenApiOptions> _options;
    private readonly IHostEnvironment _environment;

    public VersionedDocumentSetup(IOptions<SharedKernelOpenApiOptions> options, IHostEnvironment environment)
    {
        _options = options;
        _environment = environment;
    }

    /// <inheritdoc />
    public void Configure(VersionedOpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = _options.Value;
        var schemes = new SecuritySchemeSet(settings);

        options.Document.AddDocumentTransformer(new PlatformDocumentTransformer(
            DocumentText.ResolveTitle(settings, _environment),
            settings.Description,
            options.Description.ApiVersion.ToString(),
            schemes));
        options.Document.AddOperationTransformer(new PlatformOperationTransformer(schemes));
    }
}
