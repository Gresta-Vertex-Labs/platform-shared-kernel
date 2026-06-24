using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails when a method body passes a bare string literal to a
/// recognized health-check registration API while a sibling "string constants class" already
/// exists in the same assembly and exposes that exact value.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.HealthCheckConstantsUsageRules"/> to close the source-discipline gap
/// found by the P-177 audit of <c>SharedKernel.ServiceDefaults</c>: one well-known-string
/// constants class was built correctly, but several sibling files kept hardcoding default
/// health-check names as bare string literals instead of extending the same discipline. This
/// rule is additive to <see cref="Rules.HealthCheckTagIntegrityRules"/> (P-173), which enforces
/// tag mutual-exclusivity semantics — an orthogonal, never-overlapping concern.
/// </para>
/// <para>
/// <strong>Generality.</strong> Neither this predicate nor
/// <see cref="Rules.HealthCheckConstantsUsageRules"/> references any concrete constants-class
/// name. Detection is entirely shape-based (<see cref="StringConstantsClassDetector"/>) and
/// value-based (exact string equality between a call-site literal and a resolved constant). The
/// only domain-specific knowledge baked into this predicate is the *call-site* shape — which
/// methods constitute a "health-check registration API".
/// </para>
/// <para>
/// <strong>Detection technique.</strong> For each in-scope call site, walks the immediate
/// argument-producing IL instructions using the established <c>Ldstr</c> literal-collection
/// technique from <see cref="NoConflictingLivenessReadinessTagsPredicate"/> to collect every
/// string literal feeding the call — covering both the check-name argument and any
/// tag-array/tag-parameter argument in the same call. If the resolved constants set (produced by
/// <see cref="StringConstantsClassDetector"/>) is empty for the assembly, the rule passes
/// vacuously — there is nothing yet to enforce.
/// </para>
/// </remarks>
public sealed class NoBareHealthCheckLiteralWhereConstantsExistPredicate : ICustomRule
{
    /// <summary>
    /// Returns <see langword="true"/> (rule met) when no health-check registration call site in
    /// <paramref name="type"/>'s methods passes a bare string literal that exactly matches a
    /// value already exposed by a string constants class detected in the same module. Returns
    /// <see langword="false"/> (rule violated) when such a duplicate literal is found.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop.
    /// </param>
    public bool MeetsRule(TypeDefinition type)
    {
        var resolvedConstants = StringConstantsClassDetector.ResolveStringConstants(type.Module);
        if (resolvedConstants.Count == 0)
            return true;

        var valueToConstant = BuildValueLookup(resolvedConstants);

        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            if (!ContainsHealthCheckRegistrationCallSite(method.Body))
                continue;

            var callSiteLiterals = CollectStringLiterals(method.Body);

            foreach (var literal in callSiteLiterals)
            {
                if (valueToConstant.TryGetValue(literal, out _))
                    return false;
            }
        }

        return true;
    }

    private static Dictionary<string, StringConstantsClassDetector.ResolvedStringConstant>
        BuildValueLookup(IReadOnlyList<StringConstantsClassDetector.ResolvedStringConstant> resolvedConstants)
    {
        var lookup = new Dictionary<string, StringConstantsClassDetector.ResolvedStringConstant>(
            StringComparer.Ordinal);

        foreach (var constant in resolvedConstants)
            lookup[constant.Value] = constant;

        return lookup;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="body"/> contains at least one call or
    /// constructor invocation matching a recognized health-check registration API: the
    /// <c>IHealthChecksBuilder.Add(HealthCheckRegistration)</c> instance method, an
    /// <c>AddCheck</c> extension method declared on a type whose name starts with
    /// <c>"HealthChecksBuilder"</c>, or the <c>HealthCheckRegistration</c> constructor.
    /// </summary>
    private static bool ContainsHealthCheckRegistrationCallSite(MethodBody body)
    {
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is not MethodReference methodReference)
                continue;

            if (IsHealthCheckRegistrationCallSite(methodReference))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="methodReference"/> identifies one of
    /// the platform's recognized health-check registration call sites:
    /// <list type="bullet">
    /// <item><description>
    /// <c>Add</c> where the declaring type's name is <c>"IHealthChecksBuilder"</c> or
    /// <c>"HealthChecksBuilder"</c>
    /// </description></item>
    /// <item><description>
    /// <c>AddCheck</c> where the declaring type's name starts with <c>"HealthChecksBuilder"</c>
    /// (covers <c>HealthChecksBuilderAddCheckExtensions</c> and
    /// <c>HealthChecksBuilderDelegateExtensions</c>, the actual
    /// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c> extension-method host types)
    /// </description></item>
    /// <item><description>
    /// <c>.ctor</c> where the declaring type's name is <c>"HealthCheckRegistration"</c>
    /// </description></item>
    /// </list>
    /// </summary>
    private static bool IsHealthCheckRegistrationCallSite(MethodReference methodReference)
    {
        var declaringTypeName = methodReference.DeclaringType.Name;

        if (methodReference.Name == "Add"
            && (declaringTypeName == "IHealthChecksBuilder" || declaringTypeName == "HealthChecksBuilder"))
        {
            return true;
        }

        if (methodReference.Name == "AddCheck"
            && declaringTypeName.StartsWith("HealthChecksBuilder", StringComparison.Ordinal))
        {
            return true;
        }

        if (methodReference.Name == ".ctor" && declaringTypeName == "HealthCheckRegistration")
            return true;

        return false;
    }

    /// <summary>
    /// Walks every <see cref="OpCodes.Ldstr"/> instruction in <paramref name="body"/> and returns
    /// the set of string literal operands found. Reuses the established literal-collection
    /// technique from <see cref="NoConflictingLivenessReadinessTagsPredicate"/>.
    /// </summary>
    private static HashSet<string> CollectStringLiterals(MethodBody body) =>
        NoConflictingLivenessReadinessTagsPredicate.CollectStringLiterals(body);
}
