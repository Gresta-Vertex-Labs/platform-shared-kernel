using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (SK0707) that fails any type implementing <c>ISaga</c> whose
/// base type chain does not include <c>SagaStateBase</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.ExtendedMessagingArchitectureRules"/> to enforce that every saga
/// state class extends <c>SagaStateBase</c> (from <c>SharedKernel.Messaging.MassTransit</c>),
/// which provides the platform-standard <c>CorrelationId</c>, <c>Version</c> (optimistic
/// concurrency), <c>CreatedAt</c>, and <c>ModifiedAt</c> audit fields required for correct saga
/// persistence and version-conflict resolution.
/// </para>
/// <para>
/// <strong>Scope check:</strong> types whose <see cref="TypeDefinition.Interfaces"/> contains an
/// entry with <c>InterfaceType.Name == "ISaga"</c> (exact simple name match). Types not
/// implementing <c>ISaga</c> return <see langword="true"/> unconditionally — they are not in
/// scope.
/// </para>
/// <para>
/// <strong>Self-exemption:</strong> a type whose own <c>TypeDefinition.Name == "SagaStateBase"</c>
/// returns <see langword="true"/> immediately after the scope check — <c>SagaStateBase</c>
/// itself implements <c>ISaga</c> but cannot extend itself.
/// </para>
/// <para>
/// <strong>Detection:</strong> for each remaining <c>ISaga</c> implementor, walks the
/// <see cref="TypeDefinition.BaseType"/> chain iteratively:
/// <list type="bullet">
///   <item><description>At each step, checks whether <c>TypeReference.Name == "SagaStateBase"</c> (exact simple name match).</description></item>
///   <item><description>Advances by calling <c>BaseType.Resolve()</c> to obtain the next <see cref="TypeDefinition"/>.</description></item>
///   <item><description>Terminates when <c>BaseType</c> is <see langword="null"/> or its name is <c>"Object"</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Fail-open policy:</strong> if <c>BaseType.Resolve()</c> returns <see langword="null"/>
/// at any step (the base type lives in an assembly that was not loaded), the predicate returns
/// <see langword="true"/> (treated as possibly-compliant) to avoid false positives in
/// assembly-isolation test setups. This is a documented limitation, not an exemption — if a
/// saga state type's non-loadable base is itself a <c>SagaStateBase</c> descendant, this rule
/// will not catch a missing <c>SagaStateBase</c> further up an unresolved chain.
/// </para>
/// <para>
/// <strong>Failure message:</strong>
/// <c>"{offendingType} implements ISaga but does not extend SagaStateBase. All saga state
/// classes must extend SagaStateBase to carry correlation ID, version, and audit fields."</c>
/// </para>
/// </remarks>
public sealed class SagaStateMustExtendSagaStateBasePredicate : ICustomRule
{
    private const string SagaInterfaceName = "ISaga";
    private const string SagaStateBaseTypeName = "SagaStateBase";
    private const string ObjectTypeName = "Object";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types that do not implement
    /// <c>ISaga</c>, and for <c>ISaga</c> implementors whose base type chain includes
    /// <c>SagaStateBase</c>; <see langword="false"/> when an <c>ISaga</c> implementor's
    /// resolvable base type chain terminates without finding <c>SagaStateBase</c>.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when an <c>ISaga</c> implementor does not extend
    /// <c>SagaStateBase</c>; <see langword="true"/> otherwise (including the fail-open case).
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        if (!ImplementsSaga(type))
            return true;

        // SagaStateBase itself implements ISaga but cannot extend itself.
        if (type.Name == SagaStateBaseTypeName)
            return true;

        var baseType = type.BaseType;

        while (baseType is not null && baseType.Name != ObjectTypeName)
        {
            if (baseType.Name == SagaStateBaseTypeName)
                return true;

            var resolved = baseType.Resolve();
            if (resolved is null)
                return true; // fail-open: unresolved assembly dependency

            baseType = resolved.BaseType;
        }

        return false;
    }

    private static bool ImplementsSaga(TypeDefinition type)
    {
        if (!type.HasInterfaces)
            return false;

        foreach (var iface in type.Interfaces)
        {
            if (iface.InterfaceType.Name == SagaInterfaceName)
                return true;
        }

        return false;
    }
}
