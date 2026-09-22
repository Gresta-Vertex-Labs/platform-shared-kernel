using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce the composition-root exclusivity contract
/// documented in <c>13.ServiceDefaults/CLAUDE.md</c>: no production assembly other than
/// <c>SharedKernel.ServiceDefaults</c>, <c>SharedKernel.MultiTenancy</c>, and the concrete
/// provider packages themselves may reference <c>SharedKernel.Persistence.EfCore</c>,
/// <c>SharedKernel.Persistence.Dapper</c>,
/// <c>SharedKernel.Messaging.MassTransit</c>, or <c>SharedKernel.Security.Oidc</c>.
///
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching"/>
/// structurally: pure <c>NetArchTest.eNt</c> dependency-graph checking via
/// <c>.Should().NotHaveDependencyOn(term)</c>, one <see cref="ConditionList"/> per forbidden
/// term — no Mono.Cecil involved.
/// </para>
/// <para>
/// <strong>Composition-root exemption list</strong> — assemblies that are allowed to
/// reference the four concrete provider terms below:
/// </para>
/// <list type="bullet">
///   <item><description><c>SharedKernel.ServiceDefaults</c> — the composition root that wires providers at startup.</description></item>
///   <item><description><c>SharedKernel.MultiTenancy</c> — the tenant-resolution composition surface.</description></item>
///   <item><description>Each of the four provider packages referencing itself trivially (<c>SharedKernel.Persistence.EfCore</c> (which since P-558 is also the PostgreSQL provider), <c>.Dapper</c>, <c>SharedKernel.Messaging.MassTransit</c>, <c>SharedKernel.Security.Oidc</c>).</description></item>
/// </list>
/// <para>
/// Any additional exemption must be documented in <c>00.Governance/CLAUDE.md</c> before it is
/// applied in code.
/// </para>
/// </remarks>
public static class CompositionRootExclusivityRules
{
    /// <summary>
    /// The four forbidden provider-package namespace terms. A type's dependency-namespace
    /// set is checked against each of these via <c>NotHaveDependencyOn</c>.
    /// </summary>
    private static readonly string[] ForbiddenProviderTerms =
    [
        "SharedKernel.Persistence.EfCore",
        "SharedKernel.Persistence.Dapper",
        "SharedKernel.Messaging.MassTransit",
        "SharedKernel.Security.Oidc",
    ];

    /// <summary>
    /// Returns one <see cref="ConditionList"/> per forbidden provider term, asserting that no
    /// type in any of the supplied <paramref name="assembliesUnderTest"/> has a dependency on
    /// that term.
    /// </summary>
    /// <param name="assembliesUnderTest">
    /// The production assemblies to check. Must NOT include <c>SharedKernel.ServiceDefaults</c>,
    /// <c>SharedKernel.MultiTenancy</c>, or any of the four concrete provider packages
    /// themselves — those are the composition-root exemption list and are expected to
    /// legitimately reference the forbidden terms. Typical callers supply
    /// <c>05.Application</c>, <c>03.Domain</c>, <c>04.Contracts</c>,
    /// <c>11.Communication.*</c>, and <c>12.Security.Abstractions</c> assemblies — every
    /// production assembly that is not the composition root and not a provider package.
    /// </param>
    /// <returns>
    /// An array of four <see cref="ConditionList"/> instances, one per forbidden term, in the
    /// order: <c>SharedKernel.Persistence.EfCore</c>, <c>.Dapper</c>,
    /// <c>SharedKernel.Messaging.MassTransit</c>, <c>SharedKernel.Security.Oidc</c>. The caller
    /// must assert <c>.GetResult().IsSuccessful</c> on each element.
    /// </returns>
    public static ConditionList[] OnlyAllowedAssembliesMayReferenceConcreteProviders(
        params Assembly[] assembliesUnderTest)
    {
        var conditionLists = new ConditionList[ForbiddenProviderTerms.Length];

        for (var i = 0; i < ForbiddenProviderTerms.Length; i++)
        {
            var term = ForbiddenProviderTerms[i];

            conditionLists[i] = Types
                .InAssemblies(assembliesUnderTest)
                .That()
                .HaveNameStartingWith(string.Empty)
                .Should()
                .NotHaveDependencyOn(term);
        }

        return conditionLists;
    }
}
