using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="HealthCheckConstantsUsageRules"/> — introduced by WO-028 P-178.
/// </summary>
/// <remarks>
/// <para>
/// T-139 covers the fire path (pre-P-177 shape: bare literal duplicating a constant).
/// T-140 covers the pass path (post-P-177 shape: field access instead of a literal).
/// T-141 covers the vacuous pass path (no constants class anywhere in the assembly).
/// T-142 covers the generality check (a differently-named, unrelated-looking constants class).
/// </para>
/// <para>
/// Per the phase's Dependencies section, design/implementation proceeds against contrived
/// in-memory fixtures built via <see cref="CSharpCompilation"/> +
/// <see cref="MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray{byte})"/>
/// — the same technique used by <c>RedisTopologyRulesTests</c> and
/// <c>ServiceDefaultsGovernanceRulesTests</c>. A real-assembly re-verification pass against
/// <c>SharedKernel.ServiceDefaults</c> is a tracked, non-blocking follow-up (P-177).
/// </para>
/// </remarks>
public class HealthCheckConstantsUsageRulesTests
{
    // ---------------------------------------------------------------------------
    // T-139 — Fire path: bare literal duplicates a sibling constants class value
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-139: A contrived fixture reproducing the pre-P-177 shape — a string-constants class
    /// (named <c>WellKnownCheckNames</c>, deliberately not <c>HealthCheckTags</c>) plus a sibling
    /// method that passes a bare literal duplicating one of the class's field values to a
    /// <c>HealthCheckRegistration</c>-shaped call — must fail
    /// <see cref="HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist"/>.
    /// </summary>
    [Fact]
    public void NoBareHealthCheckLiteralWhereConstantsExist_BareLiteralDuplicatesConstant_RuleFails()
    {
        const string source = """
            namespace Fixture.ServiceDefaults
            {
                public static class WellKnownCheckNames
                {
                    public const string Database = "database";
                }

                public sealed class HealthCheckRegistration
                {
                    public HealthCheckRegistration(string name) { }
                }

                public static class HealthCheckExtensions
                {
                    public static void RegisterChecks()
                    {
                        var registration = new HealthCheckRegistration("database");
                        System.Console.WriteLine(registration);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.HealthCheckConstants.Duplicating", source);

        var result = HealthCheckConstantsUsageRules
            .NoBareHealthCheckLiteralWhereConstantsExist(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RegisterChecks passes the bare literal \"database\" which duplicates WellKnownCheckNames.Database");

        result.FailingTypeNames.Should().Contain(
            "Fixture.ServiceDefaults.HealthCheckExtensions",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-140 — Pass path: sibling method references the constant via field access
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-140: Same fixture shape as T-139, but the sibling method references the constant via
    /// field access (<c>Ldsfld</c>) instead of a literal (<c>Ldstr</c>) — must pass
    /// <see cref="HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist"/>.
    /// </summary>
    [Fact]
    public void NoBareHealthCheckLiteralWhereConstantsExist_FieldAccessNotLiteral_RulePasses()
    {
        const string source = """
            namespace Fixture.ServiceDefaults
            {
                public static class WellKnownCheckNames
                {
                    public static readonly string Database = "database";
                }

                public sealed class HealthCheckRegistration
                {
                    public HealthCheckRegistration(string name) { }
                }

                public static class HealthCheckExtensions
                {
                    public static void RegisterChecks()
                    {
                        var registration = new HealthCheckRegistration(WellKnownCheckNames.Database);
                        System.Console.WriteLine(registration);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.HealthCheckConstants.FieldAccess", source);

        var result = HealthCheckConstantsUsageRules
            .NoBareHealthCheckLiteralWhereConstantsExist(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "RegisterChecks references WellKnownCheckNames.Database via field access, not a bare literal");
    }

    // ---------------------------------------------------------------------------
    // T-141 — Pass path (vacuous): no string-constants class anywhere in the assembly
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-141: A contrived fixture containing health-check registration call sites with bare
    /// literals but NO string-constants class anywhere in the assembly must pass
    /// <see cref="HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist"/>
    /// vacuously — nothing to enforce yet.
    /// </summary>
    [Fact]
    public void NoBareHealthCheckLiteralWhereConstantsExist_NoConstantsClassInAssembly_RulePassesVacuously()
    {
        const string source = """
            namespace Fixture.ServiceDefaults
            {
                public sealed class HealthCheckRegistration
                {
                    public HealthCheckRegistration(string name) { }
                }

                public static class HealthCheckExtensions
                {
                    public static void RegisterChecks()
                    {
                        var registration = new HealthCheckRegistration("database");
                        System.Console.WriteLine(registration);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.HealthCheckConstants.NoConstantsClass", source);

        var result = HealthCheckConstantsUsageRules
            .NoBareHealthCheckLiteralWhereConstantsExist(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "no string constants class exists in the assembly — there is nothing yet to enforce");
    }

    // ---------------------------------------------------------------------------
    // T-142 — Generality check: a differently-named, unrelated-looking constants class
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-142: A second contrived fixture using a differently-named and differently-shaped
    /// constants class (<c>WidgetRegistrationNames</c>, unrelated to health checks by name) plus
    /// a sibling health-check call site duplicating one of its values must still fail
    /// <see cref="HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist"/> —
    /// confirming the predicate does not rely on any hardcoded class-name string.
    /// </summary>
    [Fact]
    public void NoBareHealthCheckLiteralWhereConstantsExist_UnrelatedlyNamedConstantsClass_RuleStillFires()
    {
        const string source = """
            namespace Fixture.ServiceDefaults
            {
                public static class WidgetRegistrationNames
                {
                    public static readonly string Primary = "widget-primary";
                }

                public sealed class HealthCheckRegistration
                {
                    public HealthCheckRegistration(string name) { }
                }

                public static class HealthCheckExtensions
                {
                    public static void RegisterChecks()
                    {
                        var registration = new HealthCheckRegistration("widget-primary");
                        System.Console.WriteLine(registration);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.HealthCheckConstants.Generality", source);

        var result = HealthCheckConstantsUsageRules
            .NoBareHealthCheckLiteralWhereConstantsExist(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RegisterChecks passes the bare literal \"widget-primary\" which duplicates WidgetRegistrationNames.Primary, regardless of the unrelated class name");

        result.FailingTypeNames.Should().Contain(
            "Fixture.ServiceDefaults.HealthCheckExtensions",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for reflection.
    /// Follows the established pattern from <c>RedisTopologyRulesTests</c> and
    /// <c>ServiceDefaultsGovernanceRulesTests</c>.
    /// </summary>
    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Console").Location),
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
