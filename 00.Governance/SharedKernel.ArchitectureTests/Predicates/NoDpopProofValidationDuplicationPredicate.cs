using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (WO-058 P-383) that fails any type outside
/// <c>SharedKernel.Security.Oidc</c> that either references the raw <c>"DPoP"</c> header-name
/// string literal or performs proof-JWT parsing via <c>JwtSecurityTokenHandler</c>/
/// <c>JsonWebTokenHandler</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc"/>
/// to mechanize "this validation logic lives in exactly one package" for DPoP (RFC 9449)
/// sender-constrained proof validation, applying the <c>SK.00.SecurityContextGuard</c> (P-373)
/// lesson proactively rather than retroactively — before <c>12.Security</c>'s DPoP surface even
/// shipped, not after a future gold-standard review discovered the drift.
/// </para>
/// <para>
/// <strong>Namespace exemption (first check):</strong> Types whose
/// <see cref="TypeDefinition.Namespace"/> starts with <c>"SharedKernel.Security.Oidc"</c> are
/// returned as passing (<see langword="true"/>) unconditionally — that package is the sole
/// legitimate home for DPoP proof-validation logic. No second forward-looking exemption prefix,
/// unlike SK0031's two-namespace shape — DPoP is exclusively an OIDC/JWT-bearer-adjacent concern,
/// not spread across every identity provider package.
/// </para>
/// <para>
/// <strong>Detection surfaces</strong> (either match violates the rule):
/// <list type="bullet">
///   <item><description>
///     Surface (a) — an <see cref="OpCodes.Ldstr"/> instruction whose operand is exactly
///     <c>"DPoP"</c> (case-sensitive, the RFC 9449 canonical header name) — reuses the Ldstr
///     literal-collection technique already established by
///     <see cref="NoConflictingLivenessReadinessTagsPredicate"/>/<see cref="NoBareHealthCheckLiteralWhereConstantsExistPredicate"/>.
///   </description></item>
///   <item><description>
///     Surface (b) — a <c>Call</c>/<c>Callvirt</c>/<c>Newobj</c> instruction whose resolved
///     operand's declaring type <see cref="TypeReference.FullName"/> is exactly
///     <c>System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler</c> or
///     <c>Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler</c> — reuses
///     <see cref="NoRawSymmetricCipherOutsideCryptographyPredicate"/>'s raw-type-reference
///     technique, applied to JWT-proof-parsing types instead of cipher types.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Offending pattern:</strong> a type outside <c>SharedKernel.Security.Oidc</c> reads
/// <c>Request.Headers["DPoP"]</c> and hand-parses the proof JWT itself.
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong> the type calls into <c>SharedKernel.Security.Oidc</c>'s
/// own DPoP proof-validation surface instead of duplicating header-name literals or JWT parsing.
/// </para>
/// </remarks>
public sealed class NoDpopProofValidationDuplicationPredicate : ICustomRule
{
    private const string ExemptNamespacePrefix = "SharedKernel.Security.Oidc";
    private const string DpopHeaderLiteral = "DPoP";

    private static readonly HashSet<string> ForbiddenJwtHandlerFullNames = new(StringComparer.Ordinal)
    {
        "System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler",
        "Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types in the
    /// <c>SharedKernel.Security.Oidc</c> namespace and for types whose method bodies contain
    /// neither a raw <c>"DPoP"</c> string literal nor a reference to a JWT-proof-parsing handler
    /// type; <see langword="false"/> when either surface is found outside that namespace.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a DPoP-literal or JWT-handler reference is found outside
    /// <c>SharedKernel.Security.Oidc</c>; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Namespace exemption — SharedKernel.Security.Oidc is the sole legitimate home for DPoP
        // proof-validation logic.
        if (type.Namespace is not null &&
            type.Namespace.StartsWith(ExemptNamespacePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                // Surface (a): raw "DPoP" header-name literal.
                if (instruction.OpCode == OpCodes.Ldstr &&
                    instruction.Operand is string literal &&
                    literal == DpopHeaderLiteral)
                {
                    return false;
                }

                // Surface (b): JWT-proof-parsing handler type reference.
                if (instruction.OpCode == OpCodes.Call ||
                    instruction.OpCode == OpCodes.Callvirt ||
                    instruction.OpCode == OpCodes.Newobj)
                {
                    if (instruction.Operand is MethodReference methodRef &&
                        IsForbiddenJwtHandlerType(methodRef.DeclaringType))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool IsForbiddenJwtHandlerType(TypeReference typeRef)
    {
        if (typeRef is null)
            return false;

        // Unwrap generic instances to get the element type's full name.
        var elementType = typeRef is GenericInstanceType git ? git.ElementType : typeRef;

        return ForbiddenJwtHandlerFullNames.Contains(elementType.FullName);
    }
}
