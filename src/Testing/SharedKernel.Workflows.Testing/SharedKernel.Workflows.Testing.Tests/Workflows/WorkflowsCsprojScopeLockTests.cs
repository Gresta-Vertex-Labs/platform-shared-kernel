namespace SharedKernel.Testing.SelfTests.Workflows;

/// <summary>
/// Proves the <c>Workflows/</c> SCOPE LOCK (P-288/WO-046, <c>src/Testing/CLAUDE.md</c>): zero
/// references to <c>Temporalio</c> or any real Temporal SDK type anywhere in
/// <c>SharedKernel.Workflows.Testing.csproj</c> itself -- the only permitted route to <c>Temporalio.*</c> types
/// is the transitive <c>ProjectReference</c> to <c>SharedKernel.Workflows.Temporal.csproj</c>, never a
/// direct <c>PackageReference</c> from this package.
/// </summary>
public sealed class WorkflowsCsprojScopeLockTests
{
    [Fact]
    public void SharedKernelTestingCsproj_NeverDirectlyReferencesTemporalio()
    {
        var csprojPath = FindSharedKernelTestingCsproj();
        var content = File.ReadAllText(csprojPath);

        Assert.DoesNotContain("Temporalio", content, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedKernelTestingCsproj_ReferencesSharedKernelWorkflowsTemporal_ByProjectReferenceOnly()
    {
        var csprojPath = FindSharedKernelTestingCsproj();
        var content = File.ReadAllText(csprojPath);

        Assert.Contains(
            "SharedKernel.Workflows.Temporal\\SharedKernel.Workflows.Temporal.csproj",
            content,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Locates <c>src/Testing/SharedKernel.Workflows.Testing/SharedKernel.Workflows.Testing.csproj</c> by walking up from
    /// the running test assembly's own base directory until the repo's solution file is found, then
    /// resolving the known relative path -- avoids any hardcoded absolute path or build-configuration
    /// (Debug/Release) assumption.
    /// </summary>
    private static string FindSharedKernelTestingCsproj()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Platform.SharedKernel.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (Platform.SharedKernel.slnx) by walking up from " +
                $"'{AppContext.BaseDirectory}'.");
        }

        var csprojPath = Path.Combine(dir.FullName, "src", "Testing", "SharedKernel.Workflows.Testing", "SharedKernel.Workflows.Testing.csproj");
        if (!File.Exists(csprojPath))
        {
            throw new InvalidOperationException($"Expected to find '{csprojPath}' but it does not exist.");
        }

        return csprojPath;
    }
}
