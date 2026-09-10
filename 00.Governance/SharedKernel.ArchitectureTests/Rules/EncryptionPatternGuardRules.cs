using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates (SK0301–SK0304) that close the four most likely misuse
/// patterns of the AES-256-GCM field-level encryption subsystem.
/// </summary>
/// <remarks>
/// <para>
/// All factory methods accept an <see cref="Assembly"/> (or <c>params Assembly[]</c>) parameter
/// and return a <see cref="ConditionList"/> — consistent with the established
/// <c>ArchitectureRuleBase</c> API. Call <c>.GetResult()</c> on the returned
/// <see cref="ConditionList"/> to evaluate the rule, or pass it to
/// <see cref="Helpers.ArchitectureRuleBase.AssertRule"/> to throw on violation.
/// </para>
/// <para>
/// The four rules form an interlocking guard ring:
/// <list type="bullet">
///   <item><description>
///     SK0301 (<see cref="NoCryptoCipherInDomainOrApplication"/>) — cipher usage in domain or
///     application assemblies is prohibited; all encryption must route through the persistence layer.
///   </description></item>
///   <item><description>
///     SK0302 (<see cref="NoEncryptionAttributeOnDomainEntities"/>) — encryption attributes on
///     domain entity classes leak infrastructure concerns and bypass
///     <c>EncryptionModelConvention</c>.
///   </description></item>
///   <item><description>
///     SK0303 (<see cref="NoEncryptionRotationJobInjectionInDomainOrApplication"/>) —
///     <c>IEncryptionRotationJob</c> injection must be restricted to hosted services, Hangfire
///     jobs, Temporal activities, and management controllers.
///   </description></item>
///   <item><description>
///     SK0304 (<see cref="NoDirectEncryptedValueConverterInstantiation"/>) — direct
///     <c>new EncryptedValueConverter&lt;T&gt;()</c> in EF Core configuration classes bypasses
///     the convention auto-wire, causing double-encryption or inconsistent key handling.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// The 03xx SK ID block is dedicated to the encryption subsystem.
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class EncryptionPatternGuardRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in any of the supplied
    /// assemblies references <c>System.Security.Cryptography.AesGcm</c>,
    /// <c>System.Security.Cryptography.Aes</c>, or
    /// <c>System.Security.Cryptography.SymmetricAlgorithm</c> directly via field declarations
    /// or IL instruction operands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Types whose namespace starts with <c>"SharedKernel.Cryptography"</c> are unconditionally
    /// exempt — this is the <em>sole</em> legitimate direct caller of BCL cipher types in the
    /// platform (narrowed from an earlier two-namespace exemption
    /// <c>"SharedKernel.Persistence.*"</c> / <c>"SharedKernel.Security.*"</c>; both layers now
    /// route through <c>SharedKernel.Cryptography</c>'s <c>ISymmetricEncryptionService</c> /
    /// <c>AesGcmEncryptionService</c> instead of touching BCL cipher types directly).
    /// </para>
    /// <para>
    /// This method accepts multiple assemblies because the rule is typically applied to both the
    /// domain assembly (<c>typeof(SomeDomainEntity).Assembly</c>) and the application assembly
    /// (<c>typeof(SomeCommandHandler).Assembly</c>) simultaneously.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>class OrderEncryptionHelper { private AesGcm _cipher = new(key); }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> configure encryption via
    /// <c>PropertyBuilder&lt;T&gt;.Encrypt()</c> in <c>IEntityTypeConfiguration&lt;T&gt;</c>.
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// One or more assemblies to evaluate — typically the <c>03.Domain</c> and
    /// <c>05.Application</c> assemblies. Supply via <c>typeof(SomeDomainType).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type in the supplied assemblies references
    /// forbidden cipher types from <c>System.Security.Cryptography</c>.
    /// </returns>
    public static ConditionList NoCryptoCipherInDomainOrApplication(params Assembly[] assemblies)
    {
        var types = Types.InAssemblies(assemblies);

        return types
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoAesCipherInDomainOrApplicationPredicate());
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied domain
    /// assembly carries a custom attribute whose name contains <c>"Encrypt"</c> as a
    /// case-insensitive substring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The predicate is scoped to the domain assembly supplied by the caller via
    /// <c>typeof(SomeDomainEntity).Assembly</c>. Non-domain types are never passed to this rule.
    /// </para>
    /// <para>
    /// The case-insensitive <c>"Encrypt"</c> substring check intentionally catches all common
    /// attribute-naming conventions: <c>[Encrypted]</c>, <c>[EncryptedColumn]</c>,
    /// <c>[EncryptAttribute]</c>, <c>[ShouldEncrypt]</c>, etc. If a future attribute with
    /// <c>"Encrypt"</c> in its name is legitimately placed on a domain type for non-encryption
    /// purposes, document the exemption in <c>00.Governance/CLAUDE.md</c> before adding a
    /// name-specific exclusion to the predicate.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>[EncryptedColumn] public string Ssn { get; private set; }  // on a domain entity</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>builder.Property(x =&gt; x.Ssn).Encrypt();  // inside IEntityTypeConfiguration</code>
    /// </para>
    /// </remarks>
    /// <param name="domainAssembly">
    /// The domain assembly to evaluate — supply via <c>typeof(SomeDomainEntity).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type in the domain assembly carries an
    /// <c>"Encrypt*"</c> attribute.
    /// </returns>
    public static ConditionList NoEncryptionAttributeOnDomainEntities(Assembly domainAssembly) =>
        Types
            .InAssembly(domainAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoEncryptionAttributeOnDomainEntityPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in any of the supplied
    /// assemblies injects <c>IEncryptionRotationJob</c> as a constructor parameter, outside
    /// the designated exemption list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Exemption list (applied inside the predicate):</strong>
    /// <list type="bullet">
    ///   <item><description>
    ///     Types whose namespace starts with <c>"SharedKernel.Persistence"</c> — the
    ///     interface's own package.
    ///   </description></item>
    ///   <item><description>
    ///     Types whose name contains any of: <c>"RotationJob"</c>, <c>"HostedService"</c>,
    ///     <c>"Controller"</c>, <c>"Activity"</c> — designated infrastructure consumers
    ///     of the rotation job interface (hosted services, Hangfire/Temporal jobs, management
    ///     API controllers).
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>class RotateKeysCommandHandler(IEncryptionRotationJob rotationJob) { }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>class EncryptionKeyRotationHostedService(IEncryptionRotationJob rotationJob) { }</code>
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// One or more assemblies to evaluate — typically the <c>03.Domain</c> and
    /// <c>05.Application</c> assemblies. Supply via <c>typeof(SomeDomainType).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no non-exempt type injects
    /// <c>IEncryptionRotationJob</c> in a constructor.
    /// </returns>
    public static ConditionList NoEncryptionRotationJobInjectionInDomainOrApplication(
        params Assembly[] assemblies)
    {
        var types = Types.InAssemblies(assemblies);

        return types
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoEncryptionRotationJobInjectionPredicate());
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no <c>IEntityTypeConfiguration&lt;T&gt;</c>
    /// implementor in the supplied assembly directly instantiates <c>EncryptedValueConverter&lt;T&gt;</c>
    /// via a <c>newobj</c> IL instruction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is scoped exclusively to <c>IEntityTypeConfiguration&lt;T&gt;</c> implementors.
    /// General application code that is not an EF Core configuration class is not subject to
    /// this rule.
    /// </para>
    /// <para>
    /// <c>EncryptionModelConvention</c> (registered via
    /// <c>EfCorePersistenceBuilder.WithEncryption()</c>) is unconditionally exempt — it is the
    /// sole legitimate instantiation site and must never be flagged.
    /// </para>
    /// <para>
    /// Direct instantiation bypasses the convention, causing either duplicate converter
    /// registration (double-encryption of stored data) or inconsistent key-version handling
    /// across the model.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// builder.Property(x =&gt; x.Ssn)
    ///     .HasConversion(new EncryptedValueConverter&lt;string&gt;(options));
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>builder.Property(x =&gt; x.Ssn).Encrypt();</code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">
    /// The assembly to evaluate — typically the persistence assembly containing EF Core
    /// configuration classes. Supply via <c>typeof(SomeEntityTypeConfiguration).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no <c>IEntityTypeConfiguration&lt;T&gt;</c>
    /// implementor (other than <c>EncryptionModelConvention</c>) directly instantiates
    /// <c>EncryptedValueConverter&lt;T&gt;</c>.
    /// </returns>
    public static ConditionList NoDirectEncryptedValueConverterInstantiation(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoDirectEncryptedValueConverterInstantiationPredicate());
}
