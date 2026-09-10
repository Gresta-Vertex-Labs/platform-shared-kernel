using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that inspects method bodies via Mono.Cecil IL inspection
/// and fails any type — scoped to the <c>SharedKernel.Guards</c> namespace, see Remarks —
/// whose methods contain a <c>throw</c> IL opcode (<see cref="OpCodes.Throw"/>).
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.GuardPurityRules"/> to enforce the functional-path purity
/// contract: types implementing <c>IGuardClause</c> on the <c>Guard.Against.*</c> path
/// must never throw — they must return <c>Error?</c>.
/// </para>
/// <para>
/// <strong>Namespace scope (WO-082/P-508).</strong> <c>SharedKernel.Guards</c> was merged
/// into <c>SharedKernel.Core</c> — the C# namespace was deliberately preserved, but the
/// hosting assembly now also carries base exceptions, BCL extensions, and the railway
/// extensions (<c>ResultTry</c>/<c>ResultCombine</c>), none of which this predicate should
/// ever judge. This predicate therefore checks each type's EFFECTIVE namespace — the
/// namespace of its outermost enclosing type, walked via <see cref="TypeDefinition.DeclaringType"/>
/// for nested types, since Mono.Cecil (and the underlying CLR metadata) leaves
/// <see cref="TypeDefinition.Namespace"/> empty on every nested type (confirmed by direct
/// inspection of the real, shipped <c>SharedKernel.Core.dll</c> — <c>Guard/DefaultGuardClause</c>
/// and <c>Guard/Throw</c> both report an empty <c>Namespace</c>, with the effective namespace
/// only recoverable from the enclosing <c>Guard</c> type). Relying on NetArchTest's built-in
/// <c>ResideInNamespaceStartingWith</c> selection filter instead would have silently excluded
/// every nested guard type — including <c>DefaultGuardClause</c>, the sole real
/// <c>IGuardClause</c> implementor — turning the whole rule vacuous. A type whose effective
/// namespace does not start with <c>SharedKernel.Guards</c> is out of this predicate's scope
/// and is reported as compliant (<see langword="true"/>) regardless of its own throw content —
/// this is what keeps an unrelated <c>SharedKernel.Core</c> type from ever being judged by a
/// rule that was never meant to police it.
/// </para>
/// <para>
/// The companion class <c>Guard.Throw</c> (CLR name <c>SharedKernel.Guards.Guard+Throw</c>)
/// is excluded by the predicate itself: if the type's full name matches that class, the
/// predicate returns <see langword="true"/> (passes) unconditionally, allowing the imperative
/// path to throw without triggering a violation.
/// </para>
/// <para>
/// Property accessor compiler-generated methods (names starting with <c>get_</c> or
/// <c>set_</c>) on compiler-generated types are also excluded — they never contain
/// hand-authored throw statements.
/// </para>
/// </remarks>
public sealed class DoesNotContainThrowIlPredicate : ICustomRule
{
    // The namespace vocabulary this predicate is scoped to. Checked against each type's
    // EFFECTIVE namespace (see GetEffectiveNamespace) so nested types (whose own
    // TypeDefinition.Namespace is always empty) are still correctly attributed to
    // SharedKernel.Guards when their outermost enclosing type lives there.
    private const string GuardsNamespace = "SharedKernel.Guards";
    private const string GuardsNamespacePrefix = "SharedKernel.Guards.";

    // Mono.Cecil uses '/' for nested types; CLR uses '+'.
    // Both forms are checked to guard against version differences.
    private const string GuardThrowFullNameCecil = "SharedKernel.Guards.Guard/Throw";
    private const string GuardThrowFullNameClr = "SharedKernel.Guards.Guard+Throw";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) if the type is outside this predicate's
    /// <c>SharedKernel.Guards</c> namespace scope, or contains no method whose body includes
    /// an <see cref="OpCodes.Throw"/> instruction; <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the type is out of the <c>SharedKernel.Guards</c> namespace
    /// scope, is free of throw opcodes, or is excluded by the companion-class or
    /// compiler-generated exclusions; <see langword="false"/> when the type is in scope and
    /// any method body contains a <see cref="OpCodes.Throw"/> instruction.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Scope guard — evaluated FIRST, mirroring this domain's established convention of
        // embedding a namespace exemption inside the predicate itself (see
        // PersistenceLayerProtectionRules/NoDirectSaveChangesPredicate). Anything outside the
        // SharedKernel.Guards vocabulary is not this predicate's concern, regardless of
        // whether it happens to implement IGuardClause or contains a throw.
        var effectiveNamespace = GetEffectiveNamespace(type);
        if (effectiveNamespace != GuardsNamespace
            && !effectiveNamespace.StartsWith(GuardsNamespacePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        // Exclude the Guard.Throw companion class — it is the legitimate imperative path.
        // Mono.Cecil uses '/' for nested type separators; CLR uses '+'. Both are checked.
        if (type.FullName == GuardThrowFullNameCecil
            || type.FullName == GuardThrowFullNameClr)
        {
            return true;
        }

        foreach (var method in type.Methods)
        {
            // Skip abstract / external methods (no body to inspect)
            if (method.Body is null)
                continue;

            // Skip compiler-generated accessor methods on compiler-generated types
            if (IsCompilerGeneratedAccessor(method))
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Throw)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Walks up <see cref="TypeDefinition.DeclaringType"/> for a nested type until it reaches
    /// the outermost, non-nested enclosing type, then returns that type's
    /// <see cref="TypeDefinition.Namespace"/>.
    /// </summary>
    /// <remarks>
    /// Mono.Cecil (mirroring the underlying CLR metadata's TypeDef table) never populates
    /// <see cref="TypeDefinition.Namespace"/> for a nested type — it is always the empty
    /// string. A nested type's real namespace is only recoverable by walking up to its
    /// outermost enclosing type. For a non-nested type, this returns the type's own
    /// <see cref="TypeDefinition.Namespace"/> unchanged.
    /// </remarks>
    private static string GetEffectiveNamespace(TypeDefinition type)
    {
        var current = type;
        while (current.IsNested && current.DeclaringType is not null)
        {
            current = current.DeclaringType;
        }

        return current.Namespace ?? string.Empty;
    }

    private static bool IsCompilerGeneratedAccessor(MethodDefinition method)
    {
        if (!method.HasCustomAttributes)
            return false;

        // Check for [CompilerGenerated] attribute on the method
        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.FullName ==
                "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
            {
                return true;
            }
        }

        return false;
    }
}
