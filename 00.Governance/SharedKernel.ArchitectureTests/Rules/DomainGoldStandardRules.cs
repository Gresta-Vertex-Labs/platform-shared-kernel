using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Helpers;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that encode the domain gold-standard conventions:
/// all <c>IDomainService</c> implementors must extend the <c>DomainService</c> abstract base.
/// </summary>
/// <remarks>
/// <para>
/// <c>DomainService</c> provides <c>CheckRule(IBusinessRule)</c> access and acts as the DI
/// anchor for all domain services. Direct <c>IDomainService</c> implementation bypasses these
/// shared capabilities, forcing copy-paste of <c>CheckRule</c> logic.
/// </para>
/// <para>
/// Companion Roslyn analyzers SK0008, SK0009, and SK0010 enforce the narrower per-call-site
/// conventions (dispatch coupling, domain event versioning, specification ordering).
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
}
