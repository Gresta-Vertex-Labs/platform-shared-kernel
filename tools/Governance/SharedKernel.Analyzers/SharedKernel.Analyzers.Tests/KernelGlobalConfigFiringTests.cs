using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Firing proof for the kernel self-analysis: the real <c>tools/Governance/SelfAnalysis/kernel.globalconfig</c> is applied to a
/// compilation as the analyzer-config source, and a deliberate violation of each rule then reports as an error. SK0034 stays a suggestion.
/// </summary>
public sealed partial class KernelGlobalConfigFiringTests
{
    private const string Stubs = """
        namespace Microsoft.Extensions.Logging
        {
            public enum LogLevel { Debug, Information, Warning }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class LoggerMessageAttribute : System.Attribute
            {
                public LoggerMessageAttribute(int eventId, LogLevel level, string message) { }
            }
        }

        namespace SharedKernel.Primitives.Errors
        {
            public sealed class Error { public Error(string code, string message) { Code = code; Message = message; } public string Code { get; } public string Message { get; } }
        }
        """;

    public static TheoryData<string, string> Violations() => new()
    {
        { "SK0001", "public class A { public System.DateTime Now() => System.DateTime.UtcNow; }" },
        {
            "SK0043",
            "public sealed class B : System.Exception { public B(SharedKernel.Primitives.Errors.Error error) : base(error.Message) { Error = error; } public SharedKernel.Primitives.Errors.Error Error { get; } }"
        },
        {
            "SK0044",
            "public static partial class C { [Microsoft.Extensions.Logging.LoggerMessage(1234, Microsoft.Extensions.Logging.LogLevel.Information, \"Ran.\")] public static partial void Ran(); }"
        },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public async Task Rule_WithTheRealKernelConfig_FailsAsAnError(string id, string source)
    {
        var analyzer = Analyzer(id);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
            .WithSpecificDiagnosticOptions(KernelSeverities());
        var compilation = CSharpCompilation.Create(
            "KernelConfigFiring",
            [CSharpSyntaxTree.ParseText(Stubs), CSharpSyntaxTree.ParseText(source)],
            References(),
            options);

        var diagnostics = await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync();

        var hits = diagnostics.Where(d => d.Id == id).ToList();
        Assert.NotEmpty(hits);
        Assert.All(hits, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void Sk0034_StaysASuggestion_AndEveryOtherRuleIsAnError()
    {
        var severities = KernelSeverities();

        Assert.Equal(ReportDiagnostic.Info, severities["SK0034"]);
        Assert.All(severities.Where(p => p.Key != "SK0034"), p => Assert.Equal(ReportDiagnostic.Error, p.Value));
        Assert.True(severities.ContainsKey("CA2200"));
    }

    private static DiagnosticAnalyzer Analyzer(string id) =>
        typeof(AnalyzerBase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .Single(a => a.SupportedDiagnostics.Any(d => d.Id == id));

    private static ImmutableDictionary<string, ReportDiagnostic> KernelSeverities()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var path = Path.Combine(directory!.FullName, "tools", "Governance", "SelfAnalysis", "kernel.globalconfig");
        var builder = ImmutableDictionary.CreateBuilder<string, ReportDiagnostic>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            var match = SeverityLine().Match(line);
            if (!match.Success)
                continue;

            builder[match.Groups["id"].Value] = match.Groups["severity"].Value switch
            {
                "error" => ReportDiagnostic.Error,
                "warning" => ReportDiagnostic.Warn,
                "suggestion" => ReportDiagnostic.Info,
                _ => ReportDiagnostic.Suppress,
            };
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<MetadataReference> References() =>
        (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [])
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));

    [GeneratedRegex(@"^dotnet_diagnostic\.(?<id>\w+)\.severity\s*=\s*(?<severity>\w+)\s*$")]
    private static partial Regex SeverityLine();
}
