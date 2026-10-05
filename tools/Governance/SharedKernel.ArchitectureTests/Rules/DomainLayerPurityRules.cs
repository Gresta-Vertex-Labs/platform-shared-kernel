using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that protect the architectural integrity of
/// <c>03.Domain</c>: no infrastructure references, no domain event handlers, no direct
/// system-clock usage, and no infrastructure-typed constructor parameters on domain services.
/// </summary>
/// <remarks>
/// <para>
/// All four factory methods accept an <see cref="Assembly"/> parameter and return a
/// <see cref="ConditionList"/> — consistent with the established <c>ArchitectureRuleBase</c> API.
/// </para>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class DomainLayerPurityRules
{
    /// <summary>
    /// Forbidden infrastructure assembly name substrings for Rule 1.
    /// NetArchTest's <c>NotHaveDependencyOn</c> performs substring matching against
    /// referenced assembly full names.
    /// </summary>
    private static readonly string[] ForbiddenInfrastructureTerms =
    [
        "EntityFramework",
        "MassTransit",
        "Redis",
        "RabbitMQ",
    ];

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied domain
    /// assembly has a dependency on any infrastructure assembly.
    /// </summary>
    /// <remarks>
    /// Forbidden assembly name substrings: <c>EntityFramework</c>, <c>MassTransit</c>,
    /// <c>Redis</c>, <c>RabbitMQ</c>. Uses iterative
    /// <c>.Should().NotHaveDependencyOn(term)</c> calls — one per forbidden term.
    /// </remarks>
    /// <param name="domainAssembly">The domain assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no infrastructure dependency is present.
    /// </returns>
    public static ConditionList DomainAssembliesNeverReferenceInfrastructure(Assembly domainAssembly)
    {
        var predicate = Types
            .InAssembly(domainAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(ForbiddenInfrastructureTerms[0]);

        for (int i = 1; i < ForbiddenInfrastructureTerms.Length; i++)
        {
            predicate = predicate
                .And()
                .NotHaveDependencyOn(ForbiddenInfrastructureTerms[i]);
        }

        return predicate;
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied domain
    /// assembly implements <c>IDomainEventHandler&lt;TEvent&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Event handlers belong in <c>05.Application</c> or <c>07.Messaging</c> — never in
    /// <c>03.Domain</c>. Uses <see cref="DoesNotImplementOpenGenericInterfacePredicate"/>
    /// which inspects <c>TypeDefinition.Interfaces</c> for entries whose name starts with
    /// <c>"IDomainEventHandler"</c>.
    /// </para>
    /// </remarks>
    /// <param name="domainAssembly">The domain assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no event handler implementations are present.
    /// </returns>
    public static ConditionList DomainAssembliesNeverContainEventHandlers(Assembly domainAssembly) =>
        Types
            .InAssembly(domainAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new DoesNotImplementOpenGenericInterfacePredicate("IDomainEventHandler"));

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no method in the supplied domain
    /// assembly calls <c>DateTime.UtcNow</c>, <c>DateTime.Now</c>,
    /// <c>DateTimeOffset.UtcNow</c>, or <c>DateTimeOffset.Now</c> directly.
    /// </summary>
    /// <remarks>
    /// Only <c>IClock.UtcNow</c> is the permitted time source in domain and application
    /// assemblies. Uses <see cref="DoesNotCallSystemClockPredicate"/> which walks
    /// <c>MethodDefinition.Body.Instructions</c> via Mono.Cecil IL inspection.
    /// </remarks>
    /// <param name="domainAssembly">The domain assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no direct system-clock calls are present.
    /// </returns>
    public static ConditionList DomainAssembliesNeverCallSystemClock(Assembly domainAssembly) =>
        Types
            .InAssembly(domainAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new DoesNotCallSystemClockPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type implementing
    /// <c>IDomainService</c> in the supplied assembly has constructor parameters whose types
    /// are in forbidden infrastructure namespaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Forbidden namespace prefixes: <c>Microsoft.EntityFrameworkCore</c>, <c>MassTransit</c>,
    /// <c>StackExchange.Redis</c>, <c>RabbitMQ.Client</c>.
    /// </para>
    /// <para>
    /// Uses <see cref="NoInfrastructureConstructorParametersPredicate"/> scoped to
    /// <c>IDomainService</c> implementors only.
    /// </para>
    /// </remarks>
    /// <param name="domainAssembly">The domain assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting domain services have no infrastructure
    /// constructor parameters.
    /// </returns>
    public static ConditionList DomainServicesHaveNoInfrastructureConstructorParameters(
        Assembly domainAssembly) =>
        Types
            .InAssembly(domainAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoInfrastructureConstructorParametersPredicate());
}
