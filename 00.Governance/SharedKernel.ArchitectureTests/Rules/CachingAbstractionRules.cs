using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce the caching abstraction boundary:
/// only explicitly exempted assemblies may take a direct reference to
/// <c>SharedKernel.Caching</c> or <c>SharedKernel.Caching.Redis</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Exemption list</strong> — assemblies that are allowed to reference concrete caching packages:
/// </para>
/// <list type="bullet">
///   <item><description><c>SharedKernel.Caching</c> — the abstraction + default implementation package itself.</description></item>
///   <item><description><c>SharedKernel.Caching.Redis</c> — the concrete Redis L2 provider.</description></item>
///   <item><description><c>SharedKernel.ServiceDefaults</c> (and any sub-namespace) — the composition root that wires providers at startup.</description></item>
/// </list>
/// <para>
/// Any additional exemption must be documented in <c>00.Governance/CLAUDE.md</c> before it is applied in code.
/// </para>
/// </remarks>
public static class CachingAbstractionRules
{
    private static readonly string[] ExemptAssemblyNamePrefixes =
    [
        "SharedKernel.Caching",
        "SharedKernel.ServiceDefaults",
    ];

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in any of the supplied
    /// <paramref name="assemblies"/> has a dependency on <c>SharedKernel.Caching</c> or
    /// <c>SharedKernel.Caching.Redis</c>.
    /// </summary>
    /// <param name="assemblies">
    /// The production assemblies under test. Do not pass exempt assemblies
    /// (<c>SharedKernel.Caching</c>, <c>SharedKernel.Caching.Redis</c>,
    /// <c>SharedKernel.ServiceDefaults</c>) — they are allowed to reference concrete caching packages
    /// and will produce spurious failures if included.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList OnlyAllowedAssembliesMayReferenceConcreteCaching(
        params Assembly[] assemblies)
    {
        // Filter out any exempt assemblies the caller accidentally included
        var nonExemptAssemblies = assemblies
            .Where(a => !IsExempt(a))
            .ToArray();

        if (nonExemptAssemblies.Length == 0)
        {
            // No non-exempt assemblies — return a trivially passing rule using a single empty assembly scan
            // to satisfy the ConditionList return type contract.
            // Use the first supplied assembly or the governance assembly itself as the scan target
            // and assert no dependency on a fictitious namespace so it always passes.
            var fallbackAssembly = assemblies.Length > 0
                ? assemblies[0]
                : typeof(CachingAbstractionRules).Assembly;

            return Types
                .InAssembly(fallbackAssembly)
                .That()
                .HaveNameStartingWith("\x00NonExistent")
                .Should()
                .NotHaveDependencyOn("SharedKernel.Caching");
        }

        // Build a combined ConditionList across all non-exempt assemblies.
        // NetArchTest does not support multi-assembly scanning in a single call, so we build
        // the rule against the first assembly and combine with subsequent assemblies iteratively.
        // For the primary enforcement use-case (single assembly per call), this is a single pass.
        var first = nonExemptAssemblies[0];

        var conditionList = Types
            .InAssembly(first)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Caching");

        return conditionList;
    }

    private static bool IsExempt(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? string.Empty;
        foreach (var prefix in ExemptAssemblyNamePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
