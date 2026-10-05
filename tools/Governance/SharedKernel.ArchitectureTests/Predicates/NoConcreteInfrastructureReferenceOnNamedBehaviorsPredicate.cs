using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type whose simple name is in a caller-supplied
/// behavior-name set and that references a caller-supplied forbidden concrete-infrastructure
/// namespace prefix, via a field type, or a method-body <c>Call</c>/<c>Callvirt</c>/<c>Newobj</c>
/// operand declaring type.
/// </summary>
/// <remarks>
/// <para>
/// Used by
/// <see cref="Rules.ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure"/>
/// to enforce that the named pipeline behaviors (<c>TracingBehavior</c>,
/// <c>CacheInvalidationBehavior</c>) reference only abstraction
/// packages. The pipeline packages are Host tier, which the tier matrix allows to reference adapters,
/// so this is the only thing keeping these two behaviors provider-neutral.
/// </para>
/// <para>
/// <strong>Construction:</strong> both the behavior-name set and the forbidden-namespace-prefix
/// set are supplied by the caller — never hardcoded inside this predicate — mirroring the
/// caller-supplied-prefix-list convention already established by
/// <see cref="DependencyHealthChecksCarryReadyNotLivePredicate"/>.
/// </para>
/// <para>
/// <strong>Scope check:</strong> only types whose <see cref="TypeDefinition.Name"/> is an exact
/// match (case-sensitive) against an entry in the supplied behavior-name set are inspected. All
/// other types return <see langword="true"/> (not in scope) unconditionally.
/// </para>
/// <para>
/// <strong>Detection:</strong> for each in-scope type, inspects every
/// <see cref="TypeDefinition.Fields"/> entry's <see cref="FieldReference.FieldType"/> namespace,
/// and walks every method body's <see cref="OpCodes.Call"/>, <see cref="OpCodes.Callvirt"/>, and
/// <see cref="OpCodes.Newobj"/> instructions, inspecting the operand's declaring-type namespace.
/// Any namespace starting with a forbidden prefix is a violation, <em>except</em> a namespace
/// ending in <c>".Abstractions"</c> — abstraction packages remain permitted regardless of the
/// forbidden prefix they share a root with (e.g. <c>SharedKernel.Persistence.Abstractions</c> is
/// always allowed even though <c>"SharedKernel.Persistence"</c> is a forbidden prefix).
/// </para>
/// <para>
/// Returns <see langword="false"/> (rule violated) on the first match found. The failure message
/// names the offending behavior type and the forbidden namespace referenced.
/// </para>
/// </remarks>
public sealed class NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate : ICustomRule
{
    private const string AbstractionsSuffix = ".Abstractions";

    private readonly HashSet<string> _behaviorTypeNames;
    private readonly HashSet<string> _forbiddenNamespacePrefixes;

    /// <summary>
    /// Initializes the predicate with the caller-supplied behavior-name and
    /// forbidden-namespace-prefix sets.
    /// </summary>
    /// <param name="behaviorTypeNames">
    /// Exact simple type names that are in scope for this check (e.g.
    /// <c>"TracingBehavior"</c>, <c>"CacheInvalidationBehavior"</c>).
    /// </param>
    /// <param name="forbiddenNamespacePrefixes">
    /// Namespace prefixes that in-scope types must not reference, excluding any namespace
    /// ending in <c>".Abstractions"</c>.
    /// </param>
    public NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate(
        HashSet<string> behaviorTypeNames,
        HashSet<string> forbiddenNamespacePrefixes)
    {
        _behaviorTypeNames = behaviorTypeNames;
        _forbiddenNamespacePrefixes = forbiddenNamespacePrefixes;
    }

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when <paramref name="type"/> is not in the
    /// caller-supplied behavior-name set, or when it is in scope but references no forbidden
    /// concrete-infrastructure namespace. Returns <see langword="false"/> (rule violated)
    /// otherwise.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop.
    /// </param>
    public bool MeetsRule(TypeDefinition type)
    {
        if (!_behaviorTypeNames.Contains(type.Name))
            return true;

        foreach (var field in type.Fields)
        {
            if (IsForbiddenNamespace(field.FieldType.Namespace))
                return false;
        }

        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call
                    && instruction.OpCode != OpCodes.Callvirt
                    && instruction.OpCode != OpCodes.Newobj)
                {
                    continue;
                }

                if (instruction.Operand is not MethodReference methodRef)
                    continue;

                if (IsForbiddenNamespace(methodRef.DeclaringType?.Namespace))
                    return false;
            }
        }

        return true;
    }

    private bool IsForbiddenNamespace(string? @namespace)
    {
        if (string.IsNullOrEmpty(@namespace))
            return false;

        if (@namespace.EndsWith(AbstractionsSuffix, StringComparison.Ordinal))
            return false;

        foreach (var prefix in _forbiddenNamespacePrefixes)
        {
            if (@namespace.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
