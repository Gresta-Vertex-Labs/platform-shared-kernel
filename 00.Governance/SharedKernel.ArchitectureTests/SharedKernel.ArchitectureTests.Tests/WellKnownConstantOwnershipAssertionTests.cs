using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="WellKnownConstantOwnershipAssertion"/> — WO-042 P-264.
/// </summary>
/// <remarks>
/// T-189: Fire path — a contrived non-owning fixture assembly declaring a field whose value
/// matches a caller-supplied canonical value throws, naming the offending type/field/value, while
/// the owning-type stand-in's own identical declarations in the SAME scan are correctly excluded.
/// T-190: Pass path — a contrived fixture set where only the owning-type stand-in declares the
/// canonical values (plus an unrelated clean assembly) does not throw.
/// </remarks>
public class WellKnownConstantOwnershipAssertionTests
{
    private const string OwningCorrelationHeaderTypeFullName = "Fixture.Owner.WellKnownHeaders";
    private const string OwningBaggageKeyTypeFullName = "Fixture.Owner.WellKnownBaggageKeys";

    private static readonly IReadOnlyDictionary<string, string> CanonicalValues = new Dictionary<string, string>
    {
        ["CorrelationIdHeader"] = "X-Correlation-Id",
        ["CorrelationIdBaggageKey"] = "correlation.id",
    };

    private static readonly IReadOnlyCollection<string> OwningTypeFullNames = new[]
    {
        OwningCorrelationHeaderTypeFullName,
        OwningBaggageKeyTypeFullName,
    };

    private const string OwnerAssemblySource = """
        namespace Fixture.Owner
        {
            public static class WellKnownHeaders
            {
                public const string CorrelationId = "X-Correlation-Id";
            }

            public static class WellKnownBaggageKeys
            {
                public static readonly string CorrelationId = "correlation.id";
            }
        }
        """;

    // ---------------------------------------------------------------------------
    // T-189 — Fire path: non-owning assembly redeclares a canonical value
    // ---------------------------------------------------------------------------

    [Fact]
    public void AssertSoleDeclaration_NonOwningAssemblyRedeclaresCanonicalValue_ThrowsNamingOffendingSite()
    {
        const string rogueAssemblySource = """
            namespace Fixture.Rogue
            {
                public static class LegacyHeaderNames
                {
                    public const string CorrelationHeader = "X-Correlation-Id";
                }
            }
            """;

        var ownerAssembly = CompileInMemory("Fixture.Owner.SoleDeclaration.Owner", OwnerAssemblySource);
        var rogueAssembly = CompileInMemory("Fixture.Rogue.SoleDeclaration.Rogue", rogueAssemblySource);

        var act = () =>
            WellKnownConstantOwnershipAssertion.AssertSoleDeclaration(
                CanonicalValues,
                OwningTypeFullNames,
                new[] { ownerAssembly, rogueAssembly }
            );

        act.Should()
            .Throw<InvalidOperationException>(
                because: "Fixture.Rogue.LegacyHeaderNames independently redeclares the canonical "
                    + "\"X-Correlation-Id\" value under a different name, outside the owning type"
            )
            .WithMessage("*Fixture.Rogue.LegacyHeaderNames*")
            .WithMessage("*CorrelationHeader*")
            .WithMessage("*X-Correlation-Id*");
    }

    // ---------------------------------------------------------------------------
    // T-190 — Pass path: only the owning-type stand-in declares the canonical values
    // ---------------------------------------------------------------------------

    [Fact]
    public void AssertSoleDeclaration_OnlyOwningTypeDeclaresCanonicalValues_DoesNotThrow()
    {
        const string unrelatedCleanAssemblySource = """
            namespace Fixture.Unrelated
            {
                public static class ServiceNames
                {
                    public const string OrdersService = "orders-service";
                    public static readonly string PaymentsService = "payments-service";
                }
            }
            """;

        var ownerAssembly = CompileInMemory("Fixture.Owner.SolePass.Owner", OwnerAssemblySource);
        var cleanAssembly = CompileInMemory("Fixture.Unrelated.SolePass.Clean", unrelatedCleanAssemblySource);

        var act = () =>
            WellKnownConstantOwnershipAssertion.AssertSoleDeclaration(
                CanonicalValues,
                OwningTypeFullNames,
                new[] { ownerAssembly, cleanAssembly }
            );

        act.Should()
            .NotThrow(
                because: "only the owning-type stand-in declares the canonical values, and no "
                    + "other scanned assembly duplicates them"
            );
    }

    // ---------------------------------------------------------------------------
    // Fixture compilation helper
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> to an in-memory assembly, writes it to a uniquely-named
    /// temp file, and loads it via <see cref="Assembly.LoadFrom(string)"/> so
    /// <see cref="Assembly.Location"/> resolves to a real path — required because
    /// <see cref="WellKnownConstantOwnershipAssertion"/> loads assemblies via
    /// <c>Mono.Cecil.AssemblyDefinition.ReadAssembly(assembly.Location)</c>. Same technique as
    /// <c>LoggingEventIdIntegrityAssertionTests.CompileInMemory</c>.
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
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = Path.Combine(Path.GetTempPath(), $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())
            );
            throw new InvalidOperationException($"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
        }

        stream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(tempPath, stream.ToArray());

        return Assembly.LoadFrom(tempPath);
    }
}
