using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails a read-only repository — a type implementing an
/// <c>IReadRepository</c>-prefixed interface and no <c>IRepository</c>-prefixed one — whose code calls a method
/// named <c>AsTracking</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.PersistenceInterfaceOwnershipRules.ReadOnlyRepositoriesNeverTrack"/>. The read contract
/// (P-558) never tracks what it returns; tracked loads belong to the write-side <c>IRepository</c>, which extends
/// the read contract and is therefore out of scope.
/// </para>
/// <para>
/// <strong>Detection:</strong> every method body of the type and of its nested types (async state machines,
/// closures) is scanned for a <c>call</c>/<c>callvirt</c> to a method named <c>AsTracking</c>, whatever its
/// declaring type or overload.
/// </para>
/// </remarks>
public sealed class ReadOnlyRepositoryNeverTracksPredicate : ICustomRule
{
    private const string ForbiddenMethodName = "AsTracking";
    private const string ReadRepositoryPrefix = "IReadRepository";
    private const string WriteRepositoryPrefix = "IRepository";

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when a read-only repository calls <c>AsTracking</c>;
    /// <see langword="true"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns><see langword="false"/> for a violation; <see langword="true"/> for every other type.</returns>
    public bool MeetsRule(TypeDefinition type)
    {
        if (!IsReadOnlyRepository(type))
            return true;

        return !CallsAsTracking(type);
    }

    private static bool IsReadOnlyRepository(TypeDefinition type)
    {
        if (!type.HasInterfaces)
            return false;

        var read = false;
        foreach (var iface in type.Interfaces)
        {
            var name = iface.InterfaceType.Name;
            if (name.StartsWith(WriteRepositoryPrefix, StringComparison.Ordinal))
                return false;

            if (name.StartsWith(ReadRepositoryPrefix, StringComparison.Ordinal))
                read = true;
        }

        return read;
    }

    private static bool CallsAsTracking(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                    && instruction.Operand is MethodReference called
                    && string.Equals(called.Name, ForbiddenMethodName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        foreach (var nested in type.NestedTypes)
        {
            if (CallsAsTracking(nested))
                return true;
        }

        return false;
    }
}
