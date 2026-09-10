using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Helpers;

/// <summary>
/// Abstract base class for SharedKernel architecture test suites.
/// Provides assembly discovery, rule construction helpers, and assertion wrappers
/// over the <see cref="NetArchTest.Rules"/> fluent API.
/// </summary>
/// <remarks>
/// <para>
/// Subclass this in each capability domain's test project to author architecture tests.
/// </para>
/// <para>
/// This package carries no assertion-library dependency of its own — <see cref="AssertRule"/>
/// throws <see cref="ArchitectureRuleViolationException"/>, which every test runner reports as
/// a failure. Use your own assertion library against
/// <c>ConditionList.GetResult()</c> if you prefer.
/// </para>
/// </remarks>
public abstract class ArchitectureRuleBase
{
    /// <summary>
    /// Creates a <see cref="Types"/> predicate scope targeting all types in the given assembly.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>A <see cref="Types"/> fluent API entry point scoped to <paramref name="assembly"/>.</returns>
    protected Types GetAssemblyTypes(Assembly assembly) => Types.InAssembly(assembly);

    /// <summary>
    /// Creates a <see cref="ConditionList"/> that asserts no type in the evaluated assembly
    /// has a dependency on the specified forbidden namespace.
    /// </summary>
    /// <param name="assembly">The assembly whose types to evaluate.</param>
    /// <param name="forbiddenNamespace">
    /// The namespace prefix that must not be referenced.
    /// E.g., <c>"SharedKernel.Persistence"</c>.
    /// </param>
    /// <returns>A <see cref="ConditionList"/> ready for assertion via <see cref="AssertRule"/>.</returns>
    protected ConditionList ShouldNotReference(Assembly assembly, string forbiddenNamespace) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(forbiddenNamespace);

    /// <summary>
    /// Asserts that the <see cref="ConditionList"/> result is successful.
    /// </summary>
    /// <param name="conditionList">The evaluated condition list returned by the rule factory.</param>
    /// <exception cref="ArchitectureRuleViolationException">
    /// Thrown when the rule reports one or more violating types. The message lists them.
    /// </exception>
    protected void AssertRule(ConditionList conditionList)
    {
        var result = conditionList.GetResult();
        if (result.IsSuccessful)
        {
            return;
        }

        var failingTypeNames = result.FailingTypeNames?.ToArray() ?? Array.Empty<string>();
        var detail = failingTypeNames.Length > 0
            ? string.Join(", ", failingTypeNames)
            : "(the rule engine reported no specific type names)";

        throw new ArchitectureRuleViolationException(
            $"Architecture rule violated. Failing types: {detail}")
        {
            FailingTypeNames = failingTypeNames,
        };
    }

    /// <summary>
    /// Asserts that every supplied <see cref="ConditionList"/> is successful, reporting ALL
    /// violations together rather than stopping at the first.
    /// </summary>
    /// <remarks>
    /// Several rules return a <see cref="ConditionList"/> array — one entry per forbidden term or
    /// per package pair — because a single NetArchTest condition cannot express "none of these N
    /// dependencies". Pass such a result straight to this overload:
    /// <code>
    /// AssertRules(StorageTopologyRules.ProviderPackagesNeverReferenceEachOther(s3, obs));
    /// </code>
    /// Evaluating all of them before throwing matters: fixing one forbidden dependency at a time,
    /// re-running, and discovering the next is far slower than seeing every violation at once.
    /// </remarks>
    /// <param name="conditionLists">The evaluated condition lists returned by the rule factory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="conditionLists"/> is null.</exception>
    /// <exception cref="ArchitectureRuleViolationException">
    /// Thrown when any supplied rule reports violating types. The message lists every violation.
    /// </exception>
    protected void AssertRules(params ConditionList[] conditionLists)
    {
        ArgumentNullException.ThrowIfNull(conditionLists);

        var failingTypeNames = new List<string>();

        foreach (var conditionList in conditionLists)
        {
            var result = conditionList.GetResult();
            if (result.IsSuccessful)
            {
                continue;
            }

            failingTypeNames.AddRange(result.FailingTypeNames ?? Array.Empty<string>());
        }

        if (failingTypeNames.Count == 0)
        {
            return;
        }

        var distinct = failingTypeNames.Distinct(StringComparer.Ordinal).ToArray();

        throw new ArchitectureRuleViolationException(
            $"{conditionLists.Length} architecture rule(s) evaluated; violations found. "
                + $"Failing types: {string.Join(", ", distinct)}")
        {
            FailingTypeNames = distinct,
        };
    }
}
