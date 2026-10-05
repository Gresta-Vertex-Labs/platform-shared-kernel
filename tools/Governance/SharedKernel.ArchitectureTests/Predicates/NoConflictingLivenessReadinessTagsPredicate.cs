using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that inspects health-check registration extension methods
/// declared in <c>SharedKernel.ServiceDefaults</c> and fails any method whose collected
/// string-literal tag set carries both <c>"live"</c> and <c>"ready"</c> simultaneously.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.HealthCheckTagIntegrityRules"/> to enforce the platform's
/// liveness/readiness tag mutual-exclusivity contract documented in
/// <c>src/Hosting/ServiceDefaults/CLAUDE.md</c>: every health check carries either <c>"live"</c> or
/// <c>"ready"</c>, never both.
/// </para>
/// <para>
/// <strong>Scope (method-name filter).</strong> Only <see cref="MethodDefinition"/>s whose
/// <see cref="MethodDefinition.Name"/> starts with <c>"Add"</c> and ends with
/// <c>"HealthCheck"</c> or <c>"ReadinessCheck"</c> are inspected. These are the platform's
/// sole sanctioned health-check registration surface — consuming services call these
/// extensions rather than <c>AddCheck</c> directly. All other methods (and all other types)
/// pass unconditionally.
/// </para>
/// <para>
/// <strong>Detection technique (IL-literal collection, not full data-flow analysis).</strong>
/// For each in-scope method, the predicate walks <see cref="OpCodes.Ldstr"/> instructions in
/// the method body and collects every string literal pushed onto the evaluation stack. The
/// collected set is then checked for the simultaneous presence of <c>"live"</c> and
/// <c>"ready"</c>. This is a literal-collection heuristic: if a tag value is computed
/// dynamically (e.g., read from configuration) rather than passed as a literal, this rule
/// cannot see it and the gate is advisory only for that call site. Every dependency-specific
/// extension shipped by <c>SharedKernel.ServiceDefaults</c> is expected to pass literal tag
/// arrays at its own call site, which is the only place this rule needs to (or can)
/// mechanically inspect.
/// </para>
/// </remarks>
public sealed class NoConflictingLivenessReadinessTagsPredicate : ICustomRule
{
    private const string LiveTag = "live";
    private const string ReadyTag = "ready";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when no in-scope health-check registration
    /// method in <paramref name="type"/> carries both <c>"live"</c> and <c>"ready"</c> string
    /// literals in the same method body. Returns <see langword="false"/> (rule violated)
    /// when such a conflicting literal set is found.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every in-scope method's tag literal set carries at most
    /// one of <c>"live"</c>/<c>"ready"</c>; <see langword="false"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!IsHealthCheckRegistrationMethod(method.Name))
                continue;

            if (method.Body is null)
                continue;

            var literals = CollectStringLiterals(method.Body);

            if (literals.Contains(LiveTag) && literals.Contains(ReadyTag))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="methodName"/> matches the
    /// platform's health-check registration extension naming convention: starts with
    /// <c>"Add"</c> and ends with <c>"HealthCheck"</c> or <c>"ReadinessCheck"</c>.
    /// </summary>
    internal static bool IsHealthCheckRegistrationMethod(string methodName) =>
        methodName.StartsWith("Add", StringComparison.Ordinal)
        && (methodName.EndsWith("HealthCheck", StringComparison.Ordinal)
            || methodName.EndsWith("ReadinessCheck", StringComparison.Ordinal));

    /// <summary>
    /// Walks every <see cref="OpCodes.Ldstr"/> instruction in <paramref name="body"/> and
    /// returns the set of string literal operands found.
    /// </summary>
    internal static HashSet<string> CollectStringLiterals(MethodBody body)
    {
        var literals = new HashSet<string>(StringComparer.Ordinal);

        foreach (var instruction in body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string literal)
                literals.Add(literal);
        }

        return literals;
    }
}
