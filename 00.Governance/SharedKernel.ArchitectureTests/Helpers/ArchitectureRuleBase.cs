using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Helpers;

/// <summary>
/// Abstract base class for SharedKernel architecture test suites.
/// Provides assembly discovery, rule construction helpers, and assertion wrappers
/// over the <see cref="NetArchTest.Rules"/> fluent API.
/// </summary>
/// <remarks>
/// Subclass this in each capability domain's test project to author architecture tests.
/// Never depend on this package from production code — reference it with
/// <c>PrivateAssets="all"</c>.
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
    /// Fails the test with a descriptive message listing any violating types.
    /// </summary>
    /// <param name="conditionList">The evaluated condition list returned by the rule factory.</param>
    protected void AssertRule(ConditionList conditionList)
    {
        var result = conditionList.GetResult();
        result.IsSuccessful.Should().BeTrue(
            because: $"Architecture rule violated. Failing types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}"
        );
    }
}
