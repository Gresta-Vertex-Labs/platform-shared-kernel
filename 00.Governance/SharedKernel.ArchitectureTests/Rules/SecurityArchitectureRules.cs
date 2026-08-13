using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates mechanically enforcing <c>12.Security</c>'s three documented
/// hard rules — a layering rule, a construction-path rule (SK0031,
/// <see cref="Diagnostics.RawSecurityContextConstructorInjectionAnalyzer"/> in
/// <c>SharedKernel.Analyzers</c>), and a DI-lifetime rule — none of which had any governance
/// coverage before this class was introduced (WO-057 P-373). The platform's first
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
/// </list>
/// <para>
/// The third documented hard rule — "Application-layer and domain-adjacent code must inject
/// <c>IUserContext</c>/<c>ITenantProvider</c> — never <c>IHttpContextAccessor</c>,
/// <c>ClaimsPrincipal</c>, or <c>HttpContext</c> directly" — is mechanized separately as SK0031
/// (<see cref="Diagnostics.RawSecurityContextConstructorInjectionAnalyzer"/>), a Roslyn analyzer,
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
    /// <c>SharedKernel.Security.Abstractions.Abstractions.ITenantProvider</c> via a field type,
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
    /// <c>SharedKernel.Security.Abstractions.Abstractions.IUserContext</c> or
    /// <c>SharedKernel.Security.Abstractions.Abstractions.ITenantProvider</c>.
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
}
