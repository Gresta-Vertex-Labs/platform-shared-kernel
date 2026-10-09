using System.Reflection;
using System.Text.RegularExpressions;

namespace SharedKernel.ServiceDefaults.Tests.Packaging;

/// <summary>
/// Kernel telemetry is subscribed by the <c>SharedKernel.*</c> wildcard, never by a copied name: no string literal in the
/// base's <c>Telemetry</c> folder spells a <c>SharedKernel.</c> source or meter name except the wildcard constants in
/// <c>TelemetryNames</c>. A copied name is how the Messaging meter and the Temporal sources were once dropped.
/// </summary>
public sealed partial class NoCopiedKernelTelemetryNamesTests
{
    private const string ProjectFileName = "SharedKernel.ServiceDefaults.csproj";
    private const string TelemetryNamesFile = "TelemetryNames.cs";

    [Fact]
    public void TelemetryFolder_SpellsNoSharedKernelNameLiteral_BesideTheWildcardConstants()
    {
        var telemetryFolder = Path.Combine(FindProjectDirectory(), "Telemetry");

        var offenders = Directory.EnumerateFiles(telemetryFolder, "*.cs", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file) != TelemetryNamesFile)
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(entry => !entry.line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(entry => SharedKernelLiteral().IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.number}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheScan_CanFail_WhenALiteralIsSpelled()
    {
        Assert.Matches(SharedKernelLiteral(), "    private const string Name = \"SharedKernel.Messaging\";");
        Assert.DoesNotMatch(SharedKernelLiteral(), "    private const string Name = \"MassTransit\";");
    }

    [GeneratedRegex("\"SharedKernel\\.")]
    private static partial Regex SharedKernelLiteral();

    private static string FindProjectDirectory()
    {
        var start = typeof(NoCopiedKernelTelemetryNamesTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "SharedKernel.TestProjectDirectory")?.Value
            ?? AppContext.BaseDirectory;

        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ProjectFileName)))
                return directory.FullName;
        }

        throw new FileNotFoundException($"Walked up from '{start}' without finding '{ProjectFileName}'.");
    }
}
