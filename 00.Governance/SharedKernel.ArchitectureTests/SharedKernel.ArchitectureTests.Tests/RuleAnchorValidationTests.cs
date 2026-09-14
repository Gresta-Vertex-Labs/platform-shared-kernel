using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Contracts.Events;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.DomainServices;
using SharedKernel.Guards;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for the caller-supplied anchor-type validation on the three rules that no longer bind
/// to a SharedKernel assembly directly, and for
/// <see cref="ArchitectureRuleBase.AssertRule"/>'s assertion-library-free failure path.
/// </summary>
/// <remarks>
/// <para>
/// The three rules below select the types they judge via <c>.ImplementInterface(anchor)</c> /
/// <c>.Inherit(anchor)</c>. Passing a wrong-shaped <see cref="Type"/> would select zero types
/// and make the rule pass VACUOUSLY — the exact hazard WO-082/P-508 found when Mono.Cecil's
/// empty nested-type <c>Namespace</c> silently excluded every guard type. These tests prove the
/// validation converts that silent false-pass into a loud <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// T-343 (fire path): a non-interface anchor is rejected on each of the three rules.
/// T-344 (fire path): a null anchor/assembly is rejected.
/// T-345 (fire path): a sealed or non-class inheritance target is rejected.
/// T-346 (pass path): the real anchors are accepted, so validation is not over-strict.
/// T-347: AssertRule throws ArchitectureRuleViolationException carrying FailingTypeNames.
/// T-348: AssertRule returns quietly on a passing rule.
/// </para>
/// </remarks>
public class RuleAnchorValidationTests
{
    private static readonly Assembly ThisAssembly = typeof(RuleAnchorValidationTests).Assembly;

    // ---------------------------------------------------------------------------
    // T-343 — Fire path: a non-interface anchor must be rejected, never vacuously pass
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-343: passing a class where an interface is required must throw
    /// <see cref="ArgumentException"/> rather than selecting zero types.
    /// </summary>
    [Fact]
    public void GuardAgainstMethodsMustNotThrow_NonInterfaceAnchor_Throws()
    {
        var act = () => GuardPurityRules.GuardAgainstMethodsMustNotThrow(
            ThisAssembly,
            typeof(string));

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("guardClauseInterface")
            .WithMessage("*not an interface*vacuously*");
    }

    /// <summary>
    /// T-343: same guard on the domain gold-standard rule's interface anchor.
    /// </summary>
    [Fact]
    public void DomainServicesMustExtendAbstractBase_NonInterfaceAnchor_Throws()
    {
        var act = () => DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(
            ThisAssembly,
            typeof(string),
            typeof(DomainService));

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("domainServiceInterface");
    }

    /// <summary>
    /// T-343: same guard on the contracts-purity rule's interface anchor.
    /// </summary>
    [Fact]
    public void IntegrationEventImplementationsMustBeSealed_NonInterfaceAnchor_Throws()
    {
        var act = () => ContractsPurityRules.IntegrationEventImplementationsMustBeSealed(
            ThisAssembly,
            typeof(string));

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("integrationEventInterface");
    }

