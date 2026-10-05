// Verification: proves the PACKED SharedKernel.ArchitectureTests package is genuinely consumable.
//
// This harness resolves the package from the local feed, not via ProjectReference, so it is the
// only place a missing package dependency actually surfaces. Every rule reached here is EXECUTED
// (GetResult()), never merely compiled against: the defect this harness exists to catch was a
// runtime FileNotFoundException on an assembly the nuspec never declared, which a compile-only
// check cannot see.
//
// Rules whose anchor types are supplied by the caller (guard purity, domain gold standard,
// contracts purity) are covered below with locally-declared anchors. Before those rules were
// parameterized they hard-bound to SharedKernel.Core/.Domain/.Contracts, and because this
// harness only ever exercised SharedKernelLayeringRules it passed green while three rule
// classes threw on first call from the package. Keep at least one executed call per
// caller-anchored rule here so that regression cannot return.
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Rules;
using System.Reflection;

var assembly = Assembly.GetExecutingAssembly();
var failures = 0;

// Rules that need no caller-supplied anchor.
Execute(
    "SharedKernelLayeringRules.TestingNeverReferencedByProduction",
    () => SharedKernelLayeringRules.TestingNeverReferencedByProduction(assembly));

// Caller-anchored rules — these are the ones that used to throw from the packed package.
Execute(
    "GuardPurityRules.GuardAgainstMethodsMustNotThrow",
    () => GuardPurityRules.GuardAgainstMethodsMustNotThrow(assembly, typeof(ILocalGuardClause)));

Execute(
    "DomainGoldStandardRules.DomainServicesMustExtendAbstractBase",
    () => DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(
        assembly,
        typeof(ILocalDomainService),
        typeof(LocalDomainServiceBase)));

Execute(
    "ContractsPurityRules.IntegrationEventImplementationsMustBeSealed",
    () => ContractsPurityRules.IntegrationEventImplementationsMustBeSealed(
        assembly,
        typeof(ILocalIntegrationEvent)));

if (failures > 0)
{
    Console.Error.WriteLine(
        $"ArchitectureTests consumer verification FAILED: {failures} rule(s) could not execute "
            + "from the packed package.");
    return 1;
}

Console.WriteLine("ArchitectureTests consumer verification passed: all probed rules executed.");
return 0;

// Executes a rule end to end. A rule that cannot even be evaluated (a missing package
// dependency, a bad anchor) throws here; a rule that evaluates is a pass for this harness's
// purpose regardless of whether the sample types satisfy it.
void Execute(string name, Func<NetArchTest.Rules.ConditionList> ruleFactory)
{
    try
    {
        _ = ruleFactory().GetResult();
        Console.WriteLine($"  OK      {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"  FAILED  {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

// Verifies ArchitectureRuleBase is subclassable and AssertRule is reachable from the package.
public sealed class SampleArchTest : ArchitectureRuleBase
{
    public void VerifyRule()
    {
        var rule = SharedKernelLayeringRules.TestingNeverReferencedByProduction(
            Assembly.GetExecutingAssembly());
        AssertRule(rule);
    }
}

// Local stand-ins for the SharedKernel anchor types. Declared here rather than referenced so
// this harness proves the package needs no SharedKernel dependency of its own.
public interface ILocalGuardClause;

public interface ILocalDomainService;

public interface ILocalIntegrationEvent;

public abstract class LocalDomainServiceBase : ILocalDomainService;
