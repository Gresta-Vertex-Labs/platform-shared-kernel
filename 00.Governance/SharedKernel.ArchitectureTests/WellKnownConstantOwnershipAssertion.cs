using System.Reflection;
using Mono.Cecil;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests;

/// <summary>
/// Mono.Cecil-based helper that asserts no assembly other than a caller-designated owning type
/// declares its own independently-named field carrying the exact same literal value as a
/// caller-supplied canonical cross-cutting constant.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="LoggingEventIdIntegrityAssertion"/>'s and <see cref="PipelineOrderAssertion"/>'s
/// precedent — a plain public helper, not a NetArchTest <c>ConditionList</c>/<c>ICustomRule</c>,
/// because "no assembly other than the owning one may declare its own independently-valued literal
/// for a value <c>01.Core</c> already owns" is a cross-assembly, whole-platform invariant with no
/// single-assembly "fire on one contrived violating assembly" shape a <c>ConditionList</c>
/// naturally expresses.
/// </para>
/// <para>
/// <strong>Reuses and extends <see cref="StringConstantsClassDetector"/>.</strong> This helper
/// calls <see cref="StringConstantsClassDetector.ResolveStringFieldsOnType(TypeDefinition)"/> —
/// the same field-shape + literal-value resolution technique — against EVERY
/// <see cref="TypeDefinition"/> in a scanned assembly (walked recursively, including nested types,
/// via the same <c>Mono.Cecil</c> traversal <see cref="LoggingEventIdIntegrityAssertion"/> uses),
/// not only types matching the <c>abstract sealed</c> "constants class" shape the original
/// <see cref="StringConstantsClassDetector.ResolveStringConstants(ModuleDefinition)"/> caller
/// assumed. A stray <c>const</c>/<c>static readonly string</c> field on an ordinary class is
/// therefore caught too.
/// </para>
/// <para>
/// <strong>Caller-supplied everything.</strong> <c>00.Governance</c> never references
/// <c>SharedKernel.Primitives</c> directly (it references nothing in production code). The
/// consuming test project supplies <c>canonicalValues</c> (the real
/// <c>WellKnownHeaders</c>/<c>WellKnownBaggageKeys</c> values), <c>owningTypeFullNames</c> (e.g.
/// <c>"SharedKernel.Primitives.Propagation.WellKnownHeaders"</c>), and <c>assembliesToScan</c>
/// (every other shipped production assembly) — keeping <c>01.Core</c>'s <c>WellKnownHeaders</c>/
/// <c>WellKnownBaggageKeys</c> as the single source of truth for the canonical values
/// while this helper itself stays dependency-free.
/// </para>
/// <para>
/// <strong>Motivating incident.</strong> A <c>"CorrelationId"</c> vs <c>"correlation.id"</c>
/// mismatch between a hand-rolled literal and the value <c>01.Core</c>'s registry actually
/// declared. A documented "always reference the <c>01.Core</c> constant" convention alone is
/// exactly the kind of rule this domain's own precedent (SK0013, <c>PresentationLayeringRules</c>,
/// SK0020/SK0021) has shown will drift without a build-time gate.
/// </para>
/// </remarks>
public static class WellKnownConstantOwnershipAssertion
{
    /// <summary>
    /// Walks every <see cref="TypeDefinition"/> (recursively, including nested types) in each
    /// assembly in <paramref name="assembliesToScan"/>, resolves every
    /// <c>const</c>/<c>static readonly string</c> field's literal value, and asserts that no such
    /// value — declared on a type NOT listed in <paramref name="owningTypeFullNames"/> — matches
    /// any value in <paramref name="canonicalValues"/>.
    /// </summary>
    /// <param name="canonicalValues">
    /// Maps a caller-chosen logical name (e.g. <c>"CorrelationIdHeader"</c>) to its canonical
    /// literal value (e.g. <c>"X-Correlation-Id"</c>). Only the dictionary's VALUES are matched
    /// against — the logical-name keys exist purely for diagnostic readability and are not
    /// referenced during matching.
    /// </param>
    /// <param name="owningTypeFullNames">
    /// The full Mono.Cecil type names (e.g.
    /// <c>"SharedKernel.Primitives.Propagation.WellKnownHeaders"</c>) permitted to declare fields
    /// carrying the canonical values. A type whose <c>TypeDefinition.FullName</c> appears in
    /// this collection is excluded from the scan entirely — the owning type is expected to declare
    /// exactly these values, by design.
    /// </param>
    /// <param name="assembliesToScan">
    /// Every assembly to inspect. The caller is responsible for excluding the assembly that
    /// contains the owning type(s) from this collection if that assembly declares no other
    /// candidate fields — including it is harmless (the owning type's own fields are always
    /// excluded by <paramref name="owningTypeFullNames"/>), but a non-owning type in the SAME
    /// assembly redeclaring the value is still caught.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown once, aggregating every violation found across the ENTIRE supplied assembly set —
    /// this method does not stop at the first failure. The message names every offending
    /// declaring-type/field/value, mirroring <see cref="LoggingEventIdIntegrityAssertion"/>'s and
    /// <see cref="PipelineOrderAssertion"/>'s aggregate-failure-message convention.
    /// </exception>
    public static void AssertSoleDeclaration(
        IReadOnlyDictionary<string, string> canonicalValues,
        IReadOnlyCollection<string> owningTypeFullNames,
        IReadOnlyCollection<Assembly> assembliesToScan
    )
    {
        var canonicalValueSet = new HashSet<string>(canonicalValues.Values, StringComparer.Ordinal);
        var owningTypeSet = new HashSet<string>(owningTypeFullNames, StringComparer.Ordinal);

        var violations = new List<string>();

        foreach (var assembly in assembliesToScan)
        {
            using var assemblyDefinition = AssemblyDefinition.ReadAssembly(assembly.Location);
            var module = assemblyDefinition.MainModule;

            foreach (var type in EnumerateAllTypes(module.Types))
            {
                if (owningTypeSet.Contains(type.FullName))
                    continue;

                foreach (var resolved in StringConstantsClassDetector.ResolveStringFieldsOnType(type))
                {
                    if (!canonicalValueSet.Contains(resolved.Value))
                        continue;

                    violations.Add(
                        $"{type.FullName}.{resolved.FieldName} (assembly '{assembly.GetName().Name}') "
                            + $"declares the value \"{resolved.Value}\", which duplicates a canonical "
                            + "cross-cutting constant. Reference the owning type's constant instead of "
                            + "redeclaring the literal."
                    );
                }
            }
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"WellKnownConstantOwnershipAssertion found {violations.Count} violation(s):"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, violations)
            );
        }
    }

    /// <summary>
    /// Recursively enumerates every <see cref="TypeDefinition"/> in <paramref name="types"/> plus
    /// every <see cref="TypeDefinition.NestedTypes"/> entry, at any nesting depth — the same
    /// technique <see cref="LoggingEventIdIntegrityAssertion"/> uses to bypass NetArchTest's
    /// documented compiler-generated/nested-type blind spot (see SK0012's <c>ReflectionGuardRules</c>
    /// notes).
    /// </summary>
    private static IEnumerable<TypeDefinition> EnumerateAllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;

            foreach (var nested in EnumerateAllTypes(type.NestedTypes))
                yield return nested;
        }
    }
}
