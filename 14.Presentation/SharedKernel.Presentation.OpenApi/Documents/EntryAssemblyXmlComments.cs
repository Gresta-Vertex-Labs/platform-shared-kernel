using System.Reflection;
using Asp.Versioning.OpenApi.Transformers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>
/// Points Asp.Versioning's XML-comments transformer at the service's own documentation file.
/// </summary>
/// <remarks>
/// Asp.Versioning looks for the XML file of the assembly that called its <c>AddOpenApi()</c> before the entry
/// assembly's, and that caller is this package. Wherever this package's own XML file sits next to its assembly — a
/// project reference, or <c>CopyDocumentationFilesFromPackages</c> — the service's summaries would silently vanish
/// from its documents. Asp.Versioning registers its transformer only when none is registered, so registering this one
/// first keeps the lookup on the entry assembly, searched in the same places: next to the assembly, in the content
/// root, in the application base directory.
/// </remarks>
internal static class EntryAssemblyXmlComments
{
    /// <summary>Creates the transformer for the entry assembly's XML file; an empty one when there is none.</summary>
    public static XmlCommentsTransformer CreateTransformer(IServiceProvider services) =>
        new(FindPath(Assembly.GetEntryAssembly(), services.GetRequiredService<IHostEnvironment>().ContentRootPath, AppContext.BaseDirectory));

    /// <summary>Returns the path of <paramref name="assembly"/>'s XML documentation file, or an empty string.</summary>
    internal static string FindPath(Assembly? assembly, string? contentRootPath, string? baseDirectory)
    {
        if (assembly?.GetName().Name is not { Length: > 0 } name)
        {
            return string.Empty;
        }

        var fileName = name + ".xml";

        // Empty for an assembly loaded from bytes or bundled into a single-file application.
        string? assemblyDirectory = string.IsNullOrEmpty(assembly.Location) ? null : Path.GetDirectoryName(assembly.Location);

        foreach (var directory in new[] { assemblyDirectory, contentRootPath, baseDirectory })
        {
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            var path = Path.Join(directory, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }
}
