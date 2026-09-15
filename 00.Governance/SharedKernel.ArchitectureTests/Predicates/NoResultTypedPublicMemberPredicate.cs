using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type exposing a public property or field whose type is, or
/// contains, one of <c>SharedKernel.Primitives</c>' outcome types: <c>Result</c>, <c>Result&lt;T&gt;</c>,
/// <c>ValidationResult</c> or <c>ValidationResult&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What counts as exposing.</strong> A property with a public getter or setter, or a public field. The
/// member's type is inspected recursively, so <c>Result&lt;Guid&gt;?</c>, <c>Result[]</c> and
/// <c>IReadOnlyList&lt;ValidationResult&lt;T&gt;&gt;</c> are all caught.
/// </para>
/// <para>
/// <strong>What is deliberately allowed.</strong> Method return types and parameters. A contract type may
/// offer a static validating factory such as <c>PageRequest.Create(...)</c> returning
/// <c>ValidationResult&lt;PageRequest&gt;</c>, or a codec such as <c>PageCursor.Decode(...)</c> returning
/// <c>Result&lt;T&gt;</c>: those run inside the service, while properties and fields are what a serializer
/// puts on the wire. A dependency-level rule cannot tell the two apart, which is why this is a member-level
/// check.
/// </para>
/// <para>
/// <strong>Matching.</strong> A type matches when its namespace is <c>SharedKernel.Primitives</c> or a child of
/// it and its metadata name is one of the four above, so the check does not depend on which sub-namespace the
/// outcome types live in.
/// </para>
/// </remarks>
public sealed class NoResultTypedPublicMemberPredicate : ICustomRule
{
    private const string PrimitivesNamespace = "SharedKernel.Primitives";

    private static readonly HashSet<string> OutcomeTypeNames = new(StringComparer.Ordinal)
    {
        "Result",
        "Result`1",
        "ValidationResult",
        "ValidationResult`1",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when no public property or field of <paramref name="type"/>
    /// has an outcome type anywhere in its type; <see langword="false"/> when one does.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a public property or field exposes an outcome type;
    /// <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var property in type.Properties)
        {
            var isPublic = property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true;
            if (isPublic && ContainsOutcomeType(property.PropertyType))
                return false;
        }

        foreach (var field in type.Fields)
        {
            if (field.IsPublic && ContainsOutcomeType(field.FieldType))
                return false;
        }

        return true;
    }

    private static bool ContainsOutcomeType(TypeReference typeReference)
    {
        switch (typeReference)
        {
            case GenericInstanceType generic:
                if (IsOutcomeType(generic.ElementType))
                    return true;

                foreach (var argument in generic.GenericArguments)
                {
                    if (ContainsOutcomeType(argument))
                        return true;
                }

                return false;

            case TypeSpecification specification:
                // Arrays, by-ref, pointers and required/optional modifiers wrap an element type.
                return ContainsOutcomeType(specification.ElementType);

            default:
                return IsOutcomeType(typeReference);
        }
    }

    private static bool IsOutcomeType(TypeReference typeReference)
    {
        if (!OutcomeTypeNames.Contains(typeReference.Name))
            return false;

        var outermost = typeReference;
        while (outermost.DeclaringType is not null)
            outermost = outermost.DeclaringType;

        var ns = outermost.Namespace;
        return ns == PrimitivesNamespace
            || ns.StartsWith(PrimitivesNamespace + ".", StringComparison.Ordinal);
    }
}
