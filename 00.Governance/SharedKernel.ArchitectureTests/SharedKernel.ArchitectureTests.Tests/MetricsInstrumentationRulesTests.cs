using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="MetricsInstrumentationRules"/> — introduced by WO-038 P-235
/// (phase key <c>SK.00.MetricsOutcomeTagAndMisregistrationGuard</c>).
/// </summary>
/// <remarks>
/// <para>
/// T-167 covers the fire path (a <c>Histogram&lt;double&gt;.Record(...)</c> call with no
/// <c>"outcome"</c> literal in the same method). T-168 covers the pass path (the same call shape
/// carrying an <c>"outcome"</c> literal).
/// </para>
/// <para>
/// Per the phase's Implementation Rule 5, this rule is designed and tested against CONTRIVED
/// in-memory fixtures ONLY — it is expected to fail against the real (not-yet-retrofitted)
/// <c>SharedKernel.Application.Behaviors</c> assembly until a companion <c>05.Application</c>
/// phase retrofits <c>MetricsBehavior&lt;,&gt;</c> (P-217) to emit the <c>"outcome"</c> tag. The
/// fixture stubs <c>System.Diagnostics.Metrics.Histogram&lt;T&gt;</c> locally rather than
/// referencing the real BCL assembly — the predicate scans for method name <c>Record</c> on a type
/// whose name starts with <c>Histogram</c>, so a local stub with the same shape is sufficient.
/// </para>
/// </remarks>
public class MetricsInstrumentationRulesTests
{
    // ---------------------------------------------------------------------------
    // T-167 — Fire path: Histogram<T>.Record with no "outcome" literal in the method
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-167: a contrived fixture with a <c>Histogram&lt;double&gt;.Record(...)</c> call and no
    /// <c>"outcome"</c> Ldstr literal in the same method must fail
    /// <see cref="MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag"/>.
    /// </summary>
    [Fact]
    public void RequestDurationRecordsIncludeOutcomeTag_RecordWithoutOutcomeLiteral_RuleFails()
    {
        const string source = """
            namespace System.Diagnostics.Metrics
            {
                public sealed class Histogram<T>
                {
                    public void Record(T value) { }
                    public void Record(T value, string tagName, object tagValue) { }
                }
            }

            namespace Fixture.Application.Behaviors
            {
                using System.Diagnostics.Metrics;

                public sealed class MetricsBehavior
                {
                    private readonly Histogram<double> _requestDuration = new Histogram<double>();

                    public void Handle()
                    {
                        _requestDuration.Record(1.0, "status", "ok");
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.MetricsInstrumentation.MissingOutcome", source);

        var result = MetricsInstrumentationRules
            .RequestDurationRecordsIncludeOutcomeTag(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "Handle records RequestDuration with a \"status\" tag but no \"outcome\" tag");

        result.FailingTypeNames.Should().Contain(
            "Fixture.Application.Behaviors.MetricsBehavior",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-168 — Pass path: Histogram<T>.Record carrying an "outcome" literal
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-168: the same fixture shape as T-167, but the <c>Record</c> call carries an
    /// <c>"outcome"</c> Ldstr literal in the same method — must pass
    /// <see cref="MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag"/>.
    /// </summary>
    [Fact]
    public void RequestDurationRecordsIncludeOutcomeTag_RecordWithOutcomeLiteral_RulePasses()
    {
        const string source = """
            namespace System.Diagnostics.Metrics
            {
                public sealed class Histogram<T>
                {
                    public void Record(T value) { }
                    public void Record(T value, string tagName, object tagValue) { }
                }
            }

            namespace Fixture.Application.Behaviors
            {
                using System.Diagnostics.Metrics;

                public sealed class MetricsBehavior
                {
                    private readonly Histogram<double> _requestDuration = new Histogram<double>();

                    public void Handle()
                    {
                        _requestDuration.Record(1.0, "outcome", "success");
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.MetricsInstrumentation.WithOutcome", source);

        var result = MetricsInstrumentationRules
            .RequestDurationRecordsIncludeOutcomeTag(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Handle records RequestDuration with an \"outcome\" tag in the same method body");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for reflection.
    /// Follows the established pattern from <c>HealthCheckConstantsUsageRulesTests</c>.
    /// </summary>
    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using var stream = new MemoryStream();

        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));

            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
        }

        stream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(tempPath, stream.ToArray());

        return Assembly.LoadFrom(tempPath);
    }
}
