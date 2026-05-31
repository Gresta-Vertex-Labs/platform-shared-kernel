using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type containing a non-trivial method.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.ContractsPurityRules"/> to enforce that contracts assemblies
/// contain only pure DTOs with no domain logic.
/// </para>
/// <para>
/// A method is considered trivial (and thus allowed) if it satisfies any of the following:
/// </para>
/// <list type="bullet">
///   <item><description><c>IsConstructor</c> — constructors are always allowed.</description></item>
///   <item><description><c>IsGetter</c> or <c>IsSetter</c> — property accessors are allowed.</description></item>
///   <item><description><c>IsSpecialName</c> and name starts with <c>"op_"</c> — static operators are allowed.</description></item>
///   <item><description>Name is <c>ToString</c>, <c>Equals</c>, or <c>GetHashCode</c> — standard object overrides are allowed.</description></item>
/// </list>
/// <para>
/// Any method that does not meet any of the above conditions is non-trivial and causes the
/// predicate to return <see langword="false"/> with the offending type name and method name.
/// </para>
/// </remarks>
public sealed class NoNonTrivialMethodsPredicate : ICustomRule
{
    private static readonly HashSet<string> AllowedMethodNames = new(
        System.StringComparer.Ordinal)
    {
        "ToString",
        "Equals",
        "GetHashCode",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) if the type contains no non-trivial methods;
    /// <see langword="false"/> when the first non-trivial method is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a non-trivial method is found;
    /// <see langword="true"/> when all methods are trivial.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!IsTrivialMethod(method))
                return false;
        }

        return true;
    }

    private static bool IsTrivialMethod(MethodDefinition method)
    {
        // Constructors are always trivial
        if (method.IsConstructor)
            return true;

        // Property getters and setters are trivial
        if (method.IsGetter || method.IsSetter)
            return true;

        // Static operators (IsSpecialName + name starts with "op_") are trivial
        if (method.IsSpecialName && method.Name.StartsWith("op_", System.StringComparison.Ordinal))
            return true;

        // Standard object overrides are trivial
        if (AllowedMethodNames.Contains(method.Name))
            return true;

        // Compiler-generated methods (e.g., record <Clone>$ and synthesised Deconstruct etc.)
        if (IsCompilerGenerated(method))
            return true;

        // Record/compiler special methods: Deconstruct, PrintMembers, <Clone>$
        if (method.Name.StartsWith("<", System.StringComparison.Ordinal))
            return true;

        if (method.Name == "Deconstruct" || method.Name == "PrintMembers")
            return true;

        return false;
    }

    private static bool IsCompilerGenerated(MethodDefinition method)
    {
        if (!method.HasCustomAttributes)
            return false;

        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.FullName ==
                "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
            {
                return true;
            }
        }

        return false;
    }
}
