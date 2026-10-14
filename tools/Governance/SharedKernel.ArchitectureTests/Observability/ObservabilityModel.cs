namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// One observability finding: which rule, in which domain, assembly and site, and why.
/// </summary>
/// <param name="Rule">The rule id, for example <c>eventid.shared-block</c> or <c>template.no-terminal-period</c>; <c>unscanned</c> for an assembly that could not be read.</param>
/// <param name="Domain">The domain id from the root <c>CLAUDE.md</c> table (for example <c>06.Persistence</c>); the longest folder prefix of the project.</param>
/// <param name="Assembly">The assembly name.</param>
/// <param name="Site">Where: <c>Namespace.Type::Method</c>, a constant name, or <c>-</c> for an assembly-level finding.</param>
/// <param name="Detail">What is wrong, with the value involved.</param>
public sealed record ObservabilityFinding(string Rule, string Domain, string Assembly, string Site, string Detail);

/// <summary>
/// An assembly to inspect: its name, the repository-relative path of its project (which decides its domain) and the built file.
/// </summary>
/// <param name="AssemblyName">The assembly name, for example <c>SharedKernel.Storage.S3</c>.</param>
/// <param name="ProjectPath">The repository-relative path of the project file, with either separator.</param>
/// <param name="AssemblyFile">The built DLL, or <see langword="null"/> when it does not exist (reported as <c>unscanned</c>).</param>
public sealed record ScanTarget(string AssemblyName, string ProjectPath, string? AssemblyFile);
