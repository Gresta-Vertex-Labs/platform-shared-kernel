using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type — no exemption permitted — that
/// consumes <c>ITemporalRawClientAccessor</c> as a constructor parameter or a field.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo"/> to
/// mechanize gate 3 of <c>src/Infrastructure/Workflows/CLAUDE.md</c>'s own three-gate <c>ITemporalRawClientAccessor</c>
/// escape-hatch discipline verbatim: "A 00.Governance architecture test asserts no type inside this
/// repo consumes it." The accessor is the genuine last resort for Visibility API queries, schedules,
/// namespace administration, and Nexus operations this package deliberately does not model — but it
/// bypasses tenant scoping and workflow-id composition entirely (stated IN CAPITALS on the accessor's
/// own XML doc), so it must never be a dependency of any type living inside this platform's own
/// mono-repo.
/// </para>
/// <para>
/// <strong>No exemption</strong> — mirrors <c>GrpcNeverReferencesContracts</c>'s "no exemption
/// permitted" precedent. No exemption is needed for the accessor's own DI-registration wiring code
/// either: that code PRODUCES an <c>ITemporalRawClientAccessor</c> instance (via a factory delegate
/// passed to a DI registration call) rather than CONSUMING one as a constructor/field dependency, so
/// it is never a false positive under this constructor/field-only detection technique.
/// </para>
/// <para>
/// Detection: iterates <see cref="TypeDefinition.Methods"/> where
/// <see cref="MethodDefinition.IsConstructor"/> is <see langword="true"/> and checks each
/// <c>ParameterDefinition.ParameterType</c>.<see cref="MemberReference.Name"/> for an exact
/// match against <c>"ITemporalRawClientAccessor"</c>; separately iterates
/// <see cref="TypeDefinition.Fields"/> and checks each <c>FieldDefinition.FieldType</c>.<see cref="MemberReference.Name"/>
/// for the same exact match — reusing the established Mono.Cecil constructor-parameter and field
/// inspection pattern already used throughout this file (e.g.
/// <see cref="NoEncryptionRotationJobInjectionPredicate"/>, <see cref="NoDbContextTransactionInApplicationPredicate"/>).
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>class VisibilityQueryService(ITemporalRawClientAccessor accessor) { }</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong> consuming microservice code injects <c>IWorkflowDispatcher</c>/
/// <c>IWorkflowHandle</c>; only a consuming microservice's own composition root — after calling
/// <c>.AllowRawClientAccess()</c> — may construct-inject the accessor, and even then this rule (which
/// scans the SharedKernel mono-repo's own assemblies, never a consuming service's) is not the
/// mechanism that verifies that opt-in.
/// </para>
/// </remarks>
public sealed class NoRawClientAccessorConsumptionPredicate : ICustomRule
{
    private const string AccessorInterfaceName = "ITemporalRawClientAccessor";

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when <paramref name="type"/> consumes
    /// <c>ITemporalRawClientAccessor</c> as a constructor parameter or a field; <see langword="true"/>
    /// (rule met) otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when the offending type consumes the raw client accessor;
    /// <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!method.IsConstructor)
                continue;

            foreach (var parameter in method.Parameters)
            {
                if (parameter.ParameterType.Name == AccessorInterfaceName)
                    return false;
            }
        }

        foreach (var field in type.Fields)
        {
            if (field.FieldType.Name == AccessorInterfaceName)
                return false;
        }

        return true;
    }
}
