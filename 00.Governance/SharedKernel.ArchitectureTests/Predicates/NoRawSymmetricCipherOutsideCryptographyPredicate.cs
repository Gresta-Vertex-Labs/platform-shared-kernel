using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (platform-wide extension of SK0301) that fails any type outside
/// the <c>SharedKernel.Cryptography</c> namespace whose fields or method bodies reference
/// <c>System.Security.Cryptography.AesGcm</c>, <c>System.Security.Cryptography.Aes</c>,
/// <c>System.Security.Cryptography.SymmetricAlgorithm</c>, or any
/// <c>System.Security.Cryptography.RandomNumberGenerator</c> member directly.
/// </summary>
/// <remarks>
/// <para>
/// This predicate is the platform-wide generalization of
/// <see cref="NoAesCipherInDomainOrApplicationPredicate"/> (which is scoped to whichever
/// assemblies the caller passes — historically just <c>03.Domain</c> and <c>05.Application</c>).
/// The new predicate is designed to be invoked against <strong>every</strong> production assembly
/// in the platform, closing the caller-scoping gap that allowed a hand-rolled <c>AesGcm</c>
/// usage to ship inside <c>SharedKernel.Persistence.*</c> without
/// being caught by SK0301.
/// </para>
/// <para>
/// <strong>Namespace exemption (first check):</strong> Types whose
/// <c>TypeDefinition.Namespace</c> starts with <c>"SharedKernel.Cryptography"</c> are
/// returned as passing (<see langword="true"/>) unconditionally —
/// <c>SharedKernel.Cryptography</c> is the <em>sole</em> legitimate direct caller of BCL cipher
/// and RNG types across the entire platform.
/// </para>
/// <para>
/// <strong>Detection surfaces:</strong>
/// <list type="bullet">
///   <item><description>
///     Surface 1 — <c>TypeDefinition.Fields</c>: checks
///     <c>FieldDefinition.FieldType.Namespace == "System.Security.Cryptography"</c> and
///     <c>FieldDefinition.FieldType.Name</c> in <c>{"AesGcm", "Aes", "SymmetricAlgorithm"}</c>.
///   </description></item>
///   <item><description>
///     Surface 2 — <c>TypeDefinition.Methods.Body.Instructions</c> (cipher types): for
///     <c>Call</c>, <c>Callvirt</c>, and <c>Newobj</c> opcodes, checks the resolved
///     <c>TypeReference.Namespace</c> and <c>TypeReference.Name</c> against the same
///     cipher-type set as Surface 1.
///   </description></item>
///   <item><description>
///     Surface 3 — <c>TypeDefinition.Methods.Body.Instructions</c> (RNG): for <c>Call</c>
///     and <c>Callvirt</c> opcodes, checks
///     <c>MethodReference.DeclaringType.FullName == "System.Security.Cryptography.RandomNumberGenerator"</c>.
///     A <c>DeclaringType</c> match (rather than a per-method-name match) catches all static
///     and instance entry points on <c>RandomNumberGenerator</c>:
///     <c>Fill</c>, <c>GetBytes</c>, <c>Create</c>, etc., in one pass.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Relationship to SK0301:</strong> SK0301 (<see cref="NoAesCipherInDomainOrApplicationPredicate"/>)
/// and this predicate share identical exemption logic (<c>SharedKernel.Cryptography</c> only)
/// and near-identical trigger logic (the <c>RandomNumberGenerator</c> surface is new here).
/// They differ only in which assemblies the consuming test suite passes. SK0301 is therefore
/// a narrower, caller-scoped special case by construction, not a contradictory duplicate.
/// No new SK diagnostic ID was assigned for this predicate — same diagnostic intent, wider scope.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>class SomeInfraHelper { private AesGcm _cipher = new(key); }  // outside SharedKernel.Cryptography</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong> inject
/// <c>SharedKernel.Cryptography.ISymmetricEncryptionService</c>; never reference
/// <c>AesGcm</c>, <c>Aes</c>, <c>SymmetricAlgorithm</c>, or <c>RandomNumberGenerator</c>
/// directly outside <c>SharedKernel.Cryptography</c>.
/// </para>
/// </remarks>
public sealed class NoRawSymmetricCipherOutsideCryptographyPredicate : ICustomRule
{
    private const string CryptoNamespace = "System.Security.Cryptography";
    private const string ExemptNamespacePrefix = "SharedKernel.Cryptography";
    private const string RngFullName = "System.Security.Cryptography.RandomNumberGenerator";

    private static readonly HashSet<string> ForbiddenCipherNames = new(StringComparer.Ordinal)
    {
        "AesGcm",
        "Aes",
        "SymmetricAlgorithm",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types in the
    /// <c>SharedKernel.Cryptography</c> namespace and for types whose fields and method
    /// bodies contain no direct reference to forbidden cipher types or
    /// <c>RandomNumberGenerator</c>; <see langword="false"/> when a forbidden reference is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a direct cipher or RNG reference is found outside
    /// <c>SharedKernel.Cryptography</c>; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Namespace exemption — SharedKernel.Cryptography is the sole legitimate direct caller.
        if (type.Namespace is not null &&
            type.Namespace.StartsWith(ExemptNamespacePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        // Surface 1: field type declarations (cipher types only)
        foreach (var field in type.Fields)
        {
            if (IsForbiddenCipherType(field.FieldType))
                return false;
        }

        // Surfaces 2 + 3: IL instruction walk
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                // Surface 2: Call/Callvirt/Newobj for forbidden cipher types
                if (instruction.OpCode == OpCodes.Call ||
                    instruction.OpCode == OpCodes.Callvirt ||
                    instruction.OpCode == OpCodes.Newobj)
                {
                    if (instruction.Operand is MethodReference methodRef)
                    {
                        if (IsForbiddenCipherType(methodRef.DeclaringType))
                            return false;
                    }
                }

                // Surface 3: Call/Callvirt for RandomNumberGenerator (any member)
                if (instruction.OpCode == OpCodes.Call ||
                    instruction.OpCode == OpCodes.Callvirt)
                {
                    if (instruction.Operand is MethodReference rngRef)
                    {
                        var declaringType = rngRef.DeclaringType;
                        var elementType = declaringType is GenericInstanceType git
                            ? git.ElementType
                            : declaringType;

                        if (elementType.FullName == RngFullName)
                            return false;
                    }
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
