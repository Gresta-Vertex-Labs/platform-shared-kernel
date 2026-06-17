using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="ReflectionGuardRules"/> — the platform-wide reflection prohibition
/// enforcement predicates introduced by WO-024 P-153.
/// </summary>
/// <remarks>
/// T-113: Fire path — assembly containing a <c>MakeGenericMethod</c> call fails the rule.
/// T-114: Pass path — assembly using expression-tree dispatch (P-147 fix pattern) passes.
/// T-115: Exemption path — type+method registered in <see cref="ReflectionExemptionRegistry"/>
///         exempts the violation; unregistered entries remain non-exempt.
/// </remarks>
public class ReflectionGuardRulesTests
{
    // ---------------------------------------------------------------------------
    // T-113 — Fire path: assembly with MakeGenericMethod call fails the rule
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-113: A contrived assembly whose <c>GenericDispatcher.DispatchToGeneric</c> method calls
    /// <c>typeof(SomeClass).GetMethod("Foo").MakeGenericMethod(typeof(int)).Invoke(null, null)</c>
    /// must fail <see cref="ReflectionGuardRules.NoMakeGenericMethodReflection"/>, and the
    /// failure must include the offending type name.
    /// </summary>
    [Fact]
    public void NoMakeGenericMethodReflection_ViolatingAssembly_RuleFails()
    {
        const string source = """
            using System;
            using System.Reflection;

            namespace ViolationAssembly
            {
                public class SomeClass
                {
                    public void Foo<T>() { }
                }

                public class GenericDispatcher
                {
                    public void DispatchToGeneric()
                    {
                        // Offending pattern: MakeGenericMethod + Invoke
                        var method = typeof(SomeClass).GetMethod("Foo");
                        var generic = method.MakeGenericMethod(typeof(int));
                        generic.Invoke(null, null);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.Reflection.Violation", source);

        var result = ReflectionGuardRules
            .NoMakeGenericMethodReflection(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "GenericDispatcher.DispatchToGeneric calls MakeGenericMethod, which violates SK0012");

        result.FailingTypeNames.Should().Contain(
            "ViolationAssembly.GenericDispatcher",
            because: "the failure message must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-114 — Pass path: expression-tree dispatch (P-147 fix pattern) passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-114: A contrived assembly containing the P-147 fixed pattern — typed dispatch with
    /// no <c>MakeGenericMethod</c> IL call opcode — must pass
    /// <see cref="ReflectionGuardRules.NoMakeGenericMethodReflection"/> without requiring
    /// any entry in <see cref="ReflectionExemptionRegistry"/>.
    /// </summary>
    [Fact]
    public void NoMakeGenericMethodReflection_ExpressionTreeDispatch_RulePasses()
    {
        // Simulates the P-147 fix: typed dispatch with no GetMethod/MakeGenericMethod/Invoke.
        // This is the platform gold-standard alternative to reflection-based generic dispatch.
        const string cleanSource = """
            using System;

            namespace CompliantAssembly
            {
                public class EntityRecord
                {
                    public string Name { get; set; } = string.Empty;
                }

                /// <summary>
                /// Compliant dispatcher using typed delegates — no reflection.
                /// Equivalent to the P-147 fix in EncryptionRotationService.
                /// </summary>
                public class TypedDispatcher
                {
                    public void Dispatch(EntityRecord record)
                    {
                        // Pure typed dispatch — no GetMethod, no MakeGenericMethod, no Invoke.
                        ProcessRecord(record);
                    }

                    private static void ProcessRecord(EntityRecord record)
                    {
                        _ = record.Name;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.Reflection.Compliant.ExpressionTree", cleanSource);

        var result = ReflectionGuardRules
            .NoMakeGenericMethodReflection(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "TypedDispatcher uses no reflection — no MakeGenericMethod call opcode is emitted");
    }

    // ---------------------------------------------------------------------------
    // T-115 — Exemption path: registered type+method passes; unregistered fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-115: A contrived assembly with a <c>MakeGenericMethod</c> call passes
    /// <see cref="ReflectionGuardRules.NoMakeGenericMethodReflection"/> when the offending
    /// type+method is registered in <see cref="ReflectionExemptionRegistry"/>.
    /// After the exemption is removed, the rule fails again.
    /// Also verifies that <see cref="ReflectionExemptionRegistry.IsExempt"/> returns
    /// <see langword="true"/> for registered entries and <see langword="false"/> for
    /// unregistered entries.
    /// </summary>
    [Fact]
    public void NoMakeGenericMethodReflection_ExemptedTypeMethod_RulePasses()
    {
        const string source = """
            using System;
            using System.Reflection;

            namespace ExemptableAssembly
            {
                public class SomeService
                {
                    public void ExecuteGeneric()
                    {
                        var method = typeof(SomeService).GetMethod("Helper");
                        var generic = method.MakeGenericMethod(typeof(string));
                        generic.Invoke(this, null);
                    }

                    public void Helper<T>() { }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.Reflection.Exemptable", source);

        const string typeFullName = "ExemptableAssembly.SomeService";
        const string methodName = "ExecuteGeneric";

        // Verify: unregistered — rule fails.
        ReflectionExemptionRegistry.IsExempt(typeFullName, methodName)
            .Should().BeFalse(because: "no exemption has been registered yet");

        var resultBefore = ReflectionGuardRules
            .NoMakeGenericMethodReflection(assembly)
            .GetResult();

        resultBefore.IsSuccessful.Should().BeFalse(
            because: "the type calls MakeGenericMethod and is not yet exempted");

        // Register the exemption.
        ReflectionExemptionRegistry.Register(typeFullName, methodName);

        try
        {
            // Verify: IsExempt returns true for the registered pair.
            ReflectionExemptionRegistry.IsExempt(typeFullName, methodName)
                .Should().BeTrue(because: "the pair was just registered");

            // Verify: an unrelated pair is not exempt.
            ReflectionExemptionRegistry.IsExempt("OtherAssembly.OtherClass", "OtherMethod")
                .Should().BeFalse(because: "only the registered pair should be exempt");

            // Verify: rule passes with the exemption in place.
            var resultAfter = ReflectionGuardRules
                .NoMakeGenericMethodReflection(assembly)
                .GetResult();

            resultAfter.IsSuccessful.Should().BeTrue(
                because: "SomeService.ExecuteGeneric is registered in ReflectionExemptionRegistry");
        }
        finally
        {
            // Restore the registry to its empty state.
            ReflectionExemptionRegistry.Unregister(typeFullName, methodName);
        }

        // Verify: rule fails again after the exemption is removed.
        var resultRestored = ReflectionGuardRules
            .NoMakeGenericMethodReflection(assembly)
            .GetResult();

        resultRestored.IsSuccessful.Should().BeFalse(
            because: "the exemption was removed; the violation is no longer covered");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly with a unique name
    /// and loads it for reflection / NetArchTest scanning.
    /// </summary>
    /// <remarks>
    /// Follows the pattern established in <see cref="RedisTopologyRulesTests"/> and
    /// <see cref="EncryptionPatternGuardRulesTests"/>: emits to a temp file via
    /// <see cref="Assembly.LoadFrom"/> to avoid <c>CS0234</c> resolution failures that
    /// occur when chaining in-memory fixtures without file-backed assemblies.
    /// </remarks>
    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Linq.Expressions").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using var stream = new MemoryStream();

        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var errors = string.Join(
                System.Environment.NewLine,
                emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));

            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' failed to compile:{System.Environment.NewLine}{errors}");
        }

        stream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(tempPath, stream.ToArray());

        return Assembly.LoadFrom(tempPath);
    }
}
