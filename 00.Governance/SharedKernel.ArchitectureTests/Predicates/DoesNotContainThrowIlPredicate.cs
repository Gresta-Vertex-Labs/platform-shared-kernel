using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that inspects method bodies via Mono.Cecil IL inspection
/// and fails any type whose methods contain a <c>throw</c> IL opcode
/// (<see cref="OpCodes.Throw"/>).
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.GuardPurityRules"/> to enforce the functional-path purity
/// contract: types implementing <c>IGuardClause</c> on the <c>Guard.Against.*</c> path
/// must never throw — they must return <c>Error?</c>.
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
    // Mono.Cecil uses '/' for nested types; CLR uses '+'.
    // Both forms are checked to guard against version differences.
    private const string GuardThrowFullNameCecil = "SharedKernel.Guards.Guard/Throw";
    private const string GuardThrowFullNameClr = "SharedKernel.Guards.Guard+Throw";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) if the type contains no method whose body
    /// includes an <see cref="OpCodes.Throw"/> instruction; <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the type is free of throw opcodes (or is excluded by
    /// the companion-class or compiler-generated exclusions); <see langword="false"/> when
    /// any method body contains a <see cref="OpCodes.Throw"/> instruction.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
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
