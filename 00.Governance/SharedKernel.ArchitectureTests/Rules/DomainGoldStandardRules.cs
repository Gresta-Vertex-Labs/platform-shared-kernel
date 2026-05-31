using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.DomainServices;

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
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class DomainGoldStandardRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every non-abstract type in
    /// <paramref name="assembly"/> that implements <see cref="IDomainService"/> also inherits
    /// from <see cref="DomainService"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="DomainService"/> abstract class itself is excluded via
    /// <c>.AreNotAbstract()</c> — it passes naturally since it is abstract.
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
    /// </remarks>
    /// <param name="assembly">
    /// The assembly to evaluate. Typically <c>typeof(IDomainService).Assembly</c> or a
    /// service assembly that contains domain service implementations.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting every non-abstract <c>IDomainService</c>
    /// implementor extends <see cref="DomainService"/>.
    /// </returns>
    public static ConditionList DomainServicesMustExtendAbstractBase(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .ImplementInterface(typeof(IDomainService))
            .And()
            .AreNotAbstract()
            .Should()
            .Inherit(typeof(DomainService));
}
