using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type whose method body contains a call to
/// <c>Histogram&lt;T&gt;.Record(...)</c> without a companion <c>"outcome"</c> string literal in
/// the same method body.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.MetricsInstrumentationRules"/> to enforce that every
/// <c>RequestDuration</c> histogram recording call carries an <c>outcome</c> tag — the same shape
/// <c>StreamMetricsBehavior</c> (P-234) already emits. Without an <c>outcome</c> tag, dashboards
/// cannot distinguish success/failure/exception/cached/duplicate/unauthorized outcomes for a
/// given request duration measurement.
/// </para>
/// <para>
/// <strong>Detection technique (IL-literal collection, not full data-flow analysis).</strong> This
/// predicate reuses the <c>Ldstr</c> literal-collection technique introduced by
/// <see cref="NoConflictingLivenessReadinessTagsPredicate"/> (WO-027 P-173) — no new Mono.Cecil
/// technique is introduced here, only a new call-site search target
/// (<c>Histogram&lt;T&gt;.Record</c>, the first use of this search target in the domain). For each
/// method body containing a <c>Call</c>/<c>Callvirt</c> instruction whose operand's
/// <c>MethodReference.Name == "Record"</c> and whose <c>MethodReference.DeclaringType</c> is a
/// <see cref="GenericInstanceType"/> with <c>ElementType.FullName ==
/// "System.Diagnostics.Metrics.Histogram`1"</c>, the predicate collects every <c>Ldstr</c> literal
/// in that same method body and checks for the presence of <c>"outcome"</c>.
/// </para>
/// <para>
/// This is a method-level co-occurrence check, not data-flow analysis — the same documented
/// limitation as <see cref="NoConflictingLivenessReadinessTagsPredicate"/> applies: a dynamically
/// computed tag value (e.g., built via string interpolation or read from configuration) is
/// invisible to this rule.
/// </para>
/// </remarks>
public sealed class RequestDurationRecordMissingOutcomeTagPredicate : ICustomRule
{
    private const string RecordMethodName = "Record";
    private const string HistogramElementTypeFullName = "System.Diagnostics.Metrics.Histogram`1";
    private const string OutcomeTag = "outcome";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when every <c>Histogram&lt;T&gt;.Record(...)</c>
    /// call site found in <paramref name="type"/>'s methods carries an <c>"outcome"</c> string
    /// literal in the same method body. Returns <see langword="false"/> (rule violated) when a
    /// <c>Record</c> call is found with no companion <c>"outcome"</c> literal.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every in-scope <c>Record</c> call site carries an
    /// <c>"outcome"</c> literal in its method body; <see langword="false"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            if (!HasHistogramRecordCall(method.Body))
                continue;

            var literals = NoConflictingLivenessReadinessTagsPredicate.CollectStringLiterals(method.Body);
            if (!literals.Contains(OutcomeTag))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="body"/> contains at least one
    /// <c>Call</c>/<c>Callvirt</c> instruction whose operand resolves to a method named
    /// <see cref="RecordMethodName"/> declared on a <see cref="GenericInstanceType"/> whose
    /// <c>ElementType.FullName</c> is exactly <see cref="HistogramElementTypeFullName"/>.
    /// </summary>
    private static bool HasHistogramRecordCall(MethodBody body)
    {
        foreach (var instruction in body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                continue;

            if (instruction.Operand is not MethodReference methodRef)
                continue;

            if (methodRef.Name != RecordMethodName)
                continue;

            if (methodRef.DeclaringType is not GenericInstanceType git)
                continue;

            if (git.ElementType.FullName == HistogramElementTypeFullName)
                return true;
        }

        return false;
    }
}
