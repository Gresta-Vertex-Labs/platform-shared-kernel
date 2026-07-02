using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (SK0301) that fails any type outside the
/// <c>SharedKernel.Cryptography</c> namespace whose fields or method bodies reference
/// <c>System.Security.Cryptography.AesGcm</c>, <c>System.Security.Cryptography.Aes</c>, or
/// <c>System.Security.Cryptography.SymmetricAlgorithm</c> directly.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.EncryptionPatternGuardRules"/> to enforce that cipher usage in
/// <c>03.Domain</c> or <c>05.Application</c> assemblies is prohibited. All field-level
/// encryption must route through the persistence-layer <c>EncryptedValueConverter&lt;T&gt;</c>
/// wired via <c>PropertyBuilder&lt;T&gt;.Encrypt()</c> in <c>IEntityTypeConfiguration&lt;T&gt;</c>.
/// </para>
/// <para>
/// <strong>Namespace exemption (first guard):</strong> Types whose
/// <see cref="TypeDefinition.Namespace"/> starts with <c>"SharedKernel.Cryptography"</c> are
/// returned as passing (<see langword="true"/>) unconditionally — this is the
/// <em>sole</em> legitimate crypto consumer in the platform (narrowed in WO-037 P-229 from
/// the original two-namespace exemption <c>"SharedKernel.Persistence.*"</c> /
/// <c>"SharedKernel.Security.*"</c>; both layers now route through
/// <c>SharedKernel.Cryptography</c>'s <c>ISymmetricEncryptionService</c> /
/// <c>AesGcmEncryptionService</c> instead of touching BCL cipher types directly).
/// </para>
/// <para>
/// <strong>Detection surfaces:</strong>
/// <list type="bullet">
///   <item><description>
///     <c>TypeDefinition.Fields</c> — checks <c>FieldDefinition.FieldType.Namespace ==
///     "System.Security.Cryptography"</c> and <c>FieldDefinition.FieldType.Name</c> in the
///     forbidden set.
///   </description></item>
///   <item><description>
///     <c>TypeDefinition.Methods.Body.Instructions</c> — for <c>Call</c>, <c>Callvirt</c>,
///     and <c>Newobj</c> opcodes, resolves the declaring type namespace and name against the
///     same forbidden set.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>class OrderEncryptionHelper { private AesGcm _cipher = new(key); }</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong> configure encryption via <c>PropertyBuilder&lt;T&gt;.Encrypt()</c>
/// in <c>IEntityTypeConfiguration&lt;T&gt;</c>; never reference <c>AesGcm</c> in domain or
/// application code.
/// </para>
/// </remarks>
public sealed class NoAesCipherInDomainOrApplicationPredicate : ICustomRule
{
    private const string CryptoNamespace = "System.Security.Cryptography";

    private static readonly HashSet<string> ForbiddenCipherNames = new(StringComparer.Ordinal)
    {
        "AesGcm",
        "Aes",
        "SymmetricAlgorithm",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types in the exempted namespaces and for
    /// types whose fields and method bodies contain no direct reference to forbidden cipher types;
    /// <see langword="false"/> when a forbidden cipher reference is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a direct <c>System.Security.Cryptography</c> cipher type
    /// reference is found outside <c>SharedKernel.Cryptography</c>;
    /// <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Namespace exemption — SharedKernel.Cryptography is the sole legitimate direct caller
        // of BCL cipher types (narrowed from Persistence+Security in WO-037 P-229).
        if (type.Namespace is not null &&
            type.Namespace.StartsWith("SharedKernel.Cryptography", StringComparison.Ordinal))
        {
            return true;
        }

        // Surface 1: field type declarations
        foreach (var field in type.Fields)
        {
            if (IsForbiddenCipherType(field.FieldType))
                return false;
        }

        // Surface 2: IL instruction walk — Call, Callvirt, Newobj
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call &&
                    instruction.OpCode != OpCodes.Callvirt &&
                    instruction.OpCode != OpCodes.Newobj)
                {
                    continue;
                }

                if (instruction.Operand is MethodReference methodRef &&
                    IsForbiddenCipherType(methodRef.DeclaringType))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsForbiddenCipherType(TypeReference typeRef)
    {
        if (typeRef is null)
            return false;

        // Unwrap generic instances to get the element type namespace/name
        var elementType = typeRef is GenericInstanceType git ? git.ElementType : typeRef;

        return elementType.Namespace == CryptoNamespace &&
               ForbiddenCipherNames.Contains(elementType.Name);
    }
}
