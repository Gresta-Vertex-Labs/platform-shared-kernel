using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce the <c>SharedKernel.ServiceDefaults</c>
/// health-check tagging contract documented in <c>13.ServiceDefaults/CLAUDE.md</c>:
/// every health check carries either <c>"live"</c> or <c>"ready"</c>, never both, and
/// dependency-specific checks always carry <c>"ready"</c> and never <c>"live"</c>.
///
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why an IL-level rule, not a Roslyn analyzer?</strong> Health-check tags are
/// supplied as an <c>IEnumerable&lt;string&gt;</c> constructor argument or via
/// <c>AddCheck(...).WithTags(...)</c> — both are runtime values in the general case, not
/// compile-time-resolvable constants. The platform's dependency-specific extensions
/// (<c>AddRedisHealthCheck</c>, <c>AddDatabaseReadinessCheck&lt;TContext&gt;</c>, etc.) are
/// expected to pass a literal string array at their own call site inside
/// <c>SharedKernel.ServiceDefaults</c> — this is the only place the rule can mechanically
/// inspect, and it is also the only place that needs inspecting, because these extensions
/// are the platform's sole sanctioned health-check registration surface (consuming services
/// call the extension, not <c>AddCheck</c> directly).
/// </para>
/// <para>
/// <strong>Detection-surface limitation.</strong> Both predicates collect string literals via
/// an <c>Ldstr</c> IL walk — this is an IL-literal-collection technique, not a full data-flow
/// analysis. If a tag value is ever computed dynamically (e.g., from configuration) inside one
/// of these extensions, the rule will not see it and the gate becomes advisory only for that
/// call site.
/// </para>
/// </remarks>
public static class HealthCheckTagIntegrityRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no health-check registration
    /// method (matching the <c>Add*HealthCheck</c>/<c>Add*ReadinessCheck</c> naming
    /// convention) in <paramref name="serviceDefaultsAssembly"/> carries both <c>"live"</c>
    /// and <c>"ready"</c> string literals in its tag set simultaneously.
    /// </summary>
    /// <param name="serviceDefaultsAssembly">
    /// The <c>SharedKernel.ServiceDefaults</c> assembly (or a fixture shaped like it) to scan.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail
    /// information. When the rule fails, <c>FailingTypeNames</c> names the offending type.
    /// </returns>
    public static ConditionList NoConflictingLivenessReadinessTags(
        Assembly serviceDefaultsAssembly)
        => Types
            .InAssembly(serviceDefaultsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoConflictingLivenessReadinessTagsPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every dependency-specific
    /// health-check registration method in <paramref name="serviceDefaultsAssembly"/> —
    /// identified by <paramref name="dependencyCheckMethodNamePrefixes"/> — carries
    /// <c>"ready"</c> and does not carry <c>"live"</c> in its tag literal set.
    /// </summary>
    /// <param name="serviceDefaultsAssembly">
    /// The <c>SharedKernel.ServiceDefaults</c> assembly (or a fixture shaped like it) to scan.
    /// </param>
    /// <param name="dependencyCheckMethodNamePrefixes">
    /// Method-name prefixes identifying dependency-specific health-check registration
    /// extensions (e.g. <c>"AddRedis"</c>, <c>"AddDatabase"</c>, <c>"AddRabbitMq"</c>,
    /// <c>"AddAzureServiceBus"</c>, <c>"AddCache"</c>). Supplied by the caller rather than
    /// hard-coded — the consuming test project enumerates the actual extension method names
    /// shipped by <c>SharedKernel.ServiceDefaults</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail
    /// information. When the rule fails, <c>FailingTypeNames</c> names the offending type.
    /// </returns>
    public static ConditionList DependencyHealthChecksCarryReadyNotLive(
        Assembly serviceDefaultsAssembly,
        params string[] dependencyCheckMethodNamePrefixes)
        => Types
            .InAssembly(serviceDefaultsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(
                new DependencyHealthChecksCarryReadyNotLivePredicate(
                    dependencyCheckMethodNamePrefixes));
}
