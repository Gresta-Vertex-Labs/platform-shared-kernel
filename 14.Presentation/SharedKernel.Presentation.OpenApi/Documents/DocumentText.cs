using System.Reflection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Presentation.OpenApi.Options;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>The title and description of the documents and of the API reference page.</summary>
internal static class DocumentText
{
    private const string ParagraphBreak = "\n\n";

    private static readonly Lazy<string?> EntryAssemblyDescription =
        new(() => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description);

    /// <summary>Returns the configured title, or the application name when none is configured.</summary>
    public static string ResolveTitle(SharedKernelOpenApiOptions options, IHostEnvironment environment) =>
        string.IsNullOrWhiteSpace(options.Title) ? environment.ApplicationName : options.Title;

    /// <summary>
    /// Returns the document description: the configured one followed by the version notices Asp.Versioning wrote, or
    /// what Asp.Versioning wrote when none is configured.
    /// </summary>
    /// <param name="configured">The configured description.</param>
    /// <param name="generated">
    /// The description as Asp.Versioning left it: the entry assembly's <see cref="AssemblyDescriptionAttribute"/>
    /// (when it has one), then the deprecation and sunset notices and policy links of the version.
    /// </param>
    public static string? ComposeDescription(string? configured, string? generated) =>
        ComposeDescription(configured, generated, EntryAssemblyDescription.Value);

    /// <summary>The testable form of <see cref="ComposeDescription(string?, string?)"/>.</summary>
    internal static string? ComposeDescription(string? configured, string? generated, string? assemblyDescription)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return string.IsNullOrWhiteSpace(generated) ? null : generated;
        }

        var notices = generated ?? string.Empty;

        // Asp.Versioning starts from the entry assembly's description, which the configured one replaces.
        if (!string.IsNullOrEmpty(assemblyDescription) && notices.StartsWith(assemblyDescription, StringComparison.Ordinal))
        {
            notices = notices[assemblyDescription.Length..];
        }

        notices = notices.TrimStart('.', ' ', '\r', '\n');

        return notices.Length == 0 ? configured : configured + ParagraphBreak + notices;
    }
}