    // ---------------------------------------------------------------------------
    // T-344 — Fire path: null arguments are rejected
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-344: a null anchor type must throw <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void GuardAgainstMethodsMustNotThrow_NullAnchor_Throws()
    {
        var act = () => GuardPurityRules.GuardAgainstMethodsMustNotThrow(ThisAssembly, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("guardClauseInterface");
    }

    /// <summary>
    /// T-344: a null assembly must throw <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void GuardAgainstMethodsMustNotThrow_NullAssembly_Throws()
    {
        var act = () => GuardPurityRules.GuardAgainstMethodsMustNotThrow(
            null!,
            typeof(IGuardClause));

        act.Should().Throw<ArgumentNullException>().WithParameterName("guardsAssembly");
    }

    /// <summary>
    /// T-344: a null contracts assembly must throw <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void IntegrationEventImplementationsMustBeSealed_NullAssembly_Throws()
    {
        var act = () => ContractsPurityRules.IntegrationEventImplementationsMustBeSealed(
            null!,
            typeof(IIntegrationEvent));

        act.Should().Throw<ArgumentNullException>().WithParameterName("contractsAssembly");
    }

    // ---------------------------------------------------------------------------
    // T-345 — Fire path: an unusable inheritance target is rejected
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-345: a sealed base-class anchor must be rejected — nothing can inherit from it, so the
    /// rule would fail every selected type rather than judging them meaningfully.
    /// </summary>
    [Fact]
    public void DomainServicesMustExtendAbstractBase_SealedBaseAnchor_Throws()
    {
        var act = () => DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(
            ThisAssembly,
            typeof(IDomainService),
            typeof(SealedProbe));

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("domainServiceBase")
            .WithMessage("*sealed*");
    }

    /// <summary>
    /// T-345: an interface passed where a base class is required must be rejected.
    /// </summary>
    [Fact]
    public void DomainServicesMustExtendAbstractBase_InterfaceAsBaseAnchor_Throws()
    {
        var act = () => DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(
            ThisAssembly,
            typeof(IDomainService),
            typeof(IDomainService));

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("domainServiceBase")
            .WithMessage("*not a class*");
    }

    // ---------------------------------------------------------------------------
    // T-346 — Pass path: the real anchors are accepted (validation is not over-strict)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-346: every rule accepts the real SharedKernel anchor types and returns an evaluable
    /// <see cref="ConditionList"/> — proving the validation added above rejects only genuinely
    /// wrong shapes, and that each rule still passes against its real shipped assembly.
    /// </summary>
    [Fact]
    public void AllThreeRules_RealAnchors_AreAccepted()
    {
        var guard = GuardPurityRules.GuardAgainstMethodsMustNotThrow(
            typeof(IGuardClause).Assembly,
            typeof(IGuardClause));

        var domain = DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(
            typeof(IDomainService).Assembly,
            typeof(IDomainService),
            typeof(DomainService));

        var contracts = ContractsPurityRules.IntegrationEventImplementationsMustBeSealed(
            typeof(IIntegrationEvent).Assembly,
            typeof(IIntegrationEvent));

        guard.GetResult().IsSuccessful.Should().BeTrue(
            because: "the real guard clauses are pure on the functional path");
        domain.GetResult().IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Domain has no direct IDomainService implementor");
        contracts.GetResult().IsSuccessful.Should().BeTrue(
            because: "every shipped integration event is sealed");
    }

    // ---------------------------------------------------------------------------
    // T-347 / T-348 — AssertRule's assertion-library-free failure path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-347: a failing rule must surface as <see cref="ArchitectureRuleViolationException"/>
    /// with the violating type names both in the message and on
    /// <see cref="ArchitectureRuleViolationException.FailingTypeNames"/>. This is the behaviour
    /// that replaced the FluentAssertions call inside AssertRule, so the package ships with no
    /// assertion-library dependency of its own.
    /// </summary>
    [Fact]
    public void AssertRule_FailingRule_ThrowsWithFailingTypeNames()
    {
        var harness = new AssertRuleHarness();

        // A rule this very assembly cannot satisfy: this test class is deliberately not sealed.
        var failing = Types
            .InAssembly(ThisAssembly)
            .That()
            .HaveName(nameof(RuleAnchorValidationTests))
            .Should()
            .BeSealed();

        var act = () => harness.Assert(failing);

        act.Should()
            .Throw<ArchitectureRuleViolationException>()
            .WithMessage("*" + nameof(RuleAnchorValidationTests) + "*")
            .Which.FailingTypeNames.Should()
            .Contain(t => t.Contains(nameof(RuleAnchorValidationTests), StringComparison.Ordinal));
    }

    /// <summary>
    /// T-348: a passing rule must return quietly — AssertRule throws only on violation.
    /// </summary>
    [Fact]
    public void AssertRule_PassingRule_DoesNotThrow()
    {
        var harness = new AssertRuleHarness();

        var passing = Types
            .InAssembly(ThisAssembly)
            .That()
            .HaveName(nameof(SealedProbe))
            .Should()
            .BeSealed();

        var act = () => harness.Assert(passing);

        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------------
    // T-349 / T-350 — AssertRules: the array-returning rules' aggregating overload
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-349: <see cref="ArchitectureRuleBase.AssertRules"/> must report violations from EVERY
    /// supplied condition list, not stop at the first failure. Ten shipped rules return a
    /// <c>ConditionList[]</c>, so seeing all violations in one run is the point of the overload.
    /// </summary>
    [Fact]
    public void AssertRules_MultipleFailingRules_ReportsAllViolations()
    {
        var harness = new AssertRuleHarness();

        var firstFailing = Types
            .InAssembly(ThisAssembly)
            .That()
            .HaveName(nameof(RuleAnchorValidationTests))
            .Should()
            .BeSealed();

        var secondFailing = Types
            .InAssembly(ThisAssembly)
            .That()
            .HaveName(nameof(NotSealedProbe))
            .Should()
            .BeSealed();

        var act = () => harness.AssertMany(firstFailing, secondFailing);

        act.Should()
            .Throw<ArchitectureRuleViolationException>()
            .Which.FailingTypeNames.Should()
            .HaveCount(2, because: "both supplied rules fail and both must be reported")
            .And.Contain(t => t.Contains(nameof(RuleAnchorValidationTests), StringComparison.Ordinal))
            .And.Contain(t => t.Contains(nameof(NotSealedProbe), StringComparison.Ordinal));
    }

    /// <summary>
    /// T-350: <see cref="ArchitectureRuleBase.AssertRules"/> returns quietly when every supplied
    /// rule passes, and treats an empty set as a pass.
    /// </summary>
    [Fact]
    public void AssertRules_AllPassing_DoesNotThrow()
    {
        var harness = new AssertRuleHarness();

        var passing = Types
            .InAssembly(ThisAssembly)
            .That()
            .HaveName(nameof(SealedProbe))
            .Should()
            .BeSealed();

        var actAllPassing = () => harness.AssertMany(passing, passing);
        var actEmpty = () => harness.AssertMany();

        actAllPassing.Should().NotThrow();
        actEmpty.Should().NotThrow(because: "no rules supplied means nothing was violated");
    }

    /// <summary>
    /// Exposes <see cref="ArchitectureRuleBase.AssertRule"/> and
    /// <see cref="ArchitectureRuleBase.AssertRules"/>, which are <c>protected</c>, so the failure
    /// paths can be exercised exactly as a consuming test suite would reach them.
    /// </summary>
    private sealed class AssertRuleHarness : ArchitectureRuleBase
    {
        internal void Assert(ConditionList conditionList) => AssertRule(conditionList);

        internal void AssertMany(params ConditionList[] conditionLists) =>
            AssertRules(conditionLists);
    }

    /// <summary>A deliberately unsealed type used as a second violation source in T-349.</summary>
    private class NotSealedProbe;

    /// <summary>A sealed type used as a deliberately invalid inheritance anchor in T-345.</summary>
    private sealed class SealedProbe;
}
