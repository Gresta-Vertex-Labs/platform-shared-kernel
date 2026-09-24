using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicate that enforces the platform's <c>outcome</c>-tag completeness
/// contract for <c>RequestDuration</c> histogram recordings inside
/// <c>SharedKernel.Application</c>.
/// </summary>
/// <remarks>
/// <para>
/// No SK diagnostic ID is assigned to this rule — it is a NetArchTest <c>ICustomRule</c>,
/// consistent with the <see cref="HealthCheckTagIntegrityRules"/> precedent of
/// SK-less rules for tag/instrumentation completeness checks.
/// </para>
/// <para>
/// <strong>Designed and tested against contrived in-memory fixtures.</strong> Real-assembly
/// verification against <c>SharedKernel.Application</c>'s own
/// <c>MetricsBehavior&lt;,&gt;</c> is <c>05.Application</c>'s responsibility — this domain writes
/// no implementation files for other domains.
/// </para>
/// </remarks>
public static class MetricsInstrumentationRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every <c>Histogram&lt;T&gt;.Record(...)</c>
    /// call site found in <paramref name="assembly"/> carries an <c>"outcome"</c> string literal in
    /// the same method body.
    /// </summary>
    /// <param name="assembly">
    /// The assembly to evaluate — typically <c>SharedKernel.Application</c> (or a
    /// fixture shaped like it). Supply via <c>typeof(SomeBehavior).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail information.
    /// When the rule fails, <c>FailingTypeNames</c> names the offending type.
    /// </returns>
    public static ConditionList RequestDurationRecordsIncludeOutcomeTag(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new RequestDurationRecordMissingOutcomeTagPredicate());
}
