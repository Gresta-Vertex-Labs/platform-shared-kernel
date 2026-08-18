using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="SecurityArchitectureRules"/> — covering both predicates:
/// <c>DomainNeverReferencesTenantProvider</c> and
/// <c>NoSingletonRegistrationOfSecurityContextTypes</c>.
/// </summary>
/// <remarks>
/// T-286/T-287: Rule 1 — <c>DomainNeverReferencesTenantProvider</c> (contrived fire/pass paths).
/// T-288/T-289: Rule 2 — <c>NoSingletonRegistrationOfSecurityContextTypes</c> (contrived fire/pass
/// paths).
/// T-290/T-291: real-assembly, non-gating verification against the real, shipped
/// <c>SharedKernel.Domain</c> and <c>SharedKernel.Security.Oidc</c> assemblies — both packages
/// were already fully Published as of this phase's authoring, so real-assembly verification is
/// wired directly rather than deferred as a follow-up.
/// T-292–T-295: Rule 3 — <c>DpopProofValidationNeverDuplicatedOutsideOidc</c> (contrived fire
/// paths for both detection surfaces, an exemption pass path, and a negative-control pass path).
/// T-296–T-298: Rule 4 — <c>ClientCertificateAccessNeverDuplicatedOutsideMtls</c> (contrived fire
/// path, exemption pass path, negative-control pass path).
/// T-299/T-300: real-assembly verification against the real, shipped
/// <c>SharedKernel.Security.Oidc</c> DPoP surface and the real, shipped
/// <c>SharedKernel.Security.Mtls</c> assembly — originally tracked as a Cross-Domain Dependency
/// pending <c>12.Security</c> P-376/P-377, confirmed RESOLVED on disk before this phase's
/// implementation session (12.Security shipped its full WO-058/WO-060 scope), so wired directly
/// here rather than deferred.
/// </remarks>
public class SecurityArchitectureRulesTests
{
    // ---------------------------------------------------------------------------
    // T-286 — Rule 1 fire path: 03.Domain-shaped fixture accepts ITenantProvider as a ctor param
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-286: A fixture type accepting
    /// <c>SharedKernel.Security.Abstractions.Abstractions.ITenantProvider</c> as a constructor
    /// parameter must fail <see cref="SecurityArchitectureRules.DomainNeverReferencesTenantProvider"/>.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesTenantProvider_CtorParameterViolation_RuleFails()
    {
        const string source = """
            namespace SharedKernel.Security.Abstractions.Abstractions
            {
                // Stub simulating the real, shipped ITenantProvider — same namespace and name so
                // the exact-FullName match fires correctly.
                public interface ITenantProvider
                {
                    System.Guid TenantId { get; }
                }
            }

            namespace Domain.Pricing
            {
                // Violation: a domain type directly accepts ITenantProvider as a constructor
                // parameter, instead of receiving a Guid tenantId primitive from the application
                // layer.
                public sealed class PricingPolicy
                {
                    private readonly SharedKernel.Security.Abstractions.Abstractions.ITenantProvider _tenantProvider;

                    public PricingPolicy(
                        SharedKernel.Security.Abstractions.Abstractions.ITenantProvider tenantProvider)
                    {
                        _tenantProvider = tenantProvider;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DomainTenantProviderViolation", source);

        var result = SecurityArchitectureRules
            .DomainNeverReferencesTenantProvider(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "PricingPolicy accepts ITenantProvider directly as a constructor parameter — " +
                     "03.Domain must never reference ITenantProvider, only a Guid tenantId primitive");
    }

    // ---------------------------------------------------------------------------
    // T-287 — Rule 1 pass path: equivalent fixture accepts a Guid tenantId primitive instead
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-287: The equivalent fixture type accepting a <see cref="System.Guid"/>
    /// <c>tenantId</c> primitive instead of <c>ITenantProvider</c> must pass
    /// <see cref="SecurityArchitectureRules.DomainNeverReferencesTenantProvider"/>.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesTenantProvider_GuidTenantIdPrimitive_RulePasses()
    {
        const string source = """
            namespace SharedKernel.Security.Abstractions.Abstractions
            {
                public interface ITenantProvider
                {
                    System.Guid TenantId { get; }
                }
            }

            namespace Domain.Pricing
            {
                // Compliant: the application layer resolves ITenantProvider.TenantId and passes
                // it as a Guid primitive — no reference to ITenantProvider anywhere in this type.
                public sealed class PricingPolicy
                {
                    private readonly System.Guid _tenantId;

                    public PricingPolicy(System.Guid tenantId)
                    {
                        _tenantId = tenantId;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DomainTenantIdPrimitiveCompliant", source);

        var result = SecurityArchitectureRules
            .DomainNeverReferencesTenantProvider(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "PricingPolicy accepts a Guid tenantId primitive — no reference to " +
                     "ITenantProvider is present anywhere in the fixture assembly");
    }

    // ---------------------------------------------------------------------------
    // T-288 — Rule 2 fire path: AddSingleton<IUserContext, FixtureUserContext>() registration
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-288: A fixture DI-extension method calling
    /// <c>services.AddSingleton&lt;IUserContext, FixtureUserContext&gt;()</c> must fail
    /// <see cref="SecurityArchitectureRules.NoSingletonRegistrationOfSecurityContextTypes"/>.
    /// </summary>
    [Fact]
    public void NoSingletonRegistrationOfSecurityContextTypes_AddSingletonUserContext_RuleFails()
    {
        const string source = """
            namespace Stub.DependencyInjection
            {
                // Minimal stub mirroring Microsoft.Extensions.DependencyInjection's
                // ServiceCollectionServiceExtensions — the predicate matches on the generic
                // method's name ("AddSingleton") and generic arguments, not its declaring type,
                // so a self-contained stub is sufficient (no real DI package reference needed).
                public interface IServiceCollection { }

                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService, TImplementation>(
                        this IServiceCollection services)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static IServiceCollection AddScoped<TService, TImplementation>(
                        this IServiceCollection services)
                        where TService : class
                        where TImplementation : class, TService
                        => services;
                }
            }

            namespace SharedKernel.Security.Abstractions.Abstractions
            {
                public interface IUserContext { }
            }

            namespace Fixture.Composition
            {
                public sealed class FixtureUserContext
                    : SharedKernel.Security.Abstractions.Abstractions.IUserContext { }

                public static class DiExtensions
                {
                    // Violation: registers IUserContext as a singleton — one instance would be
                    // captured and leaked across every subsequent request on the same process.
                    // Called via the static form (not extension-method dot-syntax) since this
                    // fixture source deliberately omits a "using Stub.DependencyInjection;"
                    // directive — the compiled IL shape (a Call to a closed GenericInstanceMethod
                    // named "AddSingleton") is identical either way, which is exactly what the
                    // predicate under test inspects.
                    public static Stub.DependencyInjection.IServiceCollection Register(
                        Stub.DependencyInjection.IServiceCollection services)
                    {
                        return Stub.DependencyInjection.ServiceCollectionExtensions.AddSingleton<
                            SharedKernel.Security.Abstractions.Abstractions.IUserContext,
                            FixtureUserContext>(services);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SingletonUserContextViolation", source);

        var result = SecurityArchitectureRules
            .NoSingletonRegistrationOfSecurityContextTypes(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "DiExtensions.Register() calls AddSingleton<IUserContext, FixtureUserContext>() — " +
                     "IUserContext must never be registered as singleton");
    }

    // ---------------------------------------------------------------------------
    // T-289 — Rule 2 pass path: AddScoped<IUserContext, FixtureUserContext>() registration
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-289: The equivalent fixture calling
    /// <c>services.AddScoped&lt;IUserContext, FixtureUserContext&gt;()</c> must pass
    /// <see cref="SecurityArchitectureRules.NoSingletonRegistrationOfSecurityContextTypes"/>.
    /// </summary>
    [Fact]
    public void NoSingletonRegistrationOfSecurityContextTypes_AddScopedUserContext_RulePasses()
    {
        const string source = """
            namespace Stub.DependencyInjection
            {
                public interface IServiceCollection { }

                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService, TImplementation>(
                        this IServiceCollection services)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static IServiceCollection AddScoped<TService, TImplementation>(
                        this IServiceCollection services)
                        where TService : class
                        where TImplementation : class, TService
                        => services;
                }
            }

            namespace SharedKernel.Security.Abstractions.Abstractions
            {
                public interface IUserContext { }
            }

            namespace Fixture.Composition
            {
                public sealed class FixtureUserContext
                    : SharedKernel.Security.Abstractions.Abstractions.IUserContext { }

                public static class DiExtensions
                {
                    // Compliant: registers IUserContext as Scoped — one instance per HTTP request.
                    public static Stub.DependencyInjection.IServiceCollection Register(
                        Stub.DependencyInjection.IServiceCollection services)
                    {
                        return Stub.DependencyInjection.ServiceCollectionExtensions.AddScoped<
                            SharedKernel.Security.Abstractions.Abstractions.IUserContext,
                            FixtureUserContext>(services);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ScopedUserContextCompliant", source);

        var result = SecurityArchitectureRules
            .NoSingletonRegistrationOfSecurityContextTypes(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "DiExtensions.Register() calls AddScoped<IUserContext, FixtureUserContext>() — " +
                     "AddScoped is the compliant registration lifetime");
    }

    // ---------------------------------------------------------------------------
    // T-290 — Real-assembly verification (non-gating): SharedKernel.Domain
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-290: Re-points <see cref="SecurityArchitectureRules.DomainNeverReferencesTenantProvider"/>
    /// at the real, shipped <c>SharedKernel.Domain</c> assembly and confirms zero violations.
    /// Non-gating — <c>03.Domain</c> was already fully Published (v1.7.0, WO-051) as of this
    /// phase's authoring.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesTenantProvider_RealDomainAssembly_RulePasses()
    {
        var domainAssembly = typeof(SharedKernel.Domain.Abstractions.IDomainService).Assembly;

        var result = SecurityArchitectureRules
            .DomainNeverReferencesTenantProvider(domainAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real, shipped SharedKernel.Domain assembly never references " +
                     "SharedKernel.Security.Abstractions.Abstractions.ITenantProvider — " +
                     "03.Domain has no dependency on 12.Security at all");
    }

    // ---------------------------------------------------------------------------
    // T-291 — Real-assembly verification (non-gating): SharedKernel.Security.Oidc
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-291: Re-points
    /// <see cref="SecurityArchitectureRules.NoSingletonRegistrationOfSecurityContextTypes"/> at
    /// the real, shipped <c>SharedKernel.Security.Oidc</c> assembly (its own
    /// <c>AddSharedKernelSecurity</c>/<c>AddAzureB2CAuthentication</c> registration methods) and
    /// confirms zero violations. Non-gating — <c>12.Security</c> was already fully Published as
    /// of this phase's authoring; both registration methods register
    /// <c>IUserContext</c>/<c>ITenantProvider</c> as <c>AddScoped</c>, never
    /// <c>AddSingleton</c>.
    /// </summary>
    [Fact]
    public void NoSingletonRegistrationOfSecurityContextTypes_RealOidcAssembly_RulePasses()
    {
        var oidcAssembly = typeof(SharedKernel.Security.Oidc.Extensions.SecurityServiceCollectionExtensions).Assembly;

        var result = SecurityArchitectureRules
            .NoSingletonRegistrationOfSecurityContextTypes(oidcAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real, shipped SecurityServiceCollectionExtensions registers both " +
                     "IUserContext and ITenantProvider via AddScoped — never AddSingleton");
    }

    // ---------------------------------------------------------------------------
    // T-292 — Rule 3 fire path: raw "DPoP" header-name literal outside SharedKernel.Security.Oidc
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-292: A fixture type outside <c>SharedKernel.Security.Oidc</c> containing a raw
    /// <c>"DPoP"</c> header-name Ldstr literal must fail
    /// <see cref="SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc"/>.
    /// </summary>
    [Fact]
    public void DpopProofValidationNeverDuplicatedOutsideOidc_RawDpopLiteral_RuleFails()
    {
        const string source = """
            namespace Fixture.Consumer
            {
                // Violation: reads a raw "DPoP" header-name literal outside
                // SharedKernel.Security.Oidc instead of calling into that package's own
                // proof-validation surface.
                public sealed class RawDpopReader
                {
                    public string ReadDpopHeader(System.Collections.Generic.IDictionary<string, string> headers)
                    {
                        return headers["DPoP"];
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DpopRawLiteralViolation", source);

        var result = SecurityArchitectureRules
            .DpopProofValidationNeverDuplicatedOutsideOidc(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RawDpopReader reads a raw \"DPoP\" header-name literal outside " +
                     "SharedKernel.Security.Oidc — DPoP proof-validation logic must live " +
                     "exclusively in that package");
    }

    // ---------------------------------------------------------------------------
    // T-293 — Rule 3 fire path: JwtSecurityTokenHandler reference outside SharedKernel.Security.Oidc
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-293: A fixture type outside <c>SharedKernel.Security.Oidc</c> that references
    /// <c>JwtSecurityTokenHandler</c> to parse a proof JWT must fail
    /// <see cref="SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc"/>.
    /// </summary>
    [Fact]
    public void DpopProofValidationNeverDuplicatedOutsideOidc_JwtHandlerReference_RuleFails()
    {
        const string source = """
            namespace System.IdentityModel.Tokens.Jwt
            {
                // Stub simulating the real JwtSecurityTokenHandler — same namespace and name so
                // the exact-FullName match fires correctly.
                public sealed class JwtSecurityTokenHandler
                {
                    public object ReadToken(string token) => new object();
                }
            }

            namespace Fixture.Consumer
            {
                // Violation: hand-parses the proof JWT itself outside SharedKernel.Security.Oidc
                // instead of calling into that package's own proof-validation surface.
                public sealed class RawProofParser
                {
                    public object Parse(string proofJwt)
                    {
                        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                        return handler.ReadToken(proofJwt);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DpopJwtHandlerViolation", source);

        var result = SecurityArchitectureRules
            .DpopProofValidationNeverDuplicatedOutsideOidc(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RawProofParser references JwtSecurityTokenHandler outside " +
                     "SharedKernel.Security.Oidc — proof-JWT parsing must live exclusively in " +
                     "that package");
    }

    // ---------------------------------------------------------------------------
    // T-294 — Rule 3 pass path (exemption): identical usage inside SharedKernel.Security.Oidc
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-294: The identical <c>"DPoP"</c>-literal/JWT-parsing-type usage inside a
    /// <c>SharedKernel.Security.Oidc</c>-namespaced fixture type must pass
    /// <see cref="SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc"/>.
    /// </summary>
    [Fact]
    public void DpopProofValidationNeverDuplicatedOutsideOidc_ExemptedOidcNamespace_RulePasses()
    {
        const string source = """
            namespace System.IdentityModel.Tokens.Jwt
            {
                public sealed class JwtSecurityTokenHandler
                {
                    public object ReadToken(string token) => new object();
                }
            }

            namespace SharedKernel.Security.Oidc.Dpop
            {
                // Compliant: this IS the real, shipped home for DPoP proof-validation logic —
                // both surfaces are legitimate here.
                public sealed class FixtureDpopValidator
                {
                    public object Validate(System.Collections.Generic.IDictionary<string, string> headers)
                    {
                        var raw = headers["DPoP"];
                        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                        return handler.ReadToken(raw);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DpopExemptedNamespaceCompliant", source);

        var result = SecurityArchitectureRules
            .DpopProofValidationNeverDuplicatedOutsideOidc(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "FixtureDpopValidator lives inside SharedKernel.Security.Oidc.Dpop — the " +
                     "sole legitimate home for DPoP proof-validation logic, exempted " +
                     "unconditionally");
    }

    // ---------------------------------------------------------------------------
    // T-295 — Rule 3 pass path (negative control): zero DPoP/JWT-parsing usage
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-295: A fixture type with zero DPoP/JWT-parsing usage must pass
    /// <see cref="SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc"/>.
    /// </summary>
    [Fact]
    public void DpopProofValidationNeverDuplicatedOutsideOidc_NoDpopOrJwtUsage_RulePasses()
    {
        const string source = """
            namespace Fixture.Consumer
            {
                public sealed class UnrelatedType
                {
                    public int Add(int a, int b) => a + b;
                }
            }
            """;

        var assembly = CompileInMemory("DpopNegativeControl", source);

        var result = SecurityArchitectureRules
            .DpopProofValidationNeverDuplicatedOutsideOidc(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "UnrelatedType contains no \"DPoP\" literal and no JWT-handler reference " +
                     "anywhere in the fixture assembly");
    }

    // ---------------------------------------------------------------------------
    // T-296 — Rule 4 fire path: ConnectionInfo.ClientCertificate getter outside
    // SharedKernel.Security.Mtls
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-296: A fixture type outside <c>SharedKernel.Security.Mtls</c> that calls
    /// <c>ConnectionInfo.ClientCertificate</c>'s getter directly must fail
    /// <see cref="SecurityArchitectureRules.ClientCertificateAccessNeverDuplicatedOutsideMtls"/>.
    /// </summary>
    [Fact]
    public void ClientCertificateAccessNeverDuplicatedOutsideMtls_RawGetterCall_RuleFails()
    {
        const string source = """
            namespace Microsoft.AspNetCore.Http
            {
                // Stub simulating the real ConnectionInfo — same namespace and name so the
                // exact-FullName/member-name match fires correctly.
                public sealed class ConnectionInfo
                {
                    public object ClientCertificate { get; set; }
                }
            }

            namespace Fixture.Consumer
            {
                // Violation: reads ConnectionInfo.ClientCertificate directly outside
                // SharedKernel.Security.Mtls instead of calling into that package's own
                // certificate-validation surface.
                public sealed class RawCertReader
                {
                    public object ReadCert(Microsoft.AspNetCore.Http.ConnectionInfo connection)
                    {
                        return connection.ClientCertificate;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ClientCertificateRawAccessViolation", source);

        var result = SecurityArchitectureRules
            .ClientCertificateAccessNeverDuplicatedOutsideMtls(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RawCertReader calls ConnectionInfo.ClientCertificate's getter directly " +
                     "outside SharedKernel.Security.Mtls — client-certificate trust/validation " +
                     "logic must live exclusively in that package");
    }

    // ---------------------------------------------------------------------------
    // T-297 — Rule 4 pass path (exemption): identical usage inside SharedKernel.Security.Mtls
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-297: The identical <c>ClientCertificate</c>-getter call inside a
    /// <c>SharedKernel.Security.Mtls</c>-namespaced fixture type must pass
    /// <see cref="SecurityArchitectureRules.ClientCertificateAccessNeverDuplicatedOutsideMtls"/>.
    /// </summary>
    [Fact]
    public void ClientCertificateAccessNeverDuplicatedOutsideMtls_ExemptedMtlsNamespace_RulePasses()
    {
        const string source = """
            namespace Microsoft.AspNetCore.Http
            {
                public sealed class ConnectionInfo
                {
                    public object ClientCertificate { get; set; }
                }
            }

            namespace SharedKernel.Security.Mtls.Validation
            {
                // Compliant: this IS the real, shipped home for client-certificate
                // trust/validation logic.
                public sealed class FixtureMtlsHandler
                {
                    public object ReadCert(Microsoft.AspNetCore.Http.ConnectionInfo connection)
                    {
                        return connection.ClientCertificate;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ClientCertificateExemptedNamespaceCompliant", source);

        var result = SecurityArchitectureRules
            .ClientCertificateAccessNeverDuplicatedOutsideMtls(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "FixtureMtlsHandler lives inside SharedKernel.Security.Mtls.Validation — " +
                     "the sole legitimate home for client-certificate trust/validation logic, " +
                     "exempted unconditionally");
    }

    // ---------------------------------------------------------------------------
    // T-298 — Rule 4 pass path (negative control): zero ClientCertificate access
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-298: A fixture type with zero <c>ClientCertificate</c> access must pass
    /// <see cref="SecurityArchitectureRules.ClientCertificateAccessNeverDuplicatedOutsideMtls"/>.
    /// </summary>
    [Fact]
    public void ClientCertificateAccessNeverDuplicatedOutsideMtls_NoClientCertificateAccess_RulePasses()
    {
        const string source = """
            namespace Fixture.Consumer
            {
                public sealed class UnrelatedType
                {
                    public int Add(int a, int b) => a + b;
                }
            }
            """;

        var assembly = CompileInMemory("ClientCertificateNegativeControl", source);

        var result = SecurityArchitectureRules
            .ClientCertificateAccessNeverDuplicatedOutsideMtls(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "UnrelatedType contains no ConnectionInfo.ClientCertificate getter call " +
                     "anywhere in the fixture assembly");
    }

    // ---------------------------------------------------------------------------
    // T-299 — Real-assembly verification: SharedKernel.Security.Oidc's real DPoP surface
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-299: Re-points
    /// <see cref="SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc"/> at
    /// the real, shipped <c>SharedKernel.Security.Oidc</c> assembly (which contains the real
    /// <c>DpopProofValidator</c>, itself referencing both detection surfaces) and confirms zero
    /// violations outside that package's own namespace.
    /// </summary>
    /// <remarks>
    /// Originally tracked as a Cross-Domain Dependency pending <c>12.Security</c> P-376 — CONFIRMED
    /// RESOLVED on disk before this phase's implementation session:
    /// <c>SharedKernel.Security.Oidc</c> is packed at <c>4.0.0</c> and ships a real
    /// <c>Dpop/DpopProofValidator.cs</c> implementing the full RFC 9449 algorithm. Since this test
    /// scans the ENTIRE assembly (including <c>DpopProofValidator</c> itself, which legitimately
    /// contains both the <c>"DPoP"</c> literal and a <c>JwtSecurityTokenHandler</c>/
    /// <c>JsonWebTokenHandler</c> reference), a pass here proves the namespace exemption is
    /// correctly scoped — not merely that the scan never reached the real implementation.
    /// </remarks>
    [Fact]
    public void DpopProofValidationNeverDuplicatedOutsideOidc_RealOidcAssembly_RulePasses()
    {
        var oidcAssembly = typeof(SharedKernel.Security.Oidc.Extensions.SecurityServiceCollectionExtensions).Assembly;

        var result = SecurityArchitectureRules
            .DpopProofValidationNeverDuplicatedOutsideOidc(oidcAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real, shipped SharedKernel.Security.Oidc assembly's DPoP surface " +
                     "(DpopProofValidator and its collaborators) lives entirely inside the " +
                     "exempted SharedKernel.Security.Oidc namespace — no other type in the " +
                     "assembly duplicates the \"DPoP\" literal or JWT-handler references");
    }

    // ---------------------------------------------------------------------------
    // T-300 — Real-assembly verification: the real, shipped SharedKernel.Security.Mtls assembly
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-300: Re-points
    /// <see cref="SecurityArchitectureRules.ClientCertificateAccessNeverDuplicatedOutsideMtls"/>
    /// at the real, shipped <c>SharedKernel.Security.Mtls</c> assembly and confirms zero
    /// violations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Originally tracked as a Cross-Domain Dependency pending <c>12.Security</c> P-377 —
    /// CONFIRMED RESOLVED on disk before this phase's implementation session:
    /// <c>SharedKernel.Security.Mtls</c> is packed at <c>2.0.0</c> and ships a real
    /// <c>Validation/MtlsAuthenticationHandler.cs</c>.
    /// </para>
    /// <para>
    /// <strong>Verified this is a genuine pass, not a vacuous one — the scan reaches real code.</strong>
    /// Read the shipped source directly (not assumed from its name): the real handler reads
    /// <c>context.ClientCertificate</c> where <c>context</c> is
    /// <c>Microsoft.AspNetCore.Authentication.Certificate.CertificateValidatedContext</c> — a
    /// DIFFERENT declaring type than <c>Microsoft.AspNetCore.Http.ConnectionInfo</c>, which the
    /// predicate deliberately targets. The ASP.NET Core certificate-authentication middleware
    /// itself resolves the certificate from <c>ConnectionInfo.ClientCertificate</c> before ever
    /// invoking this package's event handler, so no type in this assembly needs to (or does)
    /// call that exact getter. This is a real, non-vacuous pass: Mono.Cecil walks every method
    /// body in the assembly (proven by T-296's contrived fixture using the identical detection
    /// technique against a type that DOES call the getter), and the real code simply never
    /// performs that specific IL shape — not because the namespace exemption silently swallowed
    /// a violation, but because the offending surface genuinely does not occur here.
    /// </para>
    /// </remarks>
    [Fact]
    public void ClientCertificateAccessNeverDuplicatedOutsideMtls_RealMtlsAssembly_RulePasses()
    {
        var mtlsAssembly = typeof(SharedKernel.Security.Mtls.Extensions.MtlsServiceCollectionExtensions).Assembly;

        var result = SecurityArchitectureRules
            .ClientCertificateAccessNeverDuplicatedOutsideMtls(mtlsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real, shipped SharedKernel.Security.Mtls assembly never calls " +
                     "ConnectionInfo.ClientCertificate's getter directly — the certificate " +
                     "arrives already resolved via CertificateValidatedContext.ClientCertificate, " +
                     "a different declaring type the predicate does not match");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var refList = new System.Collections.Generic.List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: refList,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using (var fs = System.IO.File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(fs);
            if (!emitResult.Success)
            {
                var errors = string.Join(
                    System.Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"Fixture '{assemblyName}' failed to compile:{System.Environment.NewLine}{errors}");
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}
