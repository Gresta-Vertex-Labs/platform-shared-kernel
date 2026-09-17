using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates mechanically enforcing <c>12.Security</c>'s three documented
/// hard rules — a layering rule, a construction-path rule (SK0031,
/// <c>Diagnostics.RawSecurityContextConstructorInjectionAnalyzer</c> in
/// <c>SharedKernel.Analyzers</c>), and a DI-lifetime rule — none of which had any governance
/// coverage before this class was introduced. The platform's first
/// <c>12.Security</c>-domain architecture-rule class.
/// </summary>
/// <remarks>
/// <para>
/// Both factory methods accept an <see cref="Assembly"/> (or <see cref="Assembly"/>[]) parameter
/// and return a <see cref="ConditionList"/> — consistent with the established
/// <c>ArchitectureRuleBase</c> API. Neither method carries an SK diagnostic ID — following the
/// <see cref="EfCorePackageHygieneRules"/>/<see cref="RedisTopologyRules"/> "boundary/regression
/// rule, no ID" convention. Both reuse the existing <c>Mono.Cecil &gt;= 0.11.5</c> reference
/// already in <c>SharedKernel.ArchitectureTests</c> — zero new NuGet dependency.
/// </para>
/// <list type="bullet">
///   <item><description>
///     Rule 1 — <see cref="DomainNeverReferencesTenantProvider"/>: mechanizes "Domain code
///     (<c>03.Domain</c>) must never reference <c>ITenantProvider</c> — it receives tenantId as a
///     primitive."
///   </description></item>
///   <item><description>
///     Rule 2 — <see cref="NoSingletonRegistrationOfSecurityContextTypes"/>: mechanizes
///     "<c>IUserContext</c> and <c>ITenantProvider</c> are scoped — one instance per HTTP request.
///     Never register as singleton."
///   </description></item>
///   <item><description>
///     Rule 3 — <see cref="DpopProofValidationNeverDuplicatedOutsideOidc"/>:
///     mechanizes "DPoP (RFC 9449) proof-validation logic lives exclusively in
///     <c>SharedKernel.Security.Oidc</c>," applied proactively before that surface shipped.
///   </description></item>
///   <item><description>
///     Rule 4 — <see cref="ClientCertificateAccessNeverDuplicatedOutsideMtls"/>:
///     mechanizes "mTLS client-certificate trust/validation logic lives exclusively in
///     <c>SharedKernel.Security.Mtls</c>," applied proactively before that package shipped.
///   </description></item>
/// </list>
/// <para>
/// The third documented hard rule — "Application-layer and domain-adjacent code must inject
/// <c>IUserContext</c>/<c>ITenantProvider</c> — never <c>IHttpContextAccessor</c>,
/// <c>ClaimsPrincipal</c>, or <c>HttpContext</c> directly" — is mechanized separately as SK0031
/// (<c>Diagnostics.RawSecurityContextConstructorInjectionAnalyzer</c>), a Roslyn analyzer,
/// not an architecture test — it is a per-call-site syntax pattern, not an assembly-dependency
/// or IL-shape concern.
/// </para>
/// <para>
/// Real-assembly verification for both rules is non-gating and immediately available: both
/// <c>12.Security</c> (<c>.Abstractions</c>/<c>.Oidc</c>) and <c>03.Domain</c> are already fully
/// Published as of this phase's authoring.
/// </para>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class SecurityArchitectureRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied
    /// <c>03.Domain</c> assembly references
    /// <c>SharedKernel.Security.Abstractions.ITenantProvider</c> via a field type,
    /// constructor/method parameter type, or method-call instruction operand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>03.Domain</c>'s aggregates and domain services must never depend on the request-scoped
    /// <c>ITenantProvider</c> abstraction directly — the application layer resolves
    /// <c>ITenantProvider.TenantId</c> and passes it as a <see cref="System.Guid"/> primitive to
    /// domain constructors and methods. <c>ITenantProvider</c> reaching into <c>03.Domain</c>
    /// would reintroduce exactly the infrastructure-in-domain coupling the platform's
    /// <see cref="DomainLayerPurityRules"/>/<see cref="PersistenceLayerProtectionRules"/> precedent
    /// already forecloses for persistence.
    /// </para>
    /// <para>
    /// See <see cref="NoTenantProviderReferenceInDomainPredicate"/> for the full three-surface
    /// detection technique — no exemption is applied; <c>03.Domain</c> must never reference
    /// <c>ITenantProvider</c> under any circumstance.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>class PricingPolicy(ITenantProvider tenantProvider) : DomainService { ... }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// class PricingPolicy : DomainService
    /// {
    ///     public Result&lt;Money&gt; Reprice(Guid tenantId, ...) { ... }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="domainAssembly">
    /// The assembly to evaluate — typically <c>SharedKernel.Domain</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type references
    /// <c>ITenantProvider</c> in the supplied domain assembly.
    /// </returns>
    public static ConditionList DomainNeverReferencesTenantProvider(Assembly domainAssembly) =>
        Types
            .InAssembly(domainAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoTenantProviderReferenceInDomainPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no method body in the supplied
    /// assemblies contains a closed-generic <c>AddSingleton</c> registration call whose generic
    /// arguments include
    /// <c>SharedKernel.Security.Abstractions.IUserContext</c> or
    /// <c>SharedKernel.Security.Abstractions.ITenantProvider</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IUserContext</c>/<c>ITenantProvider</c> are documented as request-scoped — one instance
    /// per HTTP request, resolved from <c>IHttpContextAccessor</c> at construction time. A
    /// singleton registration would capture the FIRST resolved request's identity/tenant context
    /// and silently leak it across every subsequent request on the same process — a severe
    /// cross-tenant/cross-user data-leak defect, not a style violation.
    /// </para>
    /// <para>
    /// See <see cref="NoSecurityContextSingletonRegistrationPredicate"/> for the full
    /// generic-instance-method-argument detection technique and its documented non-generic-overload
    /// limitation — no exemption is applied.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>services.AddSingleton&lt;IUserContext, OidcUserContext&gt;();</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>services.AddScoped&lt;IUserContext, OidcUserContext&gt;();</code>
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The assemblies to evaluate — typically <c>SharedKernel.Security.Oidc</c> and any other
    /// package that registers <c>IUserContext</c>/<c>ITenantProvider</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no supplied assembly registers
    /// <c>IUserContext</c>/<c>ITenantProvider</c> as singleton.
    /// </returns>
    public static ConditionList NoSingletonRegistrationOfSecurityContextTypes(
        params Assembly[] assemblies) =>
        Types
            .InAssemblies(assemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoSecurityContextSingletonRegistrationPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied assemblies,
    /// outside <c>SharedKernel.Security.Oidc</c>, references the raw <c>"DPoP"</c> header-name
    /// string literal or performs proof-JWT parsing via <c>JwtSecurityTokenHandler</c>/
    /// <c>JsonWebTokenHandler</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mechanizes "this validation logic lives in exactly one package" for DPoP
    /// (RFC 9449) sender-constrained proof validation, applying the
    /// same lesson proactively rather than retroactively —
    /// before <c>12.Security</c>'s DPoP surface even shipped, not after a future gold-standard
    /// review discovered the drift.
    /// </para>
    /// <para>
    /// See <see cref="NoDpopProofValidationDuplicationPredicate"/> for the full two-surface
    /// detection technique. Exemption: <c>SharedKernel.Security.Oidc</c> only — the real,
    /// DPoP-proof-validating implementation package.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> a type outside <c>SharedKernel.Security.Oidc</c> reads
    /// <c>Request.Headers["DPoP"]</c> and hand-parses the proof JWT itself.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> the type calls into
    /// <c>SharedKernel.Security.Oidc</c>'s own DPoP proof-validation surface instead of
    /// duplicating header-name literals or JWT parsing.
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The assemblies to evaluate — typically every platform production assembly, including
    /// <c>SharedKernel.Security.Oidc</c> itself (which is exempted internally by the predicate).
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no supplied assembly duplicates DPoP
    /// proof-validation logic outside <c>SharedKernel.Security.Oidc</c>.
    /// </returns>
    public static ConditionList DpopProofValidationNeverDuplicatedOutsideOidc(
        params Assembly[] assemblies) =>
        Types
            .InAssemblies(assemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoDpopProofValidationDuplicationPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied assemblies,
    /// outside <c>SharedKernel.Security.Mtls</c>, reads
    /// <c>HttpContext.Connection.ClientCertificate</c> directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mechanizes "this validation logic lives in exactly one package" for mTLS
    /// client-certificate trust/validation, applying the same proactive-locality motivation as
    /// <see cref="DpopProofValidationNeverDuplicatedOutsideOidc"/> — before
    /// <c>SharedKernel.Security.Mtls</c> even shipped, not after a future gold-standard review
    /// discovered the drift.
    /// </para>
    /// <para>
    /// See <see cref="NoRawClientCertificateAccessOutsideMtlsPredicate"/> for the full
    /// single-surface detection technique. Exemption: <c>SharedKernel.Security.Mtls</c> only —
    /// the sibling provider package that owns certificate trust/validation.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> a type outside <c>SharedKernel.Security.Mtls</c>
    /// reads <c>httpContext.Connection.ClientCertificate</c> directly to perform its own trust
    /// decision.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> the type calls into
    /// <c>SharedKernel.Security.Mtls</c>'s own certificate-validation surface instead of reading
    /// the raw connection property itself.
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The assemblies to evaluate — typically every platform production assembly, including
    /// <c>SharedKernel.Security.Mtls</c> itself (which is exempted internally by the predicate).
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no supplied assembly reads
    /// <c>ConnectionInfo.ClientCertificate</c> directly outside
    /// <c>SharedKernel.Security.Mtls</c>.
    /// </returns>
    public static ConditionList ClientCertificateAccessNeverDuplicatedOutsideMtls(
        params Assembly[] assemblies) =>
        Types
            .InAssemblies(assemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoRawClientCertificateAccessOutsideMtlsPredicate());
}
