using System.Runtime.CompilerServices;
using FluentAssertions;

namespace SharedKernel.Search.ElasticSearch.Tests;

/// <summary>
/// T-25: sibling-independence regression check at this domain's own test level — proves this package's
/// own source and test files never take a using-directive dependency on its sibling provider package.
/// </summary>
/// <remarks>
/// This is a lightweight, local, line-based text scan — the authoritative architecture-test enforcement
/// is <c>00.Governance</c>'s own <c>SearchTopologyRules</c>, outside this domain's jurisdiction. This
/// test exists so a regression is caught immediately inside this domain's own test suite, without
/// waiting on a separate governance test run. The scan matches only actual <c>using</c>-directive lines
/// (trimmed-line prefix match) so it can never false-positive on its own descriptive prose — including
/// this very doc comment.
/// </remarks>
public sealed class SiblingIndependenceTests
{
    private const string ForbiddenNamespace = "SharedKernel.Search.Meilisearch";

    [Fact]
    public void ElasticSearchPackage_SourceAndTests_NeverReference_SiblingProviderNamespace()
    {
        var packageRoot = GetElasticSearchPackageRoot();
        var forbiddenUsingLine = "using " + ForbiddenNamespace;

        var offendingFiles = Directory
            .EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsNotBuildOutputPath)
            .Where(path => File.ReadLines(path).Any(line => line.TrimStart().StartsWith(forbiddenUsingLine, StringComparison.Ordinal)))
            .ToList();

        offendingFiles.Should().BeEmpty(
            $"SharedKernel.Search.ElasticSearch and its own test project must never reference the sibling " +
            $"provider namespace (sibling-package isolation rule) — offending file(s): {string.Join(", ", offendingFiles)}");
    }

    private static bool IsNotBuildOutputPath(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the <c>SharedKernel.Search.ElasticSearch</c> package root (the folder containing both
    /// the main project and this nested <c>*.Tests</c> project) from this test file's own compile-time
    /// source path — stable regardless of the machine/CI working directory the test runner uses.
    /// </summary>
    private static string GetElasticSearchPackageRoot([CallerFilePath] string testFilePath = "")
    {
        var testsProjectDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetDirectoryName(testsProjectDirectory)!;
    }
}
