using FluentAssertions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SharedKernel.Application.Behaviors.Authorization;

namespace SharedKernel.Application.Behaviors.Tests.Governance;

/// <summary>
/// Verifies (WO-041, T-64) that the compiled <c>SharedKernel.Application.Behaviors.dll</c>
/// contains zero <c>Call</c>/<c>Callvirt</c> IL instructions resolving to
/// <c>Microsoft.Extensions.Logging.LoggerExtensions.Log*</c>, <c>Microsoft.Extensions.Logging.ILogger::Log</c>,
/// or a hand-written <c>Microsoft.Extensions.Logging.LoggerMessage.Define*</c> delegate factory —
/// an independent, mechanical proof that this domain's own P-253 retrofit satisfies the
/// SK0020/SK0021 detection shape, regardless of whether <c>00.Governance</c> has actually wired
/// those Roslyn diagnostics against this assembly yet.
/// </summary>
/// <remarks>
/// Mirrors <c>Shared/FailureResponseFactoryReflectionShapeTests.cs</c>'s Mono.Cecil IL-inspection
/// technique — the same technique <c>00.Governance/SharedKernel.ArchitectureTests</c>'
/// <c>LoggingEventIdIntegrityAssertion</c> uses internally, applied here directly against the
/// compiled assembly rather than through that helper (which asserts <c>EventId</c> uniqueness/range
/// membership, a distinct concern from authoring-style shape).
/// </remarks>
/// <remarks>
/// <b>Generated-code exclusion:</b> the <c>[LoggerMessage]</c> source generator's own emitted
/// partial-method implementation legitimately calls <c>ILogger.Log&lt;TState&gt;</c> directly — that
/// IS the mechanism the attribute exists to auto-generate. Every method the generator emits carries
/// a <c>[System.CodeDom.Compiler.GeneratedCodeAttribute]</c>, so this test excludes methods carrying
/// that attribute from the scan — mirroring SK0020/SK0021's own
/// <c>ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)</c> guard at the Roslyn-analyzer
/// layer. This proves hand-authored code contains no forbidden call, without asserting the
/// impossible ("the generated code itself never calls ILogger.Log").
/// </remarks>
public sealed class LoggingAuthoringStyleShapeTests
{
    private static AssemblyDefinition LoadBehaviorsAssembly()
        => AssemblyDefinition.ReadAssembly(typeof(AuthorizationBehavior<,>).Assembly.Location);

    private static bool IsCompilerGenerated(MethodDefinition method)
        => method.HasCustomAttributes
            && method.CustomAttributes.Any(a =>
                a.AttributeType.Name is "GeneratedCodeAttribute" or "CompilerGeneratedAttribute");

