using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type outside the
/// <c>SharedKernel.Persistence.EfCore</c> namespace whose methods contain a direct call to
/// <c>DbContext.SaveChanges</c> or <c>DbContext.SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.PersistenceLayerProtectionRules"/> to enforce that only
/// <c>EfUnitOfWork</c> (which lives in <c>SharedKernel.Persistence.EfCore</c>) may commit the
/// EF Core change-tracker. All other code must call <c>IUnitOfWork.CommitAsync()</c> to ensure
/// the interceptor chain (Audit, SoftDelete, Outbox, Concurrency) is never bypassed.
/// </para>
/// <para>
/// <strong>Exemption (EfUnitOfWork exclusion):</strong> Types whose
/// <c>TypeDefinition.Namespace</c> starts with <c>"SharedKernel.Persistence.EfCore"</c>
/// are returned as passing (<see langword="true"/>) unconditionally as the very first guard —
/// before any IL walk is performed.
/// </para>
/// <para>
/// The IL walk checks every <c>call</c> or <c>callvirt</c> instruction operand. An instruction
/// is a violation when its <c>MethodReference.DeclaringType</c>.<see cref="MemberReference.Name"/>
/// equals <c>"DbContext"</c> and the method <see cref="MemberReference.Name"/> is either
/// <c>"SaveChanges"</c> or <c>"SaveChangesAsync"</c>.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>await _dbContext.SaveChangesAsync();</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>await _unitOfWork.CommitAsync();</code>
/// </para>
/// </remarks>
public sealed class NoDirectSaveChangesPredicate : ICustomRule
{
    private static readonly HashSet<string> ForbiddenMethodNames = new(
        System.StringComparer.Ordinal)
    {
        "SaveChanges",
        "SaveChangesAsync",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types in the
    /// <c>SharedKernel.Persistence.EfCore</c> namespace (EfUnitOfWork exemption) and for
    /// types whose methods contain no direct <c>DbContext.SaveChanges[Async]</c> call;
    /// <see langword="false"/> when a forbidden call is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a direct <c>DbContext.SaveChanges[Async]</c> call is found
    /// outside the exempted namespace; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // EfUnitOfWork exclusion — must be the very first guard.
        // Types in SharedKernel.Persistence.EfCore are the only permitted callers.
        if (type.Namespace is not null &&
            type.Namespace.StartsWith("SharedKernel.Persistence.EfCore", System.StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call &&
                    instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is MethodReference methodRef &&
                    methodRef.DeclaringType.Name == "DbContext" &&
                    ForbiddenMethodNames.Contains(methodRef.Name))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
