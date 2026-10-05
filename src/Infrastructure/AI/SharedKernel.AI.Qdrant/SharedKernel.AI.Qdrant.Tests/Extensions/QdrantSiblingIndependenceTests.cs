using System.Runtime.CompilerServices;
using FluentAssertions;

namespace SharedKernel.AI.Qdrant.Tests.Extensions;

/// <summary>
/// Local, project-level proof (T-07) that <c>SharedKernel.AI.Qdrant</c>'s own production source never
/// references a sibling provider package's namespace — ahead of <c>00.Governance</c>'s authoritative
/// topology rules. Scans <c>using</c>-directive <b>lines</b> (trimmed prefix), never a whole-file
/// substring search, which would false-positive on XML-doc prose mentioning a sibling package by name
/// (this file's own remarks, and several production files' remarks, do exactly that).
/// </summary>
public sealed class QdrantSiblingIndependenceTests
{
    private const string ForbiddenMilvusUsing = "using SharedKernel.AI.Milvus";
    private const string ForbiddenSemanticKernelUsing = "using SharedKernel.AI.SemanticKernel";

    [Fact]
    public void ProductionSource_NeverContainsAUsingDirectiveLine_NamingTheMilvusNamespace()
    {
        AssertNoSuchUsingLine(ForbiddenMilvusUsing);
    }

    [Fact]
    public void ProductionSource_NeverContainsAUsingDirectiveLine_NamingTheSemanticKernelNamespace()
    {
        AssertNoSuchUsingLine(ForbiddenSemanticKernelUsing);
    }

    private static void AssertNoSuchUsingLine(string forbiddenPrefix)
    {
        var offendingFiles = new List<string>();

        foreach (var file in EnumerateProductionSourceFiles())
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith(forbiddenPrefix, StringComparison.Ordinal))
                {
                    offendingFiles.Add(file);
                    break;
                }
            }
        }

        offendingFiles.Should().BeEmpty(
            $"SharedKernel.AI.Qdrant must never reference a sibling provider package's namespace via '{forbiddenPrefix}'");
    }

    /// <summary>
    /// Every <c>.cs</c> file under this package's own production project — never this test project
    /// itself (whose XML-doc remarks legitimately name the sibling packages by prose), never <c>obj/</c>
    /// build artifacts.
    /// </summary>
    private static IEnumerable<string> EnumerateProductionSourceFiles([CallerFilePath] string thisFilePath = "")
    {
        // Anchored via [CallerFilePath] so this test locates the production project relative to its own
        // location on disk rather than the process's current working directory, which varies by test
        // runner and CI invocation.
        var testProjectDirectory = Path.GetDirectoryName(Path.GetDirectoryName(thisFilePath));
        var productionProjectDirectory = testProjectDirectory is null
            ? null
            : Path.GetDirectoryName(testProjectDirectory);

        // A deterministic CI build rewrites [CallerFilePath] to the '/_/' placeholder
        // root, which is not a real directory — fall back to the test binary's location.
        if (productionProjectDirectory is null || !Directory.Exists(productionProjectDirectory))
        {
            productionProjectDirectory =
                Path.Combine(FindRepositoryRoot(), "src", "Infrastructure", "AI", "SharedKernel.AI.Qdrant");
        }

        return Directory
            .EnumerateFiles(productionProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains("SharedKernel.AI.Qdrant.Tests", StringComparison.Ordinal));
    }

    /// <summary>
    /// Walks up from the test binary to the repository root, identified by the solution file.
    /// Used when <c>[CallerFilePath]</c> is unusable — a deterministic CI build
    /// (<c>ContinuousIntegrationBuild=true</c>) makes SourceLink rewrite it to the <c>/_/</c>
    /// placeholder root, which exists in the PDB but not on disk.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repository root (Platform.SharedKernel.slnx) from "
                + AppContext.BaseDirectory);
    }
}
