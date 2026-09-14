using System.Xml.Linq;
using SharedKernel.ServiceDefaults.Extensions;

namespace SharedKernel.ServiceDefaults.Tests.Packaging;

/// <summary>
/// Locks the composition base's dependency-free shape (WO-084). Every service on the platform
/// references this package, so anything it references is restored by every service.
/// </summary>
/// <remarks>
/// <para>
/// Before WO-084, a project referencing this package alone restored 25 SharedKernel projects and 73
/// NuGet packages — MassTransit, Azure Service Bus, Microsoft.Identity.Web, EF Core, Temporalio,
/// Quartz, StackExchange.Redis — because the base carried fourteen <c>ProjectReference</c>s. Those
/// integrations now live in <c>SharedKernel.ServiceDefaults.*</c> packages.
/// </para>
/// <para>
/// <b>Two locks, because each misses what the other catches.</b> The metadata lock inspects the
/// compiled assembly: the compiler records a reference only when a type from it is actually used,
/// so it catches integration code creeping back into the base. It cannot see a reference nothing
/// uses — which leaves no trace in the assembly yet still lands in the package's dependency list
/// and in every consumer's restore. That is exactly how <c>SharedKernel.Messaging.MassTransit</c>
/// and <c>SharedKernel.Primitives</c> survived unused on this package. The project-file lock reads
/// the <c>.csproj</c> itself and catches that case.
/// </para>
/// <para>
/// The metadata lock has a second blind spot, found by perturbation: code that uses only a
/// <c>const</c> from another package — say <c>ErrorCodes.NotFound.Default</c> — also leaves no
/// assembly reference, because the compiler copies the constant's value into the caller. The
/// <c>ProjectReference</c> is still required to compile it, so the project-file lock still fails.
/// Measured against each perturbation: an unused reference or a constant-only use trips only the
/// project-file lock; a genuine type use trips both; a heavyweight package trips only the allowlist.
/// </para>
/// </remarks>
public sealed class CompositionBaseIsolationTests
{
    private const string ProjectFileName = "SharedKernel.ServiceDefaults.csproj";

    /// <summary>
    /// The only third-party packages the base may carry. Every one is OpenTelemetry, and none brings
    /// a heavy transitive graph: the EF Core instrumentation observes EF Core's
    /// <c>DiagnosticSource</c> without depending on EF Core, and the gRPC client instrumentation
    /// depends on <c>OpenTelemetry</c> alone. Anything else belongs in an integration package.
    /// </summary>
    private static readonly string[] AllowedPackageReferences =
    [
        "OpenTelemetry.Extensions.Hosting",
        "OpenTelemetry.Instrumentation.AspNetCore",
        "OpenTelemetry.Instrumentation.Http",
        "OpenTelemetry.Instrumentation.EntityFrameworkCore",
        "OpenTelemetry.Instrumentation.Runtime",
        "OpenTelemetry.Exporter.OpenTelemetryProtocol",
        "OpenTelemetry.Instrumentation.GrpcNetClient",
    ];

    [Fact]
    public void BaseAssembly_ReferencesNoSharedKernelAssembly()
    {
        var sharedKernelReferences = typeof(ServiceDefaultsExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("SharedKernel.", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            sharedKernelReferences.Length == 0,
            "SharedKernel.ServiceDefaults must not use types from any other SharedKernel package, but its "
            + $"assembly references: {string.Join(", ", sharedKernelReferences)}. Move the code that needs "
            + "them into the matching SharedKernel.ServiceDefaults.* integration package.");
    }

    [Fact]
    public void BaseProjectFile_DeclaresNoProjectReference()
    {
        var projectReferences = LoadProjectFile()
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? "(no Include)")
            .ToArray();

        Assert.True(
            projectReferences.Length == 0,
            "SharedKernel.ServiceDefaults.csproj must declare no ProjectReference — every service restores "
            + "whatever this package references. Found: "
            + $"{string.Join(", ", projectReferences)}. An integration needing another SharedKernel package "
            + "belongs in its own SharedKernel.ServiceDefaults.* package.");
    }

    [Fact]
    public void BaseProjectFile_DeclaresOnlyAllowlistedPackageReferences()
    {
        var unexpected = LoadProjectFile()
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? "(no Include)")
            .Where(include => !AllowedPackageReferences.Contains(include, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "SharedKernel.ServiceDefaults.csproj may reference only OpenTelemetry packages, but also "
            + $"references: {string.Join(", ", unexpected)}. A package that brings a transitive graph "
            + "belongs in an integration package, where only the services that use it restore it.");
    }

    [Fact]
    public void BaseProjectFile_IsActuallyTheBasePackage()
    {
        // Guards the two project-file tests above against passing vacuously: if the walk-up found the
        // wrong file, "no ProjectReference" would prove nothing about the base.
        var project = LoadProjectFile();

        Assert.Contains(
            project.Descendants("PackageReference"),
            element => (string?)element.Attribute("Include") == "OpenTelemetry.Extensions.Hosting");
        Assert.Contains(
            project.Descendants("FrameworkReference"),
            element => (string?)element.Attribute("Include") == "Microsoft.AspNetCore.App");
    }

    private static XDocument LoadProjectFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ProjectFileName);
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Walked up from '{AppContext.BaseDirectory}' without finding '{ProjectFileName}'. This test reads "
            + "the package's own project file and cannot run without it.");
    }
}
