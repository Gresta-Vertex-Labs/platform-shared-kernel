using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type whose methods contain a direct call
/// to <c>DateTime.UtcNow</c>, <c>DateTime.Now</c>, <c>DateTimeOffset.UtcNow</c>, or
/// <c>DateTimeOffset.Now</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.DomainLayerPurityRules"/> to enforce that domain assemblies never
/// call the system clock directly. Only <c>IClock.UtcNow</c> is the permitted time source in
/// domain and application assemblies.
/// </para>
/// <para>
/// The check walks all <c>MethodDefinition.Body.Instructions</c> in the type and tests
/// each <c>call</c> or <c>callvirt</c> opcode operand (<see cref="MethodReference"/>) against
/// the four forbidden property getter full names.
/// </para>
/// </remarks>
public sealed class DoesNotCallSystemClockPredicate : ICustomRule
{
    private static readonly HashSet<string> ForbiddenGetterFullNames = new(
        System.StringComparer.Ordinal)
    {
        "System.DateTime System.DateTime::get_UtcNow()",
        "System.DateTime System.DateTime::get_Now()",
        "System.DateTimeOffset System.DateTimeOffset::get_UtcNow()",
        "System.DateTimeOffset System.DateTimeOffset::get_Now()",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) if no method in the type calls a forbidden
    /// system-clock property getter; <see langword="false"/> if any call is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a forbidden call is found;
    /// <see langword="true"/> when the type is free of direct system-clock usage.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call &&
                    instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is MethodReference methodRef &&
                    ForbiddenGetterFullNames.Contains(methodRef.FullName))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
