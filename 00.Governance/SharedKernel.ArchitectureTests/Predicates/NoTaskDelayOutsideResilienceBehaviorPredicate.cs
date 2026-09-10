using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type other than <c>ResilienceBehavior</c> whose
/// method bodies contain a <c>System.Threading.Tasks.Task.Delay</c> call — a fingerprint
/// heuristic for hand-rolled retry/backoff loops.
/// </summary>
/// <remarks>
/// <para>
/// Used by
/// <see cref="Rules.ApplicationPipelineRules.NoHandRolledRetryLoopOutsideResilienceBehavior"/>
/// to extend 05.Application's existing documented prohibition on hand-rolled
/// <c>System.Random</c>/<c>DateTime.UtcNow</c> usage to retry/backoff specifically, now that
/// <c>ResilienceBehavior</c> exists as the platform-sanctioned alternative
/// (<c>IRetryableRequest</c> + <c>ApplicationBehaviorsBuilder.AddResilienceBehavior(...)</c>).
/// </para>
/// <para>
/// <strong>Self-exemption (checked first):</strong> any type whose
/// <see cref="TypeDefinition.Name"/> is exactly <c>"ResilienceBehavior"</c> returns
/// <see langword="true"/> unconditionally — it is the platform's own sanctioned Polly v8
/// resilience pipeline and is the only type permitted to call <c>Task.Delay</c>.
/// </para>
/// <para>
/// <strong>Detection (fingerprint heuristic, not a full retry-loop detector):</strong> for all
/// other types, walks every method body's <see cref="OpCodes.Call"/> and
/// <see cref="OpCodes.Callvirt"/> instructions for an operand whose
/// <c>MethodReference.Name</c> is <c>"Delay"</c> AND whose
/// <c>MethodReference.DeclaringType</c> <see cref="MemberReference.FullName"/> is
/// <c>"System.Threading.Tasks.Task"</c>. This single check covers every <c>Task.Delay</c>
/// overload (the <c>int</c>-millisecond and <c>TimeSpan</c> forms alike), because both the
/// declaring type and method name must match — avoiding false positives on unrelated
/// <c>Delay</c> methods on other types.
/// </para>
/// <para>
/// <c>Task.Delay</c> is the one IL-detectable signal common to virtually every hand-rolled
/// retry/backoff loop; a legitimate non-retry <c>Task.Delay</c> call elsewhere in
/// <c>05.Application</c> would also be flagged (none is known to exist at the time this
/// predicate was introduced).
/// </para>
/// <para>
/// Returns <see langword="false"/> (rule violated) on the first match found. The failure
/// message names the offending type, method, and the <c>Task.Delay</c> call site.
/// </para>
/// </remarks>
public sealed class NoTaskDelayOutsideResilienceBehaviorPredicate : ICustomRule
{
    private const string ResilienceBehaviorTypeName = "ResilienceBehavior";
    private const string TaskDelayMethodName = "Delay";
    private const string TaskDeclaringTypeFullName = "System.Threading.Tasks.Task";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when <paramref name="type"/> is named
    /// <c>ResilienceBehavior</c>, or when no method body in the type contains a
    /// <c>Task.Delay</c> call. Returns <see langword="false"/> (rule violated) otherwise.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop.
    /// </param>
    public bool MeetsRule(TypeDefinition type)
    {
        if (type.Name == ResilienceBehaviorTypeName)
            return true;

        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call
                    && instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is not MethodReference methodRef)
                    continue;

                if (methodRef.Name != TaskDelayMethodName)
                    continue;

                if (methodRef.DeclaringType?.FullName == TaskDeclaringTypeFullName)
                    return false;
            }
        }

        return true;
    }
}
