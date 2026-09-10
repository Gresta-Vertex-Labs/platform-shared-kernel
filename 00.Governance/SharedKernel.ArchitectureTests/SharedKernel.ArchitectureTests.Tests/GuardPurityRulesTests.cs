using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using SharedKernel.ArchitectureTests.Predicates;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Guards.Clauses;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="GuardPurityRules"/> and <see cref="DoesNotContainThrowIlPredicate"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>T-13 (fire path): predicate returns false for a type, inside the <c>SharedKernel.Guards</c> namespace scope, whose method body contains a throw opcode.</description></item>
///   <item><description>T-14 (pass path): the real <c>SharedKernel.Guards</c> namespace (hosted in <c>SharedKernel.Core.dll</c> since WO-082/P-508) passes — all functional-path guard methods are pure.</description></item>
///   <item><description>T-15 (exclusion): <c>Guard.Throw</c> contains throws but is excluded by the predicate.</description></item>
///   <item><description>WO-082/P-508 non-vacuity proof (direction 1): a contrived <c>IGuardClause</c> violation INSIDE the <c>SharedKernel.Guards</c> namespace still fails the rule.</description></item>
///   <item><description>WO-082/P-508 non-vacuity proof (direction 2): a contrived <c>IGuardClause</c> violation OUTSIDE the <c>SharedKernel.Guards</c> namespace (elsewhere in what is now <c>SharedKernel.Core</c>) does NOT falsely fail the rule.</description></item>
/// </list>
/// </remarks>
public class GuardPurityRulesTests
{
    // ---------------------------------------------------------------------------
    // T-13 — Fire path: predicate fails a type that contains a throw instruction
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-13: When a type's method body contains a throw IL opcode,
    /// <see cref="DoesNotContainThrowIlPredicate"/> must return <see langword="false"/>.
    /// The fixture's namespace is deliberately under <c>SharedKernel.Guards</c> — since
    /// WO-082/P-508 the predicate is namespace-scoped, so a fixture outside that namespace
    /// would be reported compliant regardless of its throw content (see the dedicated
    /// non-vacuity tests below for that direction).
    /// </summary>
    [Fact]
    public void DoesNotContainThrowIlPredicate_TypeWithThrow_ReturnsFalse()
    {
        // Arrange — compile a fixture assembly with a class whose method throws
        const string violationSource = """
            namespace SharedKernel.Guards.Fixtures
            {
                public class ViolatingGuardClause
                {
                    public object? BadMethod(object? value)
                    {
                        throw new System.ArgumentNullException(nameof(value));
                    }
                }
            }
            """;

        var tempDll = CompileFixture("ViolatingGuard", violationSource);

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(tempDll);
            var typeDefinition = assembly.MainModule.Types
                .Single(t => t.Name == "ViolatingGuardClause");

            var predicate = new DoesNotContainThrowIlPredicate();

            // Act
            var result = predicate.MeetsRule(typeDefinition);

            // Assert — throw opcode found; predicate must fail
            result.Should().BeFalse(
                because: "ViolatingGuardClause.BadMethod contains a throw IL opcode");
        }
        finally
        {
            TryDelete(tempDll);
        }
    }

    // ---------------------------------------------------------------------------
    // T-14 — Pass path: real SharedKernel.Guards assembly must pass the rule
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-14: The actual <c>SharedKernel.Guards</c> assembly, when evaluated against
    /// <see cref="GuardPurityRules.GuardAgainstMethodsMustNotThrow()"/>, must pass —
    /// all functional-path <c>Guard.Against.*</c> methods are pure (no throws).
    /// </summary>
    [Fact]
    public void GuardAgainstMethodsMustNotThrow_RealGuardsAssembly_Passes()
    {
        // Act — anchors are supplied by the caller (this package takes no SharedKernel reference)
        var conditionList = GuardPurityRules.GuardAgainstMethodsMustNotThrow(
            typeof(IGuardClause).Assembly,
            typeof(IGuardClause));
        var result = conditionList.GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "all IGuardClause-implementing types in SharedKernel.Guards use the functional path (no throws)");
    }

    // ---------------------------------------------------------------------------
    // T-15 — Exclusion: Guard.Throw is excluded and must not cause the rule to fail
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-15: <c>Guard.Throw</c> (CLR name <c>SharedKernel.Guards.Guard+Throw</c>) contains
    /// throw statements, but the predicate excludes it by full type name.
    /// The rule must pass even though <c>Guard.Throw</c> throws.
    /// </summary>
    [Fact]
    public void DoesNotContainThrowIlPredicate_GuardThrowClass_IsExcluded()
    {
        // Arrange — load the real Guards assembly via Mono.Cecil
        var guardsAssemblyPath = typeof(SharedKernel.Guards.Clauses.IGuardClause).Assembly.Location;
        guardsAssemblyPath.Should().NotBeNullOrEmpty(
            because: "SharedKernel.Guards must be loaded from disk for Mono.Cecil inspection");

        using var assembly = AssemblyDefinition.ReadAssembly(guardsAssemblyPath);

        // Find the Guard+Throw nested class — Mono.Cecil uses '/' for nested types.
        // Guard is a top-level type in SharedKernel.Guards namespace; Throw is nested inside it.
        var guardType = assembly.MainModule.Types
            .SingleOrDefault(t => t.Namespace == "SharedKernel.Guards" && t.Name == "Guard");

        guardType.Should().NotBeNull(because: "Guard type must exist in SharedKernel.Guards");

        var guardThrow = guardType!.NestedTypes
            .SingleOrDefault(t => t.Name == "Throw");

        guardThrow.Should().NotBeNull(
            because: "Guard.Throw nested class must exist in SharedKernel.Guards");

        var predicate = new DoesNotContainThrowIlPredicate();

        // Act — the predicate should exclude Guard+Throw and return true
        var result = predicate.MeetsRule(guardThrow!);

        // Assert
        result.Should().BeTrue(
            because: "Guard.Throw is excluded by full type name from the purity check");
    }

    /// <summary>
    /// Additional T-15 variant: the full rule invoked via
    /// <see cref="GuardPurityRules.GuardAgainstMethodsMustNotThrow()"/> passes
    /// despite Guard.Throw containing throws — because NetArchTest's
    /// <c>ImplementInterface</c> filter only selects types that implement
    /// <c>IGuardClause</c>, and <c>Guard.Throw</c> does not implement it.
    /// This test confirms the overall rule passes on the real assembly.
    /// </summary>
    [Fact]
    public void GuardAgainstMethodsMustNotThrow_WithGuardThrowPresent_RulePasses()
    {
        // Confirm Guard.Throw exists and has throw statements (pre-condition)
        var guardsAssemblyPath = typeof(SharedKernel.Guards.Clauses.IGuardClause).Assembly.Location;
        using var assembly = AssemblyDefinition.ReadAssembly(guardsAssemblyPath);

        var guardType2 = assembly.MainModule.Types
            .SingleOrDefault(t => t.Namespace == "SharedKernel.Guards" && t.Name == "Guard");
        var guardThrow = guardType2?.NestedTypes.SingleOrDefault(t => t.Name == "Throw");

        guardThrow.Should().NotBeNull(because: "Guard.Throw must exist");

        var hasThrows = guardThrow!.Methods
            .Where(m => m.Body is not null)
            .SelectMany(m => m.Body.Instructions)
            .Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);

        hasThrows.Should().BeTrue(
            because: "Guard.Throw must contain throw instructions — this validates the exclusion is necessary");

        // Act — the overall rule should still pass because Guard.Throw does not implement IGuardClause
        var conditionList = GuardPurityRules.GuardAgainstMethodsMustNotThrow(
            typeof(IGuardClause).Assembly,
            typeof(IGuardClause));
        var result = conditionList.GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "Guard.Throw is not an IGuardClause implementor, so the rule's scope excludes it naturally");
    }

    // ---------------------------------------------------------------------------
    // WO-082/P-508 — non-vacuity proof for the SharedKernel.Guards namespace re-scoping
    // ---------------------------------------------------------------------------
    //
    // SharedKernel.Guards was merged into SharedKernel.Core, an assembly that now also hosts
    // unrelated types (base exceptions, BCL/railway extensions). GuardPurityRules'
    // GuardAgainstMethodsMustNotThrow is re-scoped (via DoesNotContainThrowIlPredicate) to the
    // SharedKernel.Guards namespace specifically, so it never polices any of that unrelated
    // surface. The two tests below prove BOTH directions of that re-scoping are real, not
    // vacuous: a violation inside the namespace is still caught, and a violation outside the
    // namespace is genuinely ignored rather than accidentally never being reachable.

    /// <summary>
    /// Non-vacuity direction 1: an <c>IGuardClause</c> implementor whose throwing method sits
    /// INSIDE the <c>SharedKernel.Guards</c> namespace must still fail
    /// <see cref="GuardPurityRules.GuardAgainstMethodsMustNotThrow(Assembly)"/> after the
    /// WO-082/P-508 re-scoping — the namespace filter must not have accidentally swallowed the
    /// rule's own real fire path.
    /// </summary>
    [Fact]
    public void GuardAgainstMethodsMustNotThrow_ViolationInsideGuardsNamespace_RuleFails()
    {
        // Arrange — a fixture-local IGuardClause (matched by NetArchTest via full type name,
        // the same technique DomainGoldStandardRulesTests uses for IDomainService) with an
        // implementor whose namespace starts with SharedKernel.Guards and whose method throws.
        const string violationSource = """
            namespace SharedKernel.Guards.Clauses
            {
                public interface IGuardClause { }
            }

            namespace SharedKernel.Guards.NonVacuityFixture
            {
                public sealed class InNamespaceViolatingGuardClause
                    : SharedKernel.Guards.Clauses.IGuardClause
                {
                    public void BadMethod()
                    {
                        throw new System.InvalidOperationException("in-namespace violation");
                    }
                }
            }
            """;

        var tempDll = CompileFixture("InNamespaceGuardViolation", violationSource);

        try
        {
            var fixtureAssembly = Assembly.LoadFrom(tempDll);

            // Act
            var conditionList = GuardPurityRules.GuardAgainstMethodsMustNotThrow(
                fixtureAssembly,
                typeof(IGuardClause));
            var result = conditionList.GetResult();

            // Assert — still caught: the namespace re-scoping did not swallow the real fire path
            result.IsSuccessful.Should().BeFalse(
                because: "InNamespaceViolatingGuardClause lives under SharedKernel.Guards and throws, "
                    + "so the namespace-scoped rule must still flag it");
        }
        finally
        {
            TryDelete(tempDll);
        }
    }

    /// <summary>
    /// Non-vacuity direction 2: an <c>IGuardClause</c> implementor whose throwing method sits
    /// OUTSIDE the <c>SharedKernel.Guards</c> namespace (i.e., elsewhere in what is now
    /// <c>SharedKernel.Core</c>) must NOT fail
    /// <see cref="GuardPurityRules.GuardAgainstMethodsMustNotThrow(Assembly)"/> — proving the
    /// rule genuinely stops at the <c>SharedKernel.Guards</c> namespace boundary instead of
    /// still judging the rest of the merged assembly by coincidence.
    /// </summary>
    [Fact]
    public void GuardAgainstMethodsMustNotThrow_ViolationOutsideGuardsNamespace_RulePasses()
    {
        // Arrange — same fixture-local IGuardClause, but the implementor now lives in a
        // namespace that does NOT start with SharedKernel.Guards, mirroring an unrelated
        // SharedKernel.Core namespace (e.g. SharedKernel.Core.Exceptions/.Extensions).
        const string violationSource = """
            namespace SharedKernel.Guards.Clauses
            {
                public interface IGuardClause { }
            }

            namespace SharedKernel.Core.NonVacuityFixture
            {
                public sealed class OutsideNamespaceViolatingGuardClause
                    : SharedKernel.Guards.Clauses.IGuardClause
                {
                    public void BadMethod()
                    {
                        throw new System.InvalidOperationException("outside-namespace violation");
                    }
                }
            }
            """;

        var tempDll = CompileFixture("OutsideNamespaceGuardViolation", violationSource);

        try
        {
            var fixtureAssembly = Assembly.LoadFrom(tempDll);

            // Act
            var conditionList = GuardPurityRules.GuardAgainstMethodsMustNotThrow(
                fixtureAssembly,
                typeof(IGuardClause));
            var result = conditionList.GetResult();

            // Assert — NOT caught: out of the SharedKernel.Guards namespace scope, even though
            // it implements IGuardClause and throws
            result.IsSuccessful.Should().BeTrue(
                because: "OutsideNamespaceViolatingGuardClause lives outside SharedKernel.Guards, "
                    + "so the namespace-scoped rule must not judge it even though it implements "
                    + "IGuardClause and throws");
        }
        finally
        {
            TryDelete(tempDll);
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles C# source to a temp DLL and returns the file path.
    /// Uses Roslyn for in-memory compilation; writes to a temp file because
    /// Mono.Cecil requires a physical path.
    /// </summary>
    private static string CompileFixture(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(
                Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using var fs = File.OpenWrite(tempPath);
        var emitResult = compilation.Emit(fs);

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

        return tempPath;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
