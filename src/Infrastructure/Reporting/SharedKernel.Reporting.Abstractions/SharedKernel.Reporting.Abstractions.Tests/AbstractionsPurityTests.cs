using System.Reflection;
using FluentAssertions;


namespace SharedKernel.Reporting.Abstractions.Tests;

/// <summary>
/// Compilation-level assertion that no format-library type (ClosedXML/PdfSharp/MigraDoc) is
/// reachable from <c>SharedKernel.Reporting.Abstractions</c> — the domain's hard "zero third-party"
/// invariant for this package.
/// </summary>
public sealed class AbstractionsPurityTests
{
    private static readonly string[] ForbiddenAssemblyNamePrefixes =
    [
        "ClosedXML",
        "SpreadCheetah",
        "PdfSharp",
        "MigraDoc",
        "DocumentFormat.OpenXml",
        "SixLabors",
    ];

    [Fact]
    public void Assembly_ReferencesNoFormatLibraryAssembly()
    {
        var assembly = typeof(IReportExporter<>).Assembly;

        var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

        foreach (var forbiddenPrefix in ForbiddenAssemblyNamePrefixes)
        {
            referenced.Should().NotContain(
                name => name.StartsWith(forbiddenPrefix, StringComparison.OrdinalIgnoreCase),
                $"SharedKernel.Reporting.Abstractions must never reference a '{forbiddenPrefix}*' assembly");
        }
    }

    [Fact]
    public void Assembly_OnlyReferencesAllowedSharedKernelAssemblies()
    {
        var assembly = typeof(IReportExporter<>).Assembly;

        var sharedKernelReferences = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("SharedKernel.", StringComparison.Ordinal))
            .ToArray();

        // Layering (src/Infrastructure/Reporting/CLAUDE.md): this domain may reference only 01.Core and
        // SharedKernel.Storage.Abstractions.
        sharedKernelReferences.Should().OnlyContain(
            name => name == "SharedKernel.Primitives" || name == "SharedKernel.Execution" || name == "SharedKernel.Storage.Abstractions",
            "SharedKernel.Reporting.Abstractions may reference only SharedKernel.Primitives, SharedKernel.Execution and SharedKernel.Storage.Abstractions");
    }

    [Fact]
    public void PublicTypes_NeverExposeAFormatLibraryTypeInTheirSignature()
    {
        var assembly = typeof(IReportExporter<>).Assembly;
        var publicTypes = assembly.GetExportedTypes();

        foreach (var type in publicTypes)
        {
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var member in members)
            {
                var memberTypeNames = GetMemberTypeNames(member);
                foreach (var typeName in memberTypeNames)
                {
                    ForbiddenAssemblyNamePrefixes.Should().NotContain(
                        prefix => typeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
                        $"{type.FullName}.{member.Name} must never expose a format-library type ('{typeName}')");
                }
            }
        }
    }

    private static IEnumerable<string> GetMemberTypeNames(MemberInfo member) => member switch
    {
        PropertyInfo property => [property.PropertyType.FullName ?? string.Empty],
        MethodInfo method =>
        [
            method.ReturnType.FullName ?? string.Empty,
            .. method.GetParameters().Select(p => p.ParameterType.FullName ?? string.Empty),
        ],
        _ => [],
    };
}
