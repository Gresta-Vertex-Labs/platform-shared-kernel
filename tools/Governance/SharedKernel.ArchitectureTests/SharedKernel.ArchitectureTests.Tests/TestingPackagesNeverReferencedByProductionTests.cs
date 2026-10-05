using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Locks the hard rule "16.Testing packages are never referenced by production code" for every test-helper package —
/// the lightweight core <c>SharedKernel.Testing</c>, the per-capability <c>SharedKernel.{Capability}.Testing</c>
/// packages, <c>SharedKernel.Persistence.Testing</c> and the repo-internal <c>SharedKernel.Testing.Internal</c> (P-571) —
/// at two levels: every project file of the repository, and the IL of the persistence production assemblies.
/// </summary>
/// <remarks>
/// A testing package is recognised by its tier (<c>&lt;SharedKernelTier&gt;Testing&lt;/SharedKernelTier&gt;</c>) or, for a
/// package reference, by its name (<see cref="TestingPackageName"/>), so a testing package added later is covered without
/// editing this test.
/// </remarks>
public sealed partial class TestingPackagesNeverReferencedByProductionTests
{
    [Fact]
    public void NoProductionProject_ReferencesATestingPackage()
    {
        var root = FindRepositoryRoot();
        var projects = Directory.EnumerateFiles(root.FullName, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IgnoredPath().IsMatch(p))
            .ToList();
        projects.Should().Contain(p => p.EndsWith("SharedKernel.Persistence.EfCore.csproj", StringComparison.Ordinal),
            "the repository's project files must be found for this check to mean anything");
        projects.Where(IsTestingPackage).Should().Contain(p => p.EndsWith("SharedKernel.Caching.Testing.csproj", StringComparison.Ordinal),
            "the per-capability testing packages must be recognised for this check to mean anything");

        var violations = new List<string>();
        foreach (var project in projects.Where(IsProduction))
        {
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants().Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference"))
            {
                var include = reference.Attribute("Include")?.Value ?? string.Empty;
                var referenced = Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
                var isTesting = reference.Name.LocalName == "ProjectReference"
                    ? IsTestingPackage(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include.Replace('\\', '/'))))
                    : TestingPackageName().IsMatch(referenced);
                if (isTesting)
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
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).Where(name => TestingPackageName().IsMatch(name))
            .Should().BeEmpty($"'{assembly.GetName().Name}' must not reference a testing assembly");
    }

    [Fact]
    public void CoreTestingPackage_DependsOnlyOnFoundationAndModelPackages()
    {
        var root = FindRepositoryRoot();
        var project = Path.Combine(root.FullName, "src", "Testing", "SharedKernel.Testing", "SharedKernel.Testing.csproj");
        var document = XDocument.Load(project);

        var heavyProjects = document.Descendants().Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, (e.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/'))))
            .Where(p => TierOf(p) is not ("Foundation" or "Model"))
            .Select(Path.GetFileNameWithoutExtension);
        var heavyPackages = document.Descendants().Where(e => e.Name.LocalName == "PackageReference")
            .Where(e => e.Attribute("PrivateAssets")?.Value != "all")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(id => id != "Bogus" && !AbstractionsPackage().IsMatch(id));

        heavyProjects.Should().BeEmpty("the core testing package stays lightweight: capability doubles belong in SharedKernel.{Capability}.Testing (P-571)");
        heavyPackages.Should().BeEmpty("the core testing package takes only Bogus and Microsoft.Extensions.*.Abstractions (no test framework, no infrastructure)");
    }

    private static string? TierOf(string projectPath) =>
        File.Exists(projectPath)
            ? XDocument.Load(projectPath).Descendants().FirstOrDefault(e => e.Name.LocalName == "SharedKernelTier")?.Value.Trim()
            : null;

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

    // A project of the Testing tier, or one named like a testing package.
    private static bool IsTestingPackage(string projectPath)
    {
        if (TestingPackageName().IsMatch(Path.GetFileNameWithoutExtension(projectPath)))
            return true;

        return File.Exists(projectPath)
            && XDocument.Load(projectPath).Descendants().Any(e => e.Name.LocalName == "SharedKernelTier" && e.Value.Trim() == "Testing");
    }

    // Test projects, the test-helper packages themselves, consumer-verify harnesses and samples may reference them.
    private static bool IsProduction(string projectPath)
    {
        if (IsTestingPackage(projectPath))
            return false;

        var name = Path.GetFileNameWithoutExtension(projectPath);
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

    // SharedKernel.Testing, SharedKernel.Testing.Internal, SharedKernel.{Capability}.Testing.
    [GeneratedRegex(@"^SharedKernel(\.[A-Za-z0-9]+)*\.Testing(\.Internal)?$")]
    private static partial Regex TestingPackageName();

    [GeneratedRegex(@"^Microsoft\.Extensions\.[A-Za-z.]*Abstractions$")]
    private static partial Regex AbstractionsPackage();
}
