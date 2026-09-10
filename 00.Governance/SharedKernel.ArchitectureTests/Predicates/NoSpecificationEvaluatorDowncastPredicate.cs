using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type whose method bodies contain a
/// <c>castclass</c> IL instruction targeting a type whose name starts with
/// <c>"SpecificationEvaluator"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.EfCorePackageHygieneRules"/> to enforce that no code in the
/// <c>SharedKernel.Persistence.EfCore</c> assembly performs a concrete downcast of
/// <c>ISpecificationEvaluator&lt;T&gt;</c> to its concrete implementation. Adding
/// <c>GetProjectedQuery</c> to the interface specifically to eliminate the downcast; this
/// rule ensures the pattern cannot silently regress.
/// </para>
/// <para>
/// The check is a simple operand name prefix match — no type-hierarchy walk or semantic
/// model is required. The rule fires on any <c>castclass</c> whose target
/// <see cref="TypeReference.Name"/> starts with <c>"SpecificationEvaluator"</c> (case-sensitive),
/// covering both the generic form (<c>SpecificationEvaluator&lt;T&gt;</c>) and any subclass.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>var concreteEval = (SpecificationEvaluator&lt;T&gt;)_evaluator;</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>_evaluator.GetProjectedQuery(query, spec);</code>
/// </para>
/// </remarks>
public sealed class NoSpecificationEvaluatorDowncastPredicate : ICustomRule
{
    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when any method body in the type
    /// contains a <c>castclass</c> instruction whose target type name starts with
    /// <c>"SpecificationEvaluator"</c>; <see langword="true"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> if a concrete <c>SpecificationEvaluator</c> downcast is found;
    /// <see langword="true"/> when no such cast exists in any method body.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Castclass)
                    continue;

                if (instruction.Operand is TypeReference typeRef &&
                    typeRef.Name.StartsWith("SpecificationEvaluator", System.StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
