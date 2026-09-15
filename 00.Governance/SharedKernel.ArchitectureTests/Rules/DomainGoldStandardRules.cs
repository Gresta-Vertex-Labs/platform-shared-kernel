using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that encode the domain gold-standard conventions:
/// all <c>IDomainService</c> implementors must extend the <c>DomainService</c> abstract base, and every
/// aggregate factory must create through a <c>ValidationResult</c>-returning <c>Create</c> method.
/// </summary>
/// <remarks>
/// <para>
/// <c>DomainService</c> provides <c>CheckRule(IBusinessRule)</c> access and acts as the DI
/// anchor for all domain services. Direct <c>IDomainService</c> implementation bypasses these
/// shared capabilities, forcing copy-paste of <c>CheckRule</c> logic.
/// </para>
/// <para>
/// Companion Roslyn analyzers SK0008, SK0009, SK0010 and SK0037 enforce the narrower per-call-site
/// conventions (dispatch coupling, domain event versioning, specification ordering, value object validation).
/// </para>
/// </remarks>
public static class DomainGoldStandardRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every non-abstract type in
    /// <paramref name="assembly"/> that implements <paramref name="domainServiceInterface"/>
    /// also inherits from <paramref name="domainServiceBase"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>DomainService</c> abstract class itself is excluded via <c>.AreNotAbstract()</c> —
    /// it passes naturally since it is abstract.
    /// </para>
    /// <para>
    /// Failure message: <c>.GetResult().FailingTypeNames</c> lists all non-conforming type names.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>class PricingService : IDomainService { ... }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>class PricingService : DomainService { ... }</code>
    /// </para>
    /// <para>
    /// <strong>Usage.</strong> Both anchors come from the caller so this package needs no
    /// reference to <c>SharedKernel.Domain</c>:
    /// </para>
    /// <code>
    /// var rule = DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(
    ///     myServiceAssembly,
    ///     typeof(IDomainService),
    ///     typeof(DomainService));
    /// AssertRule(rule);
    /// </code>
    /// </remarks>
    /// <param name="assembly">
    /// The assembly to evaluate. Typically <c>typeof(IDomainService).Assembly</c> or a
    /// service assembly that contains domain service implementations.
    /// </param>
    /// <param name="domainServiceInterface">
    /// The domain-service marker interface, i.e.
    /// <c>typeof(SharedKernel.Domain.Abstractions.IDomainService)</c>.
    /// </param>
    /// <param name="domainServiceBase">
    /// The required abstract base class, i.e.
    /// <c>typeof(SharedKernel.Domain.DomainServices.DomainService)</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting every non-abstract domain-service implementor
    /// extends the required base.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="domainServiceInterface"/> is not an interface, or
    /// <paramref name="domainServiceBase"/> is not an inheritable class.
    /// </exception>
    public static ConditionList DomainServicesMustExtendAbstractBase(
        Assembly assembly,
        Type domainServiceInterface,
        Type domainServiceBase)
    {
        RuleAnchor.NotNull(assembly, nameof(assembly));
        RuleAnchor.Interface(domainServiceInterface, nameof(domainServiceInterface));
        RuleAnchor.BaseClass(domainServiceBase, nameof(domainServiceBase));

        return Types
            .InAssembly(assembly)
            .That()
            .ImplementInterface(domainServiceInterface)
            .And()
            .AreNotAbstract()
            .Should()
            .Inherit(domainServiceBase);
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every non-abstract aggregate factory in
    /// <paramref name="assembly"/> exposes a public <c>Create</c> method returning
    /// <c>ValidationResult&lt;TAggregateRoot&gt;</c> for the aggregate it declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A factory reports invalid input as a failed validation result carrying every error, never by
    /// throwing, and the marker interface <c>IAggregateFactory&lt;TAggregateRoot, TId&gt;</c> is how this rule
    /// finds factories. Apply it to each domain assembly; a factory living outside a domain assembly is
    /// simply not inspected, which keeps the convention visible in code review.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// public sealed class OrderFactory : IAggregateFactory&lt;Order, OrderId&gt;
    /// {
    ///     public Order Create(string customer) =&gt; new(...);   // throws on invalid input
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// public sealed class OrderFactory : IAggregateFactory&lt;Order, OrderId&gt;
    /// {
    ///     public ValidationResult&lt;Order&gt; Create(string customer) =&gt; Order.Place(...);
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Usage.</strong> Both anchors come from the caller, so this package needs no reference to
    /// <c>SharedKernel.Domain</c> or <c>SharedKernel.Primitives</c>:
    /// </para>
    /// <code>
    /// var rule = DomainGoldStandardRules.AggregateFactoriesMustCreateValidationResults(
    ///     typeof(Order).Assembly,
    ///     typeof(IAggregateFactory&lt;,&gt;),
    ///     typeof(ValidationResult&lt;&gt;));
    /// AssertRule(rule);
    /// </code>
    /// </remarks>
    /// <param name="assembly">The domain assembly to evaluate.</param>
    /// <param name="aggregateFactoryDefinition">
    /// The open generic marker, <c>typeof(SharedKernel.Domain.Abstractions.IAggregateFactory&lt;,&gt;)</c>.
    /// </param>
    /// <param name="validationResultDefinition">
    /// The open generic result type, <c>typeof(SharedKernel.Primitives.Results.ValidationResult&lt;&gt;)</c>.
    /// </param>
    /// <returns>A <see cref="ConditionList"/> over every non-abstract class in the assembly.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="aggregateFactoryDefinition"/> is not an open generic interface with two type parameters, or
    /// <paramref name="validationResultDefinition"/> is not an open generic type with one.
    /// </exception>
    public static ConditionList AggregateFactoriesMustCreateValidationResults(
        Assembly assembly,
        Type aggregateFactoryDefinition,
        Type validationResultDefinition)
    {
        RuleAnchor.NotNull(assembly, nameof(assembly));
        RuleAnchor.GenericDefinition(aggregateFactoryDefinition, 2, nameof(aggregateFactoryDefinition));
        RuleAnchor.Interface(aggregateFactoryDefinition, nameof(aggregateFactoryDefinition));
        RuleAnchor.GenericDefinition(validationResultDefinition, 1, nameof(validationResultDefinition));

        return Types
            .InAssembly(assembly)
            .That()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .Should()
            .MeetCustomRule(new AggregateFactoryCreateReturnsValidationResultPredicate(
                aggregateFactoryDefinition,
                validationResultDefinition));
    }
}
