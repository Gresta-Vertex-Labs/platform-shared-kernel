using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Locks the hard rule "16.Testing packages are never referenced by production code" for both test-helper packages —
/// the repo-internal <c>SharedKernel.Testing</c> and the packable <c>SharedKernel.Persistence.Testing</c> (P-558) —
/// at two levels: every project file of the repository, and the IL of the persistence production assemblies.
/// </summary>
public sealed partial class TestingPackagesNeverReferencedByProductionTests
{
    private static readonly string[] TestingPackages = ["SharedKernel.Testing", "SharedKernel.Persistence.Testing"];

    [Fact]
    public void NoProductionProject_ReferencesATestingPackage()
    {
        var root = FindRepositoryRoot();
        var projects = Directory.EnumerateFiles(root.FullName, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IgnoredPath().IsMatch(p))
            .ToList();
        projects.Should().Contain(p => p.EndsWith("SharedKernel.Persistence.EfCore.csproj", StringComparison.Ordinal),
            "the repository's project files must be found for this check to mean anything");

        var violations = new List<string>();
        foreach (var project in projects.Where(IsProduction))
        {
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants().Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference"))
            {
                var include = reference.Attribute("Include")?.Value ?? string.Empty;
                var referenced = Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
                if (TestingPackages.Contains(referenced, StringComparer.OrdinalIgnoreCase))
                    violations.Add($"{Path.GetRelativePath(root.FullName, project)} -> {referenced}");
            }
        }

        violations.Should().BeEmpty("test-helper packages are referenced by test projects only (root CLAUDE.md hard rule)");
    }

    [Theory]
    [MemberData(nameof(PersistenceProductionAssemblies))]
    public void PersistenceProductionAssemblies_NeverDependOnATestingNamespace(Assembly assembly)
    {
        var result = SharedKernelLayeringRules.TestingNeverReferencedByProduction(assembly).GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"'{assembly.GetName().Name}' must not depend on SharedKernel.Testing or SharedKernel.Persistence.Testing; offending: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>The six 06.Persistence production assemblies.</summary>
    /// <returns>One row per assembly.</returns>
    public static IEnumerable<object[]> PersistenceProductionAssemblies()
    {
        yield return [typeof(SharedKernel.Persistence.Abstractions.Context.ICrossTenantScope).Assembly];
        yield return [typeof(SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext).Assembly];
        yield return [typeof(SharedKernel.Persistence.EfCorePersistenceBuilderAuditingExtensions).Assembly];
        yield return [typeof(SharedKernel.Persistence.EfCorePersistenceBuilderEncryptionExtensions).Assembly];
        yield return [typeof(SharedKernel.Persistence.NpgsqlPersistenceExtensions).Assembly];
        yield return [typeof(SharedKernel.Persistence.Dapper.Sessions.IDbSessionFactory).Assembly];
    }

    // Test projects, the test-helper packages themselves, consumer-verify harnesses and samples may reference them.
    private static bool IsProduction(string projectPath)
    {
        var name = Path.GetFileNameWithoutExtension(projectPath);
        if (TestingPackages.Contains(name, StringComparer.OrdinalIgnoreCase))
            return false;

        if (name.EndsWith(".Tests", StringComparison.Ordinal) || name.EndsWith(".SelfTests", StringComparison.Ordinal)
            || name.EndsWith(".ConsumerVerify", StringComparison.Ordinal) || name.Contains("consumer-verify", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Benchmark", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = projectPath.Replace('\\', '/');
        return !normalized.Contains("/samples/", StringComparison.OrdinalIgnoreCase);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("Platform.SharedKernel.slnx not found above the test output directory.");
    }

    [GeneratedRegex(@"[\\/](bin|obj|nupkgs|\.git|node_modules)[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoredPath();
}
