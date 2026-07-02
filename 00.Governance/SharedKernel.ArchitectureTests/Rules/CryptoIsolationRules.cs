using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Platform-wide raw-cipher isolation predicate introduced in WO-037 P-229.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a single factory method:
/// <see cref="NoRawSymmetricCipherOutsideCryptography"/> — a generalization of the
/// SK0301-backing <see cref="EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication"/>
/// predicate that is designed to be invoked against <strong>every</strong> production assembly
/// in the platform rather than only <c>03.Domain</c> and <c>05.Application</c>.
/// </para>
/// <para>
/// <strong>Motivation (P-227 incident):</strong>
/// A hand-rolled <c>AesGcm</c> usage shipped inside <c>SharedKernel.Persistence.*</c> without
/// being caught by SK0301 because the SK0301-backing predicate exempted that namespace wholesale,
/// and the calling test suite never passed the persistence assembly. This rule closes the gap by
/// being designed for platform-wide invocation: the consuming test suite passes every production
/// assembly; the <em>only</em> exemption is <c>SharedKernel.Cryptography</c> — the package that
/// legitimately owns direct cipher usage.
/// </para>
/// <para>
/// <strong>Relationship to SK0301:</strong>
/// <see cref="EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication"/> (SK0301)
/// and <see cref="NoRawSymmetricCipherOutsideCryptography"/> share identical exemption logic
/// (namespace prefix <c>"SharedKernel.Cryptography"</c>) and near-identical trigger logic
/// (the <c>RandomNumberGenerator</c> surface is new in the platform-wide rule). They differ
/// only in which assemblies the consuming test suite passes. SK0301 is a narrower,
/// caller-scoped special case by construction. No new SK diagnostic ID was minted for the
/// platform-wide rule — same diagnostic intent, wider caller-supplied scope.
/// </para>
/// <para>
/// Reuses the existing <c>Mono.Cecil &gt;= 0.11.5</c> reference already present in
/// <c>SharedKernel.ArchitectureTests</c> — zero new NuGet dependencies.
/// </para>
/// <para>
/// Lives in <c>SharedKernel.ArchitectureTests/Rules/CryptoIsolationRules.cs</c>.
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class CryptoIsolationRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in any of the supplied
    /// assemblies references <c>System.Security.Cryptography.AesGcm</c>,
    /// <c>System.Security.Cryptography.Aes</c>,
    /// <c>System.Security.Cryptography.SymmetricAlgorithm</c>, or any member of
    /// <c>System.Security.Cryptography.RandomNumberGenerator</c> directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Exemption:</strong> Types whose namespace starts with
    /// <c>"SharedKernel.Cryptography"</c> pass unconditionally — the sole legitimate platform
    /// caller of BCL cipher and RNG types (once <c>06.Persistence</c> P-227 lands,
    /// <c>EncryptedValueConverter</c> delegates to
    /// <c>SharedKernel.Cryptography.ISymmetricEncryptionService</c> / <c>AesGcmEncryptionService</c>
    /// instead of constructing <c>AesGcm</c> directly). No <c>SharedKernel.Security.*</c>
    /// exemption is carried forward: <c>12.Security.Oidc</c>'s JWT signing path must consume
    /// <c>SharedKernel.Cryptography.IHmacSigner</c> / <c>IAsymmetricSignatureService</c>
    /// rather than referencing BCL HMAC or asymmetric cipher types directly.
    /// </para>
    /// <para>
    /// <strong>Detection surfaces (inside
    /// <see cref="NoRawSymmetricCipherOutsideCryptographyPredicate"/>):</strong>
    /// <list type="number">
    ///   <item><description>
    ///     Field type declarations in <c>System.Security.Cryptography</c> named
    ///     <c>AesGcm</c>, <c>Aes</c>, or <c>SymmetricAlgorithm</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <c>Call</c>, <c>Callvirt</c>, and <c>Newobj</c> IL opcodes whose
    ///     <c>MethodReference.DeclaringType</c> is one of the three forbidden cipher types.
    ///   </description></item>
    ///   <item><description>
    ///     <c>Call</c> and <c>Callvirt</c> IL opcodes whose
    ///     <c>MethodReference.DeclaringType.FullName</c> equals
    ///     <c>"System.Security.Cryptography.RandomNumberGenerator"</c> — covers all static and
    ///     instance entry points (<c>Fill</c>, <c>GetBytes</c>, <c>Create</c>, etc.) in one
    ///     <c>DeclaringType</c> match.
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Recommended usage:</strong> pass every production assembly in the solution to this
    /// method, not just the layers most likely to violate it historically. The consuming test
    /// project is responsible for collecting and supplying all production assembly references.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// // In ANY package outside SharedKernel.Cryptography:
    /// class SomeInfraHelper { private AesGcm _cipher = new(key); }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// // Inject SharedKernel.Cryptography.ISymmetricEncryptionService instead.
    /// // Never reference AesGcm/Aes/SymmetricAlgorithm/RandomNumberGenerator directly
    /// // outside SharedKernel.Cryptography.
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// One or more assemblies to evaluate. Typically the caller supplies every production
    /// assembly in the platform. Supply via <c>typeof(SomeProductionType).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type in the supplied assemblies references
    /// forbidden cipher or RNG types from <c>System.Security.Cryptography</c>.
    /// </returns>
    public static ConditionList NoRawSymmetricCipherOutsideCryptography(params Assembly[] assemblies)
    {
        var types = Types.InAssemblies(assemblies);

        return types
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoRawSymmetricCipherOutsideCryptographyPredicate());
    }
}
