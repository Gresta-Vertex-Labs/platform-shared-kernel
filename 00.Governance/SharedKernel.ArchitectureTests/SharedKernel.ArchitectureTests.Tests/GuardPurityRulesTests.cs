using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using SharedKernel.ArchitectureTests.Predicates;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="GuardPurityRules"/> and <see cref="DoesNotContainThrowIlPredicate"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>T-13 (fire path): predicate returns false for a type whose method body contains a throw opcode.</description></item>
///   <item><description>T-14 (pass path): the real <c>SharedKernel.Guards</c> assembly passes — all functional-path guard methods are pure.</description></item>
///   <item><description>T-15 (exclusion): <c>Guard.Throw</c> contains throws but is excluded by the predicate.</description></item>
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
    /// </summary>
    [Fact]
    public void DoesNotContainThrowIlPredicate_TypeWithThrow_ReturnsFalse()
    {
        // Arrange — compile a fixture assembly with a class whose method throws
        const string violationSource = """
            namespace GuardFixture
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
        // Act — uses typeof(IGuardClause).Assembly internally
        var conditionList = GuardPurityRules.GuardAgainstMethodsMustNotThrow();
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
        var conditionList = GuardPurityRules.GuardAgainstMethodsMustNotThrow();
        var result = conditionList.GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "Guard.Throw is not an IGuardClause implementor, so the rule's scope excludes it naturally");
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
