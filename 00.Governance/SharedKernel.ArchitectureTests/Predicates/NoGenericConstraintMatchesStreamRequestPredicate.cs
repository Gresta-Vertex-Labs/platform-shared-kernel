using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any open-generic <c>IPipelineBehavior&lt;,&gt;</c>
/// implementor whose <c>TRequest</c> generic-parameter constraint structurally satisfies
/// the kernel's streaming contract <c>IStreamQuery&lt;TResponse&gt;</c> (<c>SharedKernel.Application.Streaming</c>).
/// </summary>
/// <remarks>
/// <para>
/// Used by
/// <see cref="Rules.ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint"/>
/// to mechanically verify the documented 05.Application design decision that none of the
/// platform's pipeline behaviors apply to the streaming query vocabulary —
/// <c>ValidationBehavior</c>'s <c>TRequest : IRequest&lt;TResponse&gt;</c> constraint does not
/// match <c>IStreamQuery&lt;TResponse&gt;</c> today, and streaming has its own behavior contract
/// (<c>IStreamPipelineBehavior&lt;,&gt;</c>), so a request behavior is never applied to a stream.
/// </para>
/// <para>
/// <strong>Scope check:</strong> types whose <see cref="TypeDefinition.Interfaces"/> contains an
/// entry with <c>InterfaceType.Name</c> starting with <c>"IPipelineBehavior"</c> (the open
/// generic <c>IPipelineBehavior`2</c>). Types outside this scope return <see langword="true"/>
/// unconditionally.
/// </para>
/// <para>
/// <strong>Detection:</strong> for each in-scope type, inspects the
/// <see cref="GenericParameter.Constraints"/> collection on the first generic parameter (the
/// <c>TRequest</c> position) for any constraint <see cref="TypeReference"/> whose
/// <see cref="MemberReference.FullName"/> matches <c>"SharedKernel.Application.Streaming.IStreamQuery`1"</c> directly, or
/// whose resolved interface closure (<see cref="TypeDefinition.Interfaces"/>, walked
/// recursively) includes it.
/// </para>
/// <para>
/// <strong>Fail-open policy:</strong> if a constraint <see cref="TypeReference"/> cannot be
/// resolved (<c>Resolve()</c> returns <see langword="null"/> — an unloaded assembly dependency),
/// that constraint is treated as non-matching and the walk continues. This mirrors the
/// fail-open policy already established by
/// the messaging architecture rules.
/// </para>
/// <para>
/// Returns <see langword="false"/> (rule violated) on the first structural match found. The
/// failure message names the offending behavior type and the matching constraint type.
/// </para>
/// </remarks>
public sealed class NoGenericConstraintMatchesStreamRequestPredicate : ICustomRule
{
    private const string PipelineBehaviorInterfaceName = "IPipelineBehavior";
    private const string StreamRequestFullName = "SharedKernel.Application.Streaming.IStreamQuery`1";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when <paramref name="type"/> does not
    /// implement the open generic <c>IPipelineBehavior&lt;,&gt;</c>, or when its
    /// <c>TRequest</c> generic-parameter constraints do not structurally satisfy
    /// <c>IStreamQuery&lt;TResponse&gt;</c>. Returns <see langword="false"/> (rule violated)
    /// otherwise.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop.
    /// </param>
    public bool MeetsRule(TypeDefinition type)
    {
        if (!ImplementsPipelineBehavior(type))
            return true;

        if (!type.HasGenericParameters || type.GenericParameters.Count == 0)
            return true;

        var requestParameter = type.GenericParameters[0];

        foreach (var constraint in requestParameter.Constraints)
        {
            if (ConstraintMatchesStreamRequest(constraint.ConstraintType))
                return false;
        }

        return true;
    }

    private static bool ImplementsPipelineBehavior(TypeDefinition type)
    {
        if (!type.HasInterfaces)
            return false;

        foreach (var iface in type.Interfaces)
        {
            if (iface.InterfaceType.Name.StartsWith(
                    PipelineBehaviorInterfaceName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConstraintMatchesStreamRequest(TypeReference constraintType) =>
        ConstraintMatchesStreamRequest(constraintType, new HashSet<string>(StringComparer.Ordinal));

    private static bool ConstraintMatchesStreamRequest(
        TypeReference typeReference,
        HashSet<string> visited)
    {
        if (OpenGenericFullName(typeReference) == StreamRequestFullName)
            return true;

        // Avoid infinite recursion across self-referential or cyclic interface closures.
        if (!visited.Add(typeReference.FullName))
            return false;

        var resolved = typeReference.Resolve();
        if (resolved is null)
            return false; // fail-open: unresolved assembly dependency

        if (!resolved.HasInterfaces)
            return false;

        foreach (var iface in resolved.Interfaces)
        {
            if (ConstraintMatchesStreamRequest(iface.InterfaceType, visited))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the open-generic-definition full name for <paramref name="typeReference"/>
    /// (e.g. <c>"SharedKernel.Application.Streaming.IStreamQuery`1"</c>), stripping any closed generic-argument list
    /// (e.g. <c>"SharedKernel.Application.Streaming.IStreamQuery`1&lt;TResponse&gt;"</c>) that Mono.Cecil's
    /// <see cref="MemberReference.FullName"/> includes for a
    /// <see cref="GenericInstanceType"/> constraint reference. Non-generic-instance
    /// references return their own <see cref="MemberReference.FullName"/> unchanged.
    /// </summary>
    private static string OpenGenericFullName(TypeReference typeReference) =>
        typeReference is GenericInstanceType genericInstance
            ? genericInstance.ElementType.FullName
            : typeReference.FullName;
}