    private static IEnumerable<(TypeDefinition Type, MethodDefinition Method, Instruction Instruction)> AllCallInstructions(
        IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            foreach (var method in type.Methods.Where(m => m.Body is not null && !IsCompilerGenerated(m)))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                        yield return (type, method, instruction);
                }
            }

            foreach (var result in AllCallInstructions(type.NestedTypes))
                yield return result;
        }
    }

    [Fact]
    public void CompiledAssembly_ContainsNoDirectILoggerExtensionMethodCalls()
    {
        using var assembly = LoadBehaviorsAssembly();

        var offending = AllCallInstructions(assembly.MainModule.Types)
            .Select(t => t.Instruction.Operand as MethodReference)
            .Where(m => m is not null)
            .Where(m => m!.DeclaringType.FullName == "Microsoft.Extensions.Logging.LoggerExtensions"
                && m.Name.StartsWith("Log", StringComparison.Ordinal))
            .Select(m => m!.FullName)
            .ToList();

        offending.Should().BeEmpty(
            "every log statement must be authored via the [LoggerMessage] source-generated partial-method "
            + "pattern, never a direct Microsoft.Extensions.Logging.LoggerExtensions.Log* call. Offending: "
            + string.Join(", ", offending));
    }

    [Fact]
    public void CompiledAssembly_ContainsNoDirectILoggerLogCalls()
    {
        using var assembly = LoadBehaviorsAssembly();

        var offending = AllCallInstructions(assembly.MainModule.Types)
            .Select(t => t.Instruction.Operand as MethodReference)
            .Where(m => m is not null)
            .Where(m => m!.DeclaringType.FullName == "Microsoft.Extensions.Logging.ILogger"
                && m.Name == "Log")
            .Select(m => m!.FullName)
            .ToList();

        offending.Should().BeEmpty(
            "every log statement must be authored via the [LoggerMessage] source-generated partial-method "
            + "pattern, never a direct ILogger.Log<TState> call. Offending: " + string.Join(", ", offending));
    }

    /// <summary>
    /// Unlike <c>ILogger.Log</c>/<c>LoggerExtensions.Log*</c>, the <c>[LoggerMessage]</c> source
    /// generator's own emitted implementation on this SDK legitimately calls
    /// <c>LoggerMessage.Define&lt;...&gt;(...)</c> to build its cached delegate — but it does so
    /// from the type's implicit static constructor (<c>.cctor</c>), assigning the result directly
    /// into a compiler-generated field (<c>[GeneratedCodeAttribute]</c>), never into a hand-rolled
    /// one. This check walks each <c>.cctor</c> pairing every <c>Call</c> to
    /// <c>LoggerMessage.Define*</c> with the immediately-following <c>Stsfld</c> target: if that
    /// field carries <c>[GeneratedCodeAttribute]</c> the pair is the generator's own machinery and
    /// is excluded; any other <c>LoggerMessage.Define*</c> call (in a <c>.cctor</c> assigning a
    /// non-generated field, or anywhere else in the assembly) is a genuine hand-rolled violation.
    /// </summary>
    [Fact]
    public void CompiledAssembly_ContainsNoHandWrittenLoggerMessageDefineDelegates()
    {
        using var assembly = LoadBehaviorsAssembly();

        var offending = new List<string>();

        foreach (var type in EnumerateAllTypes(assembly.MainModule.Types))
        {
            foreach (var method in type.Methods.Where(m => m.Body is not null))
            {
                var instructions = method.Body.Instructions;
                for (var i = 0; i < instructions.Count; i++)
                {
                    var instruction = instructions[i];
                    if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                        continue;

                    if (instruction.Operand is not MethodReference methodRef
                        || methodRef.DeclaringType.FullName != "Microsoft.Extensions.Logging.LoggerMessage"
                        || !methodRef.Name.StartsWith("Define", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // A [LoggerMessage]-generated .cctor immediately stores the Define(...) result
                    // into a compiler-generated cache field — that pairing is the sanctioned
                    // generator machinery, not a hand-rolled violation.
                    var isGeneratedCachePairing =
                        method.IsConstructor && method.IsStatic
                        && i + 1 < instructions.Count
                        && instructions[i + 1].OpCode == OpCodes.Stsfld
                        && instructions[i + 1].Operand is FieldReference fieldRef
                        && fieldRef.Resolve() is { } fieldDef
                        && fieldDef.CustomAttributes.Any(a => a.AttributeType.Name == "GeneratedCodeAttribute");

                    if (!isGeneratedCachePairing)
                        offending.Add($"{type.FullName}.{method.Name} -> {methodRef.FullName}");
                }
            }
        }

        offending.Should().BeEmpty(
            "hand-written LoggerMessage.Define<>() delegate fields must not exist — every log statement "
            + "uses the [LoggerMessage]-attributed static partial-method pattern instead (whose own "
            + "generated .cctor/field pairing is excluded as sanctioned generator machinery). Offending: "
            + string.Join(", ", offending));
    }

    private static IEnumerable<TypeDefinition> EnumerateAllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in EnumerateAllTypes(type.NestedTypes))
                yield return nested;
        }
    }
}
