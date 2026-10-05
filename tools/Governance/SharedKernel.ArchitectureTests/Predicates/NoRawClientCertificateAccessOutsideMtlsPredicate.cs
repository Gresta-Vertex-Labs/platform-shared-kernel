using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type outside
/// <c>SharedKernel.Security.Mtls</c> that reads <c>HttpContext.Connection.ClientCertificate</c>
/// directly.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.SecurityArchitectureRules.ClientCertificateAccessNeverDuplicatedOutsideMtls"/>
/// to mechanize "this validation logic lives in exactly one package" for mutual-TLS
/// client-certificate trust/validation, applying the same single-owner
/// lesson proactively rather than retroactively — before <c>SharedKernel.Security.Mtls</c> even
/// shipped, not after a future gold-standard review discovered the drift.
/// </para>
/// <para>
/// <strong>Namespace exemption (first check):</strong> Types whose
/// <c>TypeDefinition.Namespace</c> starts with <c>"SharedKernel.Security.Mtls"</c> are
/// returned as passing (<see langword="true"/>) unconditionally — that package is the sole
/// legitimate home for mTLS client-certificate trust/validation logic.
/// </para>
/// <para>
/// <strong>Detection surface (single):</strong> a <c>Call</c>/<c>Callvirt</c> instruction whose
/// resolved <c>MethodReference.Name</c> is exactly <c>"get_ClientCertificate"</c> and
/// whose <c>MethodReference.DeclaringType</c>.<see cref="MemberReference.FullName"/> is
/// exactly <c>"Microsoft.AspNetCore.Http.ConnectionInfo"</c> — the property-getter shape of
/// <c>HttpContext.Connection.ClientCertificate</c>. This single, precise signal satisfies both
/// halves of the "<c>HttpContext.Connection.ClientCertificate</c>/<c>X509Certificate2</c>"
/// acceptance criterion in one match, since <c>ConnectionInfo.ClientCertificate</c> IS declared
/// as <c>X509Certificate2?</c> — a bare "any <c>X509Certificate2</c> type reference" surface was
/// deliberately rejected as a second condition because <c>src/Foundation/SharedKernel.Cryptography</c>'s
/// <c>IAsymmetricSignatureService</c> legitimately handles X.509-adjacent cryptographic material
/// for unrelated (non-HTTP-connection) signing/verification purposes; a broad type-reference
/// match would false-positive there.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong> a type outside <c>SharedKernel.Security.Mtls</c> reads
/// <c>httpContext.Connection.ClientCertificate</c> directly to perform its own trust decision.
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong> the type calls into <c>SharedKernel.Security.Mtls</c>'s
/// own certificate-validation surface instead of reading the raw connection property itself.
/// </para>
/// </remarks>
public sealed class NoRawClientCertificateAccessOutsideMtlsPredicate : ICustomRule
{
    private const string ExemptNamespacePrefix = "SharedKernel.Security.Mtls";
    private const string ConnectionInfoFullName = "Microsoft.AspNetCore.Http.ConnectionInfo";
    private const string ClientCertificateGetterName = "get_ClientCertificate";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types in the
    /// <c>SharedKernel.Security.Mtls</c> namespace and for types whose method bodies contain no
    /// direct call to <c>ConnectionInfo.ClientCertificate</c>'s getter; <see langword="false"/>
    /// when such a call is found outside that namespace.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a raw <c>ClientCertificate</c>-getter call is found outside
    /// <c>SharedKernel.Security.Mtls</c>; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Namespace exemption — SharedKernel.Security.Mtls is the sole legitimate home for
        // client-certificate trust/validation logic.
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
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                    continue;

                if (instruction.Operand is MethodReference methodRef &&
                    methodRef.Name == ClientCertificateGetterName &&
                    methodRef.DeclaringType.FullName == ConnectionInfoFullName)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
