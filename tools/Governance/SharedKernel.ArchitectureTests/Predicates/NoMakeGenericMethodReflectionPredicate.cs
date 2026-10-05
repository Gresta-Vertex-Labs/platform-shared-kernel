using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that inspects method bodies via Mono.Cecil IL inspection
/// and fails any type whose methods contain a <c>MakeGenericMethod</c> call opcode that is
/// not explicitly registered in <see cref="ReflectionExemptionRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.ReflectionGuardRules"/> to enforce the platform-wide prohibition
/// on reflection-based generic method invocation (<c>GetMethod</c>/<c>GetMethods</c> +
/// <c>MakeGenericMethod</c> + <c>Invoke</c>)
/// </para>
/// <para>
/// <strong>Motivating incident:</strong>
/// <c>SharedKernel.Persistence.EfCore.EncryptionRotationService.LoadBatchAsync</c> shipped
/// a <c>GetMethod("LoadBatchAsync").MakeGenericMethod(entityType).Invoke(...)</c> pattern
/// while the same package's <c>CLAUDE.md</c> documented expression trees as the gold
/// standard. IL inspection is the only reliable detection mechanism because
/// <c>MethodInfo.MakeGenericMethod</c> is called at runtime on a variable of type
/// <c>MethodInfo</c> — there is no compile-time syntax pattern that captures every form.
/// </para>
/// <para>
/// <strong>Detection logic:</strong> for each <see cref="MethodDefinition"/> with a non-null
/// body, the predicate checks for <see cref="OpCodes.Call"/> or <see cref="OpCodes.Callvirt"/>
/// instructions whose <c>MethodReference.Name</c> equals <c>"MakeGenericMethod"</c>
/// (exact, case-sensitive). This name is unique to <c>System.Reflection.MethodInfo.MakeGenericMethod</c>
/// within the BCL — no namespace or declaring-type check is required.
/// </para>
/// <para>
/// <strong>Exemption:</strong> before returning <see langword="false"/> the predicate
/// consults <see cref="ReflectionExemptionRegistry.IsExempt(string, string)"/> with the
/// type's <c>FullName</c> and the method's <c>Name</c>. If exempt, the type passes. The
/// registry is the only valid mechanism for authorising a <c>MakeGenericMethod</c> use —
/// no per-call-site suppression is accepted.
/// </para>
/// <para>
/// Reuses the established Mono.Cecil <see cref="TypeDefinition"/> IL-walk pattern from
/// <see cref="DoesNotContainThrowIlPredicate"/>. No new NuGet dependency is introduced.
/// </para>
/// </remarks>
public sealed class NoMakeGenericMethodReflectionPredicate : ICustomRule
{
    private const string MakeGenericMethodName = "MakeGenericMethod";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when no method body in <paramref name="type"/>
    /// contains a <c>MakeGenericMethod</c> call opcode, or when every such call is covered by
    /// an entry in <see cref="ReflectionExemptionRegistry"/>. Returns <see langword="false"/>
    /// (rule violated) when a non-exempt <c>MakeGenericMethod</c> call is found.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop. The factory method in
    /// <see cref="Rules.ReflectionGuardRules"/> pre-filters to non-abstract types before
    /// invoking this predicate.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the type is free of non-exempt <c>MakeGenericMethod</c>
    /// calls; <see langword="false"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            // Skip methods with no IL body (abstract, extern, interface members).
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                // Only Call and Callvirt opcodes can invoke MakeGenericMethod.
                if (instruction.OpCode != OpCodes.Call
                    && instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is not MethodReference methodRef)
                    continue;

                if (methodRef.Name != MakeGenericMethodName)
                    continue;

                // Found a MakeGenericMethod call — check the exemption registry.
                if (ReflectionExemptionRegistry.IsExempt(type.FullName, method.Name))
                    continue;

                // Non-exempt violation found.
                return false;
            }
        }

        return true;
    }
}
