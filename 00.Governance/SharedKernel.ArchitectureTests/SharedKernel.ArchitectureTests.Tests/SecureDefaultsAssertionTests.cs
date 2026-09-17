using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="SecureDefaultsAssertion"/> — WO-060 P-390 (<c>SK.00.SecureDefaultsLock</c>).
/// </summary>
/// <remarks>
/// T-301/T-302: <see cref="SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals"/> against a
/// contrived <c>AllowedCertificateTypes</c>-shaped fixture property — pass path (<c>"Chained"</c>)
/// and fire path (<c>"All"</c>, reproducing the real, historical WO-058 shipped defect shape).
/// T-303/T-304: the same method against a contrived <c>RevocationMode</c>-shaped fixture property —
/// pass path (<c>"Offline"</c>) and fire path (<c>"NoCheck"</c>, reproducing the real, historical
/// WO-058 shipped defect shape).
/// T-305–T-308: <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes"/>
/// against a contrived algorithm-allowlist-shaped fixture property — pass path
/// (<c>["PS256", "ES256"]</c>), fire path (empty array AND uninitialized/<see langword="null"/>
/// default — both fail via the same non-empty check), fire path (<c>"none"</c> included), fire
/// path (a symmetric algorithm, <c>"HS256"</c>, included).
/// T-309/T-310: real-assembly, GATING verification against the real, shipped
/// <c>SharedKernel.Security.Mtls</c>/<c>.Oidc</c> hardened defaults. Originally authored as
/// non-gating and tracked as a Cross-Domain Dependency pending <c>12.Security</c> P-386/P-387 — CONFIRMED
/// RESOLVED on disk before this phase's implementation session: <c>12.Security</c> shipped its
/// full WO-060 scope (C-39/C-40/C-41) earlier in the same overall session, so these are wired
/// directly here as GATING tests rather than deferred, mirroring the
/// <c>SK.00.SenderConstrainedCredentialGuard</c> precedent from the immediately-preceding phase.
/// <para>
/// T-311–T-318 (<c>SK.00.TenantAndMtlsBoundaryLock</c>/WO-061/P-401):
/// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals"/> and
/// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> against contrived
/// fixtures (T-311–T-316) and the real, shipped <c>SharedKernel.MultiTenancy</c>/
/// <c>.ServiceDefaults</c> assemblies (T-317/T-318, GATING — originally authored as non-gating
/// and tracked as a Cross-Domain Dependency pending <c>13.ServiceDefaults</c> P-393/P-394/P-395,
/// CONFIRMED RESOLVED on disk before this phase's implementation session: that domain shipped its
/// full WO-061 scope, 171/171 + 51/51 tests green, before this session began).
/// </para>
/// <para>
/// T-326/T-327 (<c>SK.00.CorsWildcardCredentialsGuard</c>/WO-062/P-410):
/// <see cref="SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType"/> against contrived
/// pass/fail-path fixtures. T-328: the real-assembly re-verification originally scoped to re-point
/// <see cref="SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType"/> at
/// <c>SharedKernel.Presentation.WebApi</c>'s real <c>AddSharedKernelCors</c> instead re-points the
/// EXISTING <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> at
/// <c>CorsPolicyOptionsValidator.Validate</c> — the real, shipped guard is validator-based
/// (<c>IValidateOptions&lt;CorsPolicyOptions&gt;</c> returning <c>ValidateOptionsResult.Fail</c>),
/// never a direct <c>throw</c> inside this assembly's own IL; see that test's own remarks for the
/// full design/reality-mismatch reasoning.
/// </para>
/// <para>
/// T-329/T-330 (<c>SK.00.CorrelationIdValidationGuard</c>/WO-063/P-415):
/// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> against contrived pass/
/// fail-path fixtures shaped after <c>CorrelationIdMiddleware.ResolveCorrelationId</c>'s real
/// check-then-substitute pattern. T-331: real-assembly, GATING verification re-points the same,
/// already-shipped technique at the real <c>CorrelationIdMiddleware.ResolveCorrelationId</c> and
/// its real private <c>IsValidFormat</c> callee — the first phase in this "lock a not-yet-shipped
/// hardened guard" family to require zero new production code in
/// <see cref="SecureDefaultsAssertion"/>, since the real shipped shape matched this phase's design
/// exactly (a conditional invocation, never a throw), unlike
/// <c>SK.00.CorsWildcardCredentialsGuard</c>'s validator-vs-throw mismatch.
/// </para>
/// <para>
/// T-332–T-336 (<c>SK.00.WebhookSsrfGuardLock</c>/WO-064/P-432): the first phase in this family to
/// lock TWO independent facts with two different techniques. Technique A —
/// <see cref="SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton"/> — against contrived
/// pass/fire/boundary-path fixtures (T-332–T-334) and the real, shipped
/// <c>SharedKernel.Integration.Webhooks.Extensions.ServiceCollectionExtensions.AddSharedKernelWebhooks</c>
/// (T-335, GATING — originally authored as non-gating and tracked as a Cross-Domain Dependency
/// pending <c>15.Integration</c> P-422/H-06/H-07, CONFIRMED RESOLVED on disk before this phase's
/// implementation session: that domain had already shipped
/// <c>IWebhookUrlValidator</c>/<c>PrivateNetworkWebhookUrlValidator</c> and their default
/// <c>TryAddSingleton</c> registration past Design into Core). Technique B — a genuinely EXECUTED
/// test (T-336, GATING, same resolved dependency) invoking the real, compiled
/// <c>PrivateNetworkWebhookUrlValidator</c> against a fixed IP-literal table — the first executed
/// (not IL-only) real-assembly test in this file, per that method's own remarks explaining why no
/// static technique can honestly prove range-membership behavior.
/// </para>
/// <para>
/// T-337–T-340 (<c>SK.00.CacheEncryptionAndRedisValidationLock</c>/WO-065/P-437): the second phase
/// in this family to lock TWO independent facts with two different techniques (after
/// <c>SK.00.WebhookSsrfGuardLock</c>). Technique A — T-337, a genuinely EXECUTED real-assembly
/// test (the SECOND in this file, after <c>SK.00.WebhookSsrfGuardLock</c>'s T-336) building the
/// real, shipped <c>ICachingBuilder.AddBrotliCompression()</c> + <c>.AddCacheEncryption()</c>
/// composed pipeline (<c>SharedKernel.Caching.FusionCache</c>), serializing a highly-compressible
/// payload through it, and asserting the stored-byte output is meaningfully smaller than an
/// encrypt-only baseline built directly via <c>ISymmetricEncryptionService</c> — proving compression
/// genuinely ran before encryption, plus a round-trip-correctness precondition. Lives directly in
/// this file rather than as a reusable <see cref="SecureDefaultsAssertion"/> method, per that
/// class's own Technique 5/T-336 precedent for a computed-behavior check no static IL technique can
/// honestly prove. Technique B — T-338/T-339 (contrived pass/fire-path fixtures) and T-340 (GATING,
/// real-assembly) — reuses the EXISTING <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>
/// unchanged, re-pointed at the real, shipped <c>RedisConnectionCoreExtensions.AddRedisConnection</c>
/// (<c>SharedKernel.Caching.Redis.Core</c>), asserting a call to
/// <c>Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions.ValidateOnStart</c> —
/// CORRECTED at implementation time from this phase's own authoring-time Rule 6, which named
/// <c>Microsoft.Extensions.Options.OptionsBuilderExtensions</c> instead (the real extension method
/// lives in the <c>Microsoft.Extensions.Options</c> NuGet package but under the
/// <c>Microsoft.Extensions.DependencyInjection</c> namespace). Both <c>02.Caching</c> Cross-Domain
/// Dependencies (P-433/P-436) were CONFIRMED RESOLVED on disk before this phase's implementation
/// session — that domain had already shipped its full Phase 42/Phase 45 scope (WO-065) past Design
/// into Core, mirroring this file's own now-nine-times-repeated dependency-resolved-before-
/// implementation pattern. The real <c>AddRedisConnection</c> call site
/// (<c>services.AddOptions&lt;RedisConnectionOptions&gt;().Configure(...).ValidateDataAnnotations().ValidateOnStart()</c>)
/// lives directly in its own IL body, not inside a lambda closure, so no closure-scanning extension
/// is exercised by this particular call site.
/// </para>
/// <para>
/// <strong>T-337 RE-LOCKED 2026-09-08</strong> against <c>02.Caching</c>'s
/// <c>SK.02.CacheEncryptionAadBinding</c> phase (WO-081), which shipped the same day and deleted
/// <c>CacheEncryptionSerializer</c> entirely — <c>AddCacheEncryption()</c> no longer decorates
/// <c>IFusionCacheSerializer</c> (which never receives the cache key, so it cannot derive key-bound
/// AAD); it now wraps <c>ICacheService</c> with a new <c>Encryption.EncryptedCacheService</c>
/// instead. T-337's original design, still asserting against <c>IFusionCacheSerializer.Serialize</c>
/// output, had gone VACUOUS: after the <c>02.Caching</c> change that serializer performs compression
/// only (encryption moved one layer up), so the size-ratio assertion kept passing for the wrong
/// reason — a compressed-only payload is still trivially smaller than an encrypt-only baseline. This
/// was found during a routine full-solution build, not by this file's own suite, which stayed green
/// throughout. T-337 is re-pointed at the real, current architecture — see its own remarks for the
/// full detail — rather than merely patched to compile against the old one.
/// </para>
/// <para>
/// <strong>Cryptography re-locked against the P-545 redesign.</strong> The redesigned
/// <c>SharedKernel.Cryptography</c> removed the runtime synchronous-provider gates
/// (<c>EncryptionKeyProviderCapabilities</c>/<c>AsymmetricKeyProviderCapabilities</c>, the constructor
/// capability checks and the <see cref="NotSupportedException"/> guards on
/// <c>AesGcmEncryptionService</c>/<c>RsaSignatureService</c>/<c>EcdsaSignatureService</c>), so the two
/// real-assembly tests that located those call sites (T-362/T-363) are gone. The secure defaults they
/// protected, and the ones the redesign added, are locked by four real-assembly tests instead: synchronous
/// encryption requires an <c>ISynchronousEncryptionKeyProvider</c> by constructor type; RSA signing keys
/// below 2048 bits are rejected; HMAC keys below 32 bytes are rejected; and stored PBKDF2/Argon2id hash
/// costs are bounded before any derivation. T-360/T-361's contrived fixtures remain, re-themed, as the
/// file's coverage of <c>.ctor</c> resolution.
/// </para>
/// </remarks>
public class SecureDefaultsAssertionTests
{
    // ---------------------------------------------------------------------------
    // T-301 — AssertEnumPropertyDefaultEquals pass path: AllowedCertificateTypes-shaped, "Chained"
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-301: A fixture options type whose default-constructed <c>AllowedCertificateTypes</c>-
    /// shaped property equals <c>"Chained"</c> must pass
    /// <see cref="SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals"/>.
    /// </summary>
    [Fact]
    public void AssertEnumPropertyDefaultEquals_CertificateTypesChained_DoesNotThrow()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                public enum FixtureCertificateTypes
                {
                    None = 0,
                    SelfSigned = 1,
                    Chained = 2,
                    All = 3
                }

                // Compliant: mirrors the corrected WO-060/P-386 hardened default.
                public sealed class FixtureMtlsOptionsSecureCertificateTypes
                {
                    public FixtureCertificateTypes AllowedCertificateTypes { get; set; } =
                        FixtureCertificateTypes.Chained;
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsCertTypesPass", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureMtlsOptionsSecureCertificateTypes")!;

        var act = () =>
            SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals(
                optionsType, "AllowedCertificateTypes", "Chained");

        act.Should().NotThrow(
            because: "the fixture's default-constructed AllowedCertificateTypes property " +
                     "resolves to Chained, matching the expected hardened default");
    }

    // ---------------------------------------------------------------------------
    // T-302 — AssertEnumPropertyDefaultEquals fire path: AllowedCertificateTypes-shaped, "All"
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-302: A fixture options type whose default-constructed <c>AllowedCertificateTypes</c>-
    /// shaped property equals <c>"All"</c> — reproducing the real, historical WO-058 shipped
    /// defect shape — must fail
    /// <see cref="SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals"/>.
    /// </summary>
    [Fact]
    public void AssertEnumPropertyDefaultEquals_CertificateTypesAll_ThrowsNamingActualAndExpected()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                public enum FixtureCertificateTypes
                {
                    None = 0,
                    SelfSigned = 1,
                    Chained = 2,
                    All = 3
                }

                // Violation: reproduces the real, historical WO-058 shipped defect — a
                // self-signed certificate would be accepted before IMtlsCertificateValidator
                // ever runs.
                public sealed class FixtureMtlsOptionsWeakCertificateTypes
                {
                    public FixtureCertificateTypes AllowedCertificateTypes { get; set; } =
                        FixtureCertificateTypes.All;
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsCertTypesFail", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureMtlsOptionsWeakCertificateTypes")!;

        var act = () =>
            SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals(
                optionsType, "AllowedCertificateTypes", "Chained");

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's default-constructed AllowedCertificateTypes property " +
                         "resolves to All, the exact WEAKER-than-framework-default shape the " +
                         "real WO-058 defect shipped")
            .WithMessage("*All*")
            .WithMessage("*Chained*");
    }

    // ---------------------------------------------------------------------------
    // T-303 — AssertEnumPropertyDefaultEquals pass path: RevocationMode-shaped, "Offline"
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-303: A fixture options type whose default-constructed <c>RevocationMode</c>-shaped
    /// property equals <c>"Offline"</c> must pass
    /// <see cref="SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals"/>.
    /// </summary>
    [Fact]
    public void AssertEnumPropertyDefaultEquals_RevocationModeOffline_DoesNotThrow()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                public enum FixtureRevocationMode
                {
                    NoCheck = 0,
                    Online = 1,
                    Offline = 2
                }

                // Compliant: mirrors the corrected WO-060/P-386 hardened default.
                public sealed class FixtureMtlsOptionsSecureRevocationMode
                {
                    public FixtureRevocationMode RevocationMode { get; set; } =
                        FixtureRevocationMode.Offline;
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsRevocationModePass", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureMtlsOptionsSecureRevocationMode")!;

        var act = () =>
            SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals(
                optionsType, "RevocationMode", "Offline");

        act.Should().NotThrow(
            because: "the fixture's default-constructed RevocationMode property resolves to " +
                     "Offline, matching the expected hardened default");
    }

    // ---------------------------------------------------------------------------
    // T-304 — AssertEnumPropertyDefaultEquals fire path: RevocationMode-shaped, "NoCheck"
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-304: A fixture options type whose default-constructed <c>RevocationMode</c>-shaped
    /// property equals <c>"NoCheck"</c> — reproducing the real, historical WO-058 shipped defect
    /// shape — must fail <see cref="SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals"/>.
    /// </summary>
    [Fact]
    public void AssertEnumPropertyDefaultEquals_RevocationModeNoCheck_ThrowsNamingActualAndExpected()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                public enum FixtureRevocationMode
                {
                    NoCheck = 0,
                    Online = 1,
                    Offline = 2
                }

                // Violation: reproduces the real, historical WO-058 shipped defect — no
                // revocation checking of any kind is performed by default.
                public sealed class FixtureMtlsOptionsWeakRevocationMode
                {
                    public FixtureRevocationMode RevocationMode { get; set; } =
                        FixtureRevocationMode.NoCheck;
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsRevocationModeFail", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureMtlsOptionsWeakRevocationMode")!;

        var act = () =>
            SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals(
                optionsType, "RevocationMode", "Offline");

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's default-constructed RevocationMode property resolves " +
                         "to NoCheck, the exact shape the real WO-058 defect shipped")
            .WithMessage("*NoCheck*")
            .WithMessage("*Offline*");
    }

    // ---------------------------------------------------------------------------
    // T-305 — AssertStringCollectionPropertyDefaultExcludes pass path: ["PS256", "ES256"]
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-305: A fixture options type whose default-constructed algorithm-allowlist property is
    /// <c>["PS256", "ES256"]</c> must pass
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes"/>.
    /// </summary>
    [Fact]
    public void AssertStringCollectionPropertyDefaultExcludes_Ps256Es256_DoesNotThrow()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Compliant: mirrors the real WO-060/P-387 FAPI 2.0 baseline allowlist.
                public sealed class FixtureAlgorithmOptionsSecure
                {
                    public System.Collections.Generic.IReadOnlyCollection<string> ValidAlgorithms
                        { get; set; } = ["PS256", "ES256"];
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsAlgorithmsPass", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureAlgorithmOptionsSecure")!;

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes(
                optionsType, "ValidAlgorithms", forbiddenValues: ["none", "HS256", "HS384", "HS512"]);

        act.Should().NotThrow(
            because: "the fixture's default-constructed ValidAlgorithms collects [\"PS256\", " +
                     "\"ES256\"] — neither forbidden value is present, and the collection is " +
                     "non-empty");
    }

    // ---------------------------------------------------------------------------
    // T-306 — AssertStringCollectionPropertyDefaultExcludes fire path: empty array AND null default
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-306 (case 1): A fixture options type whose default-constructed algorithm-allowlist
    /// property is an explicit empty array must fail
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes"/> via the
    /// non-empty check.
    /// </summary>
    [Fact]
    public void AssertStringCollectionPropertyDefaultExcludes_EmptyArrayDefault_ThrowsNamingEmptyCollection()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Violation: an empty allowlist enforces no algorithm restriction at all.
                public sealed class FixtureAlgorithmOptionsEmpty
                {
                    public System.Collections.Generic.IReadOnlyCollection<string> ValidAlgorithms
                        { get; set; } = System.Array.Empty<string>();
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsAlgorithmsEmpty", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureAlgorithmOptionsEmpty")!;

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes(
                optionsType, "ValidAlgorithms", forbiddenValues: ["none", "HS256", "HS384", "HS512"]);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's default-constructed ValidAlgorithms resolves to an " +
                         "explicit empty array — no Ldstr literals are collected, tripping the " +
                         "non-empty check")
            .WithMessage("*empty or null*");
    }

    /// <summary>
    /// T-306 (case 2): A fixture options type whose algorithm-allowlist property carries no
    /// default initializer at all (an uninitialized/<see langword="null"/> default) must fail
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes"/> via the
    /// SAME non-empty check as the explicit-empty-array case — arguably the more dangerous shape,
    /// since it may mean "no allowlist enforced at all."
    /// </summary>
    [Fact]
    public void AssertStringCollectionPropertyDefaultExcludes_NullDefault_ThrowsNamingEmptyCollection()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Violation: no property initializer at all — the constructor emits no Stfld
                // for this backing field, and the CLR zero-initializes it to null.
                public sealed class FixtureAlgorithmOptionsNull
                {
                    public System.Collections.Generic.IReadOnlyCollection<string> ValidAlgorithms
                        { get; set; }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsAlgorithmsNull", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureAlgorithmOptionsNull")!;

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes(
                optionsType, "ValidAlgorithms", forbiddenValues: ["none", "HS256", "HS384", "HS512"]);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's ValidAlgorithms property has no default initializer at " +
                         "all — no Stfld targeting its backing field exists in the constructor, " +
                         "so zero literals are collected, tripping the SAME non-empty check as " +
                         "an explicit empty array")
            .WithMessage("*empty or null*");
    }

    // ---------------------------------------------------------------------------
    // T-307 — AssertStringCollectionPropertyDefaultExcludes fire path: includes "none"
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-307: A fixture options type whose default-constructed algorithm-allowlist property
    /// includes <c>"none"</c> must fail
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes"/>.
    /// </summary>
    [Fact]
    public void AssertStringCollectionPropertyDefaultExcludes_IncludesNone_ThrowsNamingForbiddenValue()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Violation: "none" is the RFC 7518 alg-confusion/downgrade attack value — an
                // allowlist that admits it accepts an unsigned token.
                public sealed class FixtureAlgorithmOptionsNone
                {
                    public System.Collections.Generic.IReadOnlyCollection<string> ValidAlgorithms
                        { get; set; } = ["PS256", "none"];
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsAlgorithmsNone", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureAlgorithmOptionsNone")!;

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes(
                optionsType, "ValidAlgorithms", forbiddenValues: ["none", "HS256", "HS384", "HS512"]);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's default-constructed ValidAlgorithms collects " +
                         "[\"PS256\", \"none\"] — \"none\" is a forbidden value")
            .WithMessage("*none*");
    }

    // ---------------------------------------------------------------------------
    // T-308 — AssertStringCollectionPropertyDefaultExcludes fire path: includes "HS256"
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-308: A fixture options type whose default-constructed algorithm-allowlist property
    /// includes a symmetric algorithm (<c>"HS256"</c>) must fail
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes"/>.
    /// </summary>
    [Fact]
    public void AssertStringCollectionPropertyDefaultExcludes_IncludesSymmetricAlgorithm_ThrowsNamingForbiddenValue()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Violation: a symmetric (HMAC) algorithm in an asymmetric-signing-only default
                // allowlist opens an algorithm-confusion attack surface.
                public sealed class FixtureAlgorithmOptionsSymmetric
                {
                    public System.Collections.Generic.IReadOnlyCollection<string> ValidAlgorithms
                        { get; set; } = ["PS256", "HS256"];
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsAlgorithmsSymmetric", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureAlgorithmOptionsSymmetric")!;

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes(
                optionsType, "ValidAlgorithms", forbiddenValues: ["none", "HS256", "HS384", "HS512"]);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's default-constructed ValidAlgorithms collects " +
                         "[\"PS256\", \"HS256\"] — \"HS256\" is a forbidden symmetric algorithm")
            .WithMessage("*HS256*");
    }

    // ---------------------------------------------------------------------------
    // T-309 — Real-assembly verification (GATING): SharedKernel.Security.Mtls
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-309: Re-points <see cref="SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals"/> at
    /// the real, shipped <c>MtlsAuthenticationOptions</c> type for both
    /// <c>AllowedCertificateTypes</c> (expected <c>"Chained"</c>) and <c>RevocationMode</c>
    /// (expected <c>"Online"</c>, ASP.NET Core's own default) and confirms both hardened defaults hold.
    /// </summary>
    /// <remarks>
    /// Originally tracked as a Cross-Domain Dependency pending <c>12.Security</c> P-386 — CONFIRMED
    /// RESOLVED on disk before this phase's implementation session:
    /// <c>SharedKernel.Security.Mtls</c> is packed at <c>2.0.0</c> and ships C-39's corrected
    /// defaults. Non-vacuous: a deliberately-wrong expectation (<c>"All"</c>/<c>"NoCheck"</c>)
    /// against this exact real type is proven to fail by T-302/T-304's contrived fixtures using
    /// the identical detection technique — this test proves the real type's actual resolved
    /// default, not merely that the scan runs without error.
    /// </remarks>
    [Fact]
    public void AssertEnumPropertyDefaultEquals_RealMtlsAuthenticationOptions_HardenedDefaultsHold()
    {
        var optionsType = typeof(SharedKernel.Security.Mtls.Options.MtlsAuthenticationOptions);

        var allowedCertificateTypesAct = () =>
            SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals(
                optionsType, "AllowedCertificateTypes", "Chained");

        var revocationModeAct = () =>
            SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals(
                optionsType, "RevocationMode", "Online");

        allowedCertificateTypesAct.Should().NotThrow(
            because: "the real, shipped MtlsAuthenticationOptions.AllowedCertificateTypes " +
                     "defaults to CertificateTypes.Chained (WO-060, C-39)");
        revocationModeAct.Should().NotThrow(
            because: "the real, shipped MtlsAuthenticationOptions.RevocationMode defaults to " +
                     "X509RevocationMode.Online, never weaker than ASP.NET Core's certificate authentication");
    }

    // ---------------------------------------------------------------------------
    // T-310 — Real-assembly verification (GATING): SharedKernel.Security.Oidc
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-310: The real, shipped <c>AddOidcAuthentication</c> never accepts <c>none</c> or an HMAC algorithm, either
    /// by default or when configuration asks for one.
    /// </summary>
    /// <remarks>
    /// The algorithm lists default to empty (configuration binding appends to a non-empty default list), so the
    /// defaults are asserted on the configured <c>JwtBearerOptions</c> rather than by scanning a property
    /// initializer.
    /// </remarks>
    [Fact]
    public void AddOidcAuthentication_RealAssembly_RejectsSymmetricAndNoneAlgorithms()
    {
        string[] forbiddenValues = ["none", "HS256", "HS384", "HS512"];

        using ServiceProvider defaults = BuildOidc([]);
        var parameters = defaults
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(SharedKernel.Security.Oidc.OidcAuthenticationDefaults.AuthenticationScheme)
            .TokenValidationParameters;

        parameters.ValidAlgorithms.Should().NotBeNullOrEmpty();
        parameters.ValidAlgorithms.Should().NotContain(forbiddenValues);

        foreach (string forbidden in forbiddenValues)
        {
            using ServiceProvider configured = BuildOidc(new Dictionary<string, string?> { ["SharedKernel:Security:Oidc:ValidAlgorithms:0"] = forbidden });
            var read = () => configured
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<SharedKernel.Security.Oidc.Options.OidcAuthenticationOptions>>()
                .Value;

            read.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>(
                because: $"'{forbidden}' is not an asymmetric JWS algorithm and must fail startup validation");
        }
    }

    private static ServiceProvider BuildOidc(Dictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["SharedKernel:Security:Oidc:Authority"] = "https://issuer.example.test",
            ["SharedKernel:Security:Oidc:Audiences:0"] = "api://orders",
        };

        foreach ((string key, string? value) in overrides)
        {
            settings[key] = value;
        }

        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        SharedKernel.Security.Oidc.Extensions.OidcServiceCollectionExtensions.AddOidcAuthentication(services, configuration);
        return services.BuildServiceProvider();
    }

    // ---------------------------------------------------------------------------
    // T-311 — AssertStringCollectionPropertyDefaultEquals pass path: exact order match
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-311: A fixture options type whose default-constructed <c>StrategyOrder</c>-shaped
    /// property is exactly <c>["Claim", "Header", "Database"]</c> must pass
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals"/>.
    /// </summary>
    [Fact]
    public void AssertStringCollectionPropertyDefaultEquals_ClaimHeaderDatabase_DoesNotThrow()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Compliant: mirrors the corrected WO-061/P-393 hardened default order.
                public sealed class FixtureTenantResolutionOptionsSecureOrder
                {
                    public System.Collections.Generic.IReadOnlyList<string> StrategyOrder
                        { get; set; } = ["Claim", "Header", "Database"];
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsStrategyOrderPass", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureTenantResolutionOptionsSecureOrder")!;

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals(
                optionsType, "StrategyOrder", ["Claim", "Header", "Database"]);

        act.Should().NotThrow(
            because: "the fixture's default-constructed StrategyOrder resolves to exactly " +
                     "[\"Claim\", \"Header\", \"Database\"], matching the expected sequence " +
                     "element-for-element and position-for-position");
    }

    // ---------------------------------------------------------------------------
    // T-312 — AssertStringCollectionPropertyDefaultEquals fire path: wrong order (real historical shape)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-312: A fixture options type whose default-constructed <c>StrategyOrder</c>-shaped
    /// property is <c>["Header", "Claim", "Database"]</c> — reproducing the real, historical
    /// pre-P-393 shipped default order — must fail
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals"/>.
    /// </summary>
    [Fact]
    public void AssertStringCollectionPropertyDefaultEquals_HeaderClaimDatabase_ThrowsNamingActualAndExpected()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Violation: reproduces the real, historical pre-P-393 shipped default order — an
                // unsigned, caller-supplied X-Tenant-Id header outranks a cryptographically
                // verified JWT tenant claim for the same request.
                public sealed class FixtureTenantResolutionOptionsWeakOrder
                {
                    public System.Collections.Generic.IReadOnlyList<string> StrategyOrder
                        { get; set; } = ["Header", "Claim", "Database"];
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsStrategyOrderFail", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureTenantResolutionOptionsWeakOrder")!;

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals(
                optionsType, "StrategyOrder", ["Claim", "Header", "Database"]);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's default-constructed StrategyOrder resolves to " +
                         "[\"Header\", \"Claim\", \"Database\"], the exact WEAKER-than-corrected " +
                         "shape the real pre-P-393 default shipped")
            .WithMessage("*position 0*")
            .WithMessage("*Header*")
            .WithMessage("*Claim*");
    }

    // ---------------------------------------------------------------------------
    // T-313 — AssertStringCollectionPropertyDefaultEquals fire path: missing entry (length mismatch)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-313: A fixture whose default order is missing an entry present in the expected sequence
    /// must fail <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals"/>
    /// on the length mismatch, not silently succeed as a truncation-tolerant prefix match.
    /// </summary>
    [Fact]
    public void AssertStringCollectionPropertyDefaultEquals_MissingEntry_ThrowsNamingLengthMismatch()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Violation: missing "Database" — must fail on length, never silently pass as a
                // truncation-tolerant prefix match.
                public sealed class FixtureTenantResolutionOptionsMissingEntry
                {
                    public System.Collections.Generic.IReadOnlyList<string> StrategyOrder
                        { get; set; } = ["Claim", "Header"];
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsStrategyOrderMissing", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureTenantResolutionOptionsMissingEntry")!;

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals(
                optionsType, "StrategyOrder", ["Claim", "Header", "Database"]);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's default-constructed StrategyOrder resolves to only 2 " +
                         "elements, one fewer than the 3-element expected sequence")
            .WithMessage("*2-element*")
            .WithMessage("*3-element*");
    }

    // ---------------------------------------------------------------------------
    // T-314 — AssertStringCollectionPropertyDefaultEquals fire path: extra entry (length mismatch)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-314: A fixture whose default order contains every expected element but with one extra,
    /// unexpected entry appended must fail
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals"/> on the
    /// length mismatch.
    /// </summary>
    [Fact]
    public void AssertStringCollectionPropertyDefaultEquals_ExtraEntry_ThrowsNamingLengthMismatch()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Violation: an extra, unexpected "Gateway" entry appended — must fail on length,
                // never silently pass because every expected element is present somewhere.
                public sealed class FixtureTenantResolutionOptionsExtraEntry
                {
                    public System.Collections.Generic.IReadOnlyList<string> StrategyOrder
                        { get; set; } = ["Claim", "Header", "Database", "Gateway"];
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsStrategyOrderExtra", source);
        var optionsType = assembly.GetType(
            "Fixture.SecureDefaults.FixtureTenantResolutionOptionsExtraEntry")!;

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals(
                optionsType, "StrategyOrder", ["Claim", "Header", "Database"]);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's default-constructed StrategyOrder resolves to 4 " +
                         "elements, one more than the 3-element expected sequence")
            .WithMessage("*4-element*")
            .WithMessage("*3-element*");
    }

    // ---------------------------------------------------------------------------
    // T-315 — AssertMethodBodyInvokesMethod pass path: callee call present
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-315: A fixture method body that calls the designated callee method must pass
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyInvokesMethod_CalleeCallPresent_DoesNotThrow()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                public static class FixtureWarningLog
                {
                    public static void TrustBoundaryUnconfigured(string headerName) { }
                }

                // Compliant: mirrors the real AddMtlsForwardedHeaderCertificate shape — the
                // registration method calls the designated warning-log method.
                public static class FixtureRegistrationPresent
                {
                    public static void Register(string headerName)
                    {
                        FixtureWarningLog.TrustBoundaryUnconfigured(headerName);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsInvokesPresent", source);
        var declaringType = assembly.GetType("Fixture.SecureDefaults.FixtureRegistrationPresent")!;
        var calleeDeclaringType = assembly.GetType("Fixture.SecureDefaults.FixtureWarningLog")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType, "Register", calleeDeclaringType, "TrustBoundaryUnconfigured");

        act.Should().NotThrow(
            because: "the fixture's Register method genuinely calls " +
                     "FixtureWarningLog.TrustBoundaryUnconfigured");
    }

    // ---------------------------------------------------------------------------
    // T-316 — AssertMethodBodyInvokesMethod fire path: callee call removed
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-316: A fixture method body identical in shape but with the callee-method call removed —
    /// reproducing the exact "warning silently deleted in a future edit" regression this phase
    /// exists to prevent — must fail
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyInvokesMethod_CalleeCallRemoved_ThrowsNamingMethod()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                public static class FixtureWarningLog
                {
                    public static void TrustBoundaryUnconfigured(string headerName) { }
                }

                // Violation: identical in shape to the compliant fixture, but the warning-log
                // call was silently deleted — the exact regression this phase exists to prevent.
                public static class FixtureRegistrationRemoved
                {
                    public static void Register(string headerName)
                    {
                        _ = headerName;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsInvokesRemoved", source);
        var declaringType = assembly.GetType("Fixture.SecureDefaults.FixtureRegistrationRemoved")!;
        var calleeDeclaringType = assembly.GetType("Fixture.SecureDefaults.FixtureWarningLog")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType, "Register", calleeDeclaringType, "TrustBoundaryUnconfigured");

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's Register method no longer calls " +
                         "FixtureWarningLog.TrustBoundaryUnconfigured — the call site was removed")
            .WithMessage("*Register*")
            .WithMessage("*TrustBoundaryUnconfigured*");
    }

    // ---------------------------------------------------------------------------
    // T-317 — Real-assembly verification (GATING): SharedKernel.MultiTenancy
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-317: Confirms the real, shipped <c>TenantResolutionOptions.DefaultStrategyOrder</c> is the
    /// corrected <c>[Claim, Header, Database]</c> order, and that <c>StrategyOrder</c> itself starts
    /// empty so configuration binding replaces the default instead of appending to it.
    /// </summary>
    /// <remarks>
    /// Originally tracked as a Cross-Domain Dependency pending <c>13.ServiceDefaults</c> P-393 —
    /// CONFIRMED RESOLVED on disk before this phase's implementation session:
    /// <c>SharedKernel.MultiTenancy</c> ships C-49's corrected default. Non-vacuous: verified by a
    /// temporary sanity check during implementation — asserting the deliberately-wrong
    /// <c>["Header", "Claim", "Database"]</c> order against this exact real type, confirmed to
    /// fail with the same message shape T-312's contrived fixture produces, then removed before
    /// commit — this test proves the real type's actual resolved default, not merely that the
    /// scan runs without error. Since P-546 the order lives in the static <c>DefaultStrategyOrder</c>
    /// (a non-empty instance default made a bound order append to it), so the check reads the
    /// property value directly instead of scanning the constructor.
    /// </remarks>
    [Fact]
    public void RealTenantResolutionOptions_HardenedDefaultOrderHolds()
    {
        SharedKernel.MultiTenancy.Resolution.TenantResolutionOptions.DefaultStrategyOrder
            .Should().Equal(
                ["Claim", "Header", "Database"],
                because: "the real, shipped TenantResolutionOptions.DefaultStrategyOrder is the " +
                         "corrected, secure order (WO-061, C-49)");

        new SharedKernel.MultiTenancy.Resolution.TenantResolutionOptions().StrategyOrder
            .Should().BeEmpty(
                because: "a non-empty instance default would make a configured order append to it");
    }

    // ---------------------------------------------------------------------------
    // T-318 — Real-assembly verification (GATING): SharedKernel.ServiceDefaults
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-318: Re-points <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> at the
    /// real, shipped <c>MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate</c>
    /// registration method and confirms it still calls
    /// <c>MtlsLog.ForwardedHeaderTrustBoundaryUnconfigured</c>.
    /// </summary>
    /// <remarks>
    /// Originally tracked as a Cross-Domain Dependency pending <c>13.ServiceDefaults</c>
    /// P-394/P-395 — CONFIRMED RESOLVED on disk before this phase's implementation session:
    /// <c>SharedKernel.ServiceDefaults</c> ships C-50/C-53's warning call site. Non-vacuous:
    /// verified by a temporary sanity check during implementation — asserting a deliberately-wrong
    /// callee method name against this exact real registration method, confirmed to fail with the
    /// same message shape T-316's contrived fixture produces, then removed before commit. This
    /// also proves — by construction, since the real call site lives inside a compiler-generated
    /// <c>PostConfigure</c> lambda closure, not directly in the registration method's own IL body
    /// — that <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>'s lambda-closure
    /// scanning is exercised for real, not merely by a contrived fixture shaped to avoid needing
    /// it.
    /// <para>
    /// <b>WO-084:</b> mutual-TLS composition moved into <c>SharedKernel.ServiceDefaults.Security.Mtls</c>,
    /// and its log events — EventIds unchanged — moved from <c>ServiceDefaultsLog</c> into that
    /// package's <c>MtlsLog</c>. Run against the old name before being updated, this test failed with
    /// its own "Could not resolve … has it been renamed or moved?" message, which is the behaviour the
    /// explicit throw exists for.
    /// </para>
    /// </remarks>
    [Fact]
    public void AssertMethodBodyInvokesMethod_RealMtlsForwardedHeaderExtensions_WarningCallSiteHolds()
    {
        var declaringType =
            typeof(SharedKernel.ServiceDefaults.Security.MtlsForwardedHeaderExtensions);

        // MtlsLog is `internal` to SharedKernel.ServiceDefaults.Security.Mtls — no
        // InternalsVisibleTo grant exists (or should exist) to this governance test project, so
        // `typeof(...)` cannot name it directly. Assembly.GetType(string) resolves a Type object
        // by name regardless of accessibility — this helper only ever compares the resolved
        // Type's FullName against Mono.Cecil's TypeReference.FullName, never invokes a member
        // through it, so no accessibility violation occurs at runtime either.
        var calleeDeclaringType =
            declaringType.Assembly.GetType("SharedKernel.ServiceDefaults.Logging.MtlsLog")
            ?? throw new InvalidOperationException(
                "Could not resolve SharedKernel.ServiceDefaults.Logging.MtlsLog via " +
                "Assembly.GetType — has it been renamed or moved?");

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType,
                "AddMtlsForwardedHeaderCertificate",
                calleeDeclaringType,
                "ForwardedHeaderTrustBoundaryUnconfigured");

        act.Should().NotThrow(
            because: "the real, shipped AddMtlsForwardedHeaderCertificate's PostConfigure lambda " +
                     "still calls MtlsLog.ForwardedHeaderTrustBoundaryUnconfigured " +
                     "when TrustedNetworks is left unconfigured (WO-061, C-50/C-53)");
    }

    // ---------------------------------------------------------------------------
    // T-326 — AssertMethodBodyThrowsExceptionType pass path: throw present
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-326: A fixture method body that constructs and throws the expected exception type must
    /// pass <see cref="SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyThrowsExceptionType_ThrowPresent_DoesNotThrow()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                public sealed class FixtureGuardException : System.Exception
                {
                    public FixtureGuardException(string message) : base(message) { }
                }

                // Compliant: mirrors a startup guard that genuinely constructs and throws the
                // expected exception type when a dangerous configuration is detected.
                public static class FixtureGuardPresent
                {
                    public static void Validate(bool isDangerous)
                    {
                        if (isDangerous)
                        {
                            throw new FixtureGuardException("dangerous configuration detected");
                        }
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsThrowsPresent", source);
        var declaringType = assembly.GetType("Fixture.SecureDefaults.FixtureGuardPresent")!;
        var expectedExceptionType = assembly.GetType("Fixture.SecureDefaults.FixtureGuardException")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType(
                declaringType, "Validate", expectedExceptionType);

        act.Should().NotThrow(
            because: "the fixture's Validate method genuinely constructs and throws " +
                     "FixtureGuardException");
    }

    // ---------------------------------------------------------------------------
    // T-327 — AssertMethodBodyThrowsExceptionType fire path: throw removed
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-327: A fixture method body identical in shape but with the throw removed — reproducing
    /// the exact "startup guard silently deleted in a future edit" regression this check exists to
    /// prevent — must fail
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyThrowsExceptionType_ThrowRemoved_ThrowsNamingMethodAndException()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                public sealed class FixtureGuardException : System.Exception
                {
                    public FixtureGuardException(string message) : base(message) { }
                }

                // Violation: identical in shape to the compliant fixture, but the guard's throw
                // was silently deleted — the exact regression this check exists to prevent.
                public static class FixtureGuardRemoved
                {
                    public static void Validate(bool isDangerous)
                    {
                        _ = isDangerous;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsThrowsRemoved", source);
        var declaringType = assembly.GetType("Fixture.SecureDefaults.FixtureGuardRemoved")!;
        var expectedExceptionType = assembly.GetType("Fixture.SecureDefaults.FixtureGuardException")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType(
                declaringType, "Validate", expectedExceptionType);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's Validate method no longer constructs and throws " +
                         "FixtureGuardException — the throw was removed")
            .WithMessage("*Validate*")
            .WithMessage("*FixtureGuardException*");
    }

    // ---------------------------------------------------------------------------
    // T-328 — Real-assembly verification (GATING; re-pointed technique — see remarks)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-328: Confirms the real, shipped CORS wildcard-origin-plus-credentials guard exists inside
    /// <c>SharedKernel.Presentation.WebApi</c> — proven via
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>, NOT
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType"/>, per the
    /// design/reality mismatch documented below.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Cross-Domain Dependency resolved.</strong> Originally tracked as pending
    /// <c>14.Presentation</c> P-404 reaching Core — CONFIRMED RESOLVED on disk before this phase's
    /// implementation session: <c>SharedKernel.Presentation.WebApi</c> is packed at <c>1.2.0</c>
    /// and ships <c>Cors/CorsPolicyOptions.cs</c>, <c>Cors/CorsPolicyOptionsValidator.cs</c>,
    /// <c>Cors/CorsExtensions.cs</c>.
    /// </para>
    /// <para>
    /// <strong>Design/reality mismatch found and resolved.</strong> This phase's own design prose
    /// (D-73) anticipated <c>AddSharedKernelCors</c> itself constructing and throwing a named
    /// exception directly — the exact shape
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType"/> (T-326/T-327) is
    /// built to prove. The REAL shipped guard is validator-based, not throw-based:
    /// <c>CorsExtensions.AddSharedKernelCors</c> registers <c>CorsPolicyOptionsValidator</c> (an
    /// <c>IValidateOptions&lt;CorsPolicyOptions&gt;</c> whose <c>Validate</c> method returns
    /// <c>ValidateOptionsResult.Fail(...)</c> on the dangerous combination) via
    /// <c>services.AddOptions&lt;CorsPolicyOptions&gt;().Configure(configure).ValidateOnStart()</c>.
    /// The actual <c>throw new OptionsValidationException(...)</c> this produces happens entirely
    /// inside <c>Microsoft.Extensions.Options</c>'s own <c>OptionsFactory&lt;TOptions&gt;</c>
    /// machinery at <c>IHost.StartAsync()</c> — FRAMEWORK code, never emitted into
    /// <c>SharedKernel.Presentation.WebApi</c>'s own IL at all. There is therefore no
    /// <c>Newobj</c>-then-<c>Throw</c> sequence anywhere in this real assembly for
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType"/> to find —
    /// re-pointing it at <c>AddSharedKernelCors</c> would be STRUCTURALLY UNABLE to ever pass
    /// against this real assembly, regardless of how correctly the guard itself behaves.
    /// </para>
    /// <para>
    /// <strong>Resolution.</strong> Per this phase's own explicit guidance to use judgment rather
    /// than force-fit a mismatched technique or silently skip verification: this test re-points the
    /// ALREADY-SHIPPED <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> (from
    /// <c>SK.00.TenantAndMtlsBoundaryLock</c>/P-401) at <c>CorsPolicyOptionsValidator.Validate</c>,
    /// asserting it genuinely calls <c>ValidateOptionsResult.Fail</c> — an invocation-presence
    /// assertion matching the guard's REAL shape, reusing existing, already-proven infrastructure
    /// instead of adding a narrowly-motivated seventh method to <see cref="SecureDefaultsAssertion"/>
    /// for a single call site. <see cref="SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType"/>
    /// itself remains unverified against a real assembly by this phase — proven only via
    /// T-326/T-327's contrived fixtures — and remains available as a generically useful technique
    /// for a FUTURE guard that genuinely throws directly from its own method body (the shape most
    /// of this class's other methods were built for).
    /// </para>
    /// <para>
    /// <strong>Non-vacuous.</strong> <c>CorsPolicyOptionsValidator</c> is <c>internal</c> — no
    /// <c>InternalsVisibleTo</c> grant exists (or should exist) to this governance test project, so
    /// <c>typeof(...)</c> cannot name it directly. <c>Assembly.GetType(string)</c> resolves a
    /// <see cref="Type"/> object by name regardless of accessibility, mirroring T-318's identical
    /// technique for <c>MtlsLog</c> — this helper only ever compares
    /// <see cref="System.Reflection.MemberInfo.Name"/>/declaring-type
    /// <c>FullName</c>, never invokes a member through it. T-315/T-316's contrived fixtures already
    /// prove <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> correctly fires
    /// when the expected call site is ABSENT — this test proves the real type's actual call site is
    /// PRESENT, not merely that the scan runs without error.
    /// </para>
    /// </remarks>
    [Fact]
    public void AssertMethodBodyInvokesMethod_RealCorsPolicyOptionsValidator_CallsValidateOptionsResultFail()
    {
        var webApiAssembly = typeof(SharedKernel.Presentation.WebApi.Cors.CorsPolicyNames).Assembly;

        var declaringType =
            webApiAssembly.GetType("SharedKernel.Presentation.WebApi.Cors.CorsPolicyOptionsValidator")
            ?? throw new InvalidOperationException(
                "Could not resolve SharedKernel.Presentation.WebApi.Cors.CorsPolicyOptionsValidator " +
                "via Assembly.GetType — has it been renamed or moved?");

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType,
                "Validate",
                typeof(Microsoft.Extensions.Options.ValidateOptionsResult),
                "Fail");

        act.Should().NotThrow(
            because: "the real, shipped CorsPolicyOptionsValidator.Validate calls " +
                     "ValidateOptionsResult.Fail(...) on the dangerous AllowCredentials + " +
                     "empty/wildcard AllowedOrigins combination (WO-062, P-404)");
    }

    // ---------------------------------------------------------------------------
    // T-329 — AssertMethodBodyInvokesMethod pass path: correlation-id validation call present
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-329 (<c>SK.00.CorrelationIdValidationGuard</c>/WO-063/P-415): A fixture method body
    /// shaped after <c>CorrelationIdMiddleware.ResolveCorrelationId</c>'s real check-then-
    /// substitute pattern — calling its own format-validation helper before deciding whether to
    /// preserve the caller-supplied value or regenerate a fresh one — must pass
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyInvokesMethod_CorrelationValidationCallPresent_DoesNotThrow()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Compliant: mirrors the real CorrelationIdMiddleware.ResolveCorrelationId
                // shape — the resolution method checks the caller-supplied value's format
                // before deciding whether to preserve it or regenerate a fresh one.
                public sealed class FixtureCorrelationResolverPresent
                {
                    public string ResolveCorrelationId(string headerValue)
                    {
                        if (IsValidFormat(headerValue))
                        {
                            return headerValue;
                        }

                        return "generated";
                    }

                    private bool IsValidFormat(string value) => value.Length <= 128;
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsCorrelationInvokesPresent", source);
        var declaringType =
            assembly.GetType("Fixture.SecureDefaults.FixtureCorrelationResolverPresent")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType, "ResolveCorrelationId", declaringType, "IsValidFormat");

        act.Should().NotThrow(
            because: "the fixture's ResolveCorrelationId method genuinely calls IsValidFormat " +
                     "before deciding whether to preserve the caller-supplied value");
    }

    // ---------------------------------------------------------------------------
    // T-330 — AssertMethodBodyInvokesMethod fire path: correlation-id validation call removed
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-330 (<c>SK.00.CorrelationIdValidationGuard</c>/WO-063/P-415): A fixture method body
    /// identical in shape but with the format-validation call removed — reproducing the exact
    /// "format-validation silently deleted in a future edit" regression this check exists to
    /// prevent — must fail <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyInvokesMethod_CorrelationValidationCallRemoved_ThrowsNamingMethod()
    {
        const string source = """
            namespace Fixture.SecureDefaults
            {
                // Violation: identical in shape to the compliant fixture, but the format-
                // validation call was silently deleted — the caller-supplied value would be
                // preserved unconditionally, regardless of length/character-allowlist shape.
                public sealed class FixtureCorrelationResolverRemoved
                {
                    public string ResolveCorrelationId(string headerValue)
                    {
                        return headerValue;
                    }

                    private bool IsValidFormat(string value) => value.Length <= 128;
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsCorrelationInvokesRemoved", source);
        var declaringType =
            assembly.GetType("Fixture.SecureDefaults.FixtureCorrelationResolverRemoved")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType, "ResolveCorrelationId", declaringType, "IsValidFormat");

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's ResolveCorrelationId method no longer calls " +
                         "IsValidFormat — the format-validation call site was removed")
            .WithMessage("*ResolveCorrelationId*")
            .WithMessage("*IsValidFormat*");
    }

    // ---------------------------------------------------------------------------
    // T-331 — Real-assembly verification (GATING): SharedKernel.Presentation.WebApi
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-331 (<c>SK.00.CorrelationIdValidationGuard</c>/WO-063/P-415): Re-points
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> at the real, shipped
    /// <c>CorrelationIdMiddleware.ResolveCorrelationId</c> and confirms it still calls its own
    /// private <c>IsValidFormat</c> format-validation helper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Cross-Domain Dependency resolved.</strong> This phase's own authoring-time prose
    /// recorded P-415 as <c>○</c> Design-only against <c>14.Presentation/state-map.md</c> — STALE
    /// by the time of this implementation session: that domain shipped its full WO-063 scope
    /// (D-57/D-58/S-25/C-64/C-65/T-55–T-57/DO-23 all <c>●</c>,
    /// <c>SharedKernel.Presentation.WebApi</c> re-packed to <c>1.3.0</c>) before this phase's
    /// implementation session began, mirroring the now-repeated dependency-resolved-before-
    /// implementation pattern this file already records for
    /// <c>SK.00.SenderConstrainedCredentialGuard</c>/<c>SK.00.SecureDefaultsLock</c>/
    /// <c>SK.00.TenantAndMtlsBoundaryLock</c>/<c>SK.00.CorsWildcardCredentialsGuard</c>.
    /// </para>
    /// <para>
    /// <strong>Design matched reality, unlike the CORS phase.</strong> The real, shipped
    /// <c>CorrelationIdMiddleware.ResolveCorrelationId</c> is a conditional check-then-substitute
    /// shape exactly as this phase's design (D-58) anticipated — it calls its own private
    /// <c>IsValidFormat</c> helper and falls back to regenerating a fresh value when the check
    /// fails, never throwing. <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>
    /// is therefore the correct technique with no re-pointing surprise, unlike
    /// <c>SK.00.CorsWildcardCredentialsGuard</c>'s validator-vs-throw mismatch — this phase adds
    /// zero new production code to <see cref="SecureDefaultsAssertion"/>.
    /// </para>
    /// <para>
    /// <strong>Non-vacuous.</strong> Verified by a temporary sanity check during implementation —
    /// asserting a deliberately-wrong callee method name (<c>"IsValidFormatXyz"</c>) against this
    /// exact real method, confirmed to fail with the same message shape T-330's contrived fixture
    /// produces, then reverted before commit. Both <c>ResolveCorrelationId</c> and
    /// <c>IsValidFormat</c> are declared directly on <c>CorrelationIdMiddleware</c> itself — the
    /// callee is a private instance method on the SAME type, not a different one — and the call
    /// site lives directly in <c>ResolveCorrelationId</c>'s own IL body, not inside a lambda
    /// closure, so no closure-scanning extension is exercised by this particular call site (that
    /// extension remains proven by T-318/T-328's own real-assembly call sites).
    /// </para>
    /// </remarks>
    [Fact]
    public void AssertMethodBodyInvokesMethod_RealCorrelationIdMiddleware_ValidationCallSiteHolds()
    {
        var declaringType = typeof(SharedKernel.Presentation.WebApi.Middleware.CorrelationIdMiddleware);

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType, "ResolveCorrelationId", declaringType, "IsValidFormat");

        act.Should().NotThrow(
            because: "the real, shipped CorrelationIdMiddleware.ResolveCorrelationId still calls " +
                     "its own private IsValidFormat format-validation helper before preserving a " +
                     "caller-supplied value (WO-063, C-65)");
    }

    // ---------------------------------------------------------------------------
    // T-332 — AssertMethodBodyRegistersSingleton pass path: expected pair registered
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-332: A fixture builder method that registers the expected service→implementation pair via
    /// <c>AddSingleton&lt;TService,TImplementation&gt;()</c> must pass
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyRegistersSingleton_ExpectedPairRegistered_DoesNotThrow()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Fixture.SecureDefaults
            {
                public interface IFixtureUrlValidator { }

                public sealed class FixtureUrlValidatorDefault : IFixtureUrlValidator { }

                // Compliant: mirrors AddSharedKernelWebhooks registering its default SSRF guard.
                public static class FixtureRegistrationPresent
                {
                    public static IServiceCollection Build(IServiceCollection services)
                    {
                        services.AddSingleton<IFixtureUrlValidator, FixtureUrlValidatorDefault>();
                        return services;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsRegistersSingletonPresent", source);
        var declaringType = assembly.GetType("Fixture.SecureDefaults.FixtureRegistrationPresent")!;
        var serviceType = assembly.GetType("Fixture.SecureDefaults.IFixtureUrlValidator")!;
        var implementationType = assembly.GetType("Fixture.SecureDefaults.FixtureUrlValidatorDefault")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton(
                declaringType, "Build", serviceType, implementationType);

        act.Should().NotThrow(
            because: "Build registers IFixtureUrlValidator -> FixtureUrlValidatorDefault via " +
                     "AddSingleton");
    }

    // ---------------------------------------------------------------------------
    // T-333 — AssertMethodBodyRegistersSingleton fire path: registration removed
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-333: A fixture builder method with the default registration silently removed — the exact
    /// regression this check exists to prevent — must fail
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyRegistersSingleton_RegistrationRemoved_ThrowsNamingTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Fixture.SecureDefaults
            {
                public interface IFixtureUrlValidator { }

                public sealed class FixtureUrlValidatorDefault : IFixtureUrlValidator { }

                // Regression case: the default registration was silently deleted in a later edit.
                public static class FixtureRegistrationRemoved
                {
                    public static IServiceCollection Build(IServiceCollection services)
                    {
                        return services;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsRegistersSingletonRemoved", source);
        var declaringType = assembly.GetType("Fixture.SecureDefaults.FixtureRegistrationRemoved")!;
        var serviceType = assembly.GetType("Fixture.SecureDefaults.IFixtureUrlValidator")!;
        var implementationType = assembly.GetType("Fixture.SecureDefaults.FixtureUrlValidatorDefault")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton(
                declaringType, "Build", serviceType, implementationType);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's Build method no longer registers IFixtureUrlValidator -> " +
                         "FixtureUrlValidatorDefault — the registration was removed")
            .WithMessage("*FixtureRegistrationRemoved*")
            .WithMessage("*FixtureUrlValidatorDefault*");
    }

    // ---------------------------------------------------------------------------
    // T-334 — AssertMethodBodyRegistersSingleton fire path (boundary): wrong implementation
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-334: A fixture builder method that registers a DIFFERENT implementation type for the same
    /// service interface must fail
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton"/> — proving the check
    /// verifies the exact service→implementation pairing, not merely "some <c>AddSingleton</c> call
    /// for that service exists somewhere."
    /// </summary>
    [Fact]
    public void AssertMethodBodyRegistersSingleton_DifferentImplementationRegistered_ThrowsNamingTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Fixture.SecureDefaults
            {
                public interface IFixtureUrlValidator { }

                public sealed class FixtureUrlValidatorDefault : IFixtureUrlValidator { }

                public sealed class FixtureUrlValidatorOther : IFixtureUrlValidator { }

                // Boundary case: the same service interface is registered, but with a DIFFERENT
                // implementation type.
                public static class FixtureRegistrationWrongImplementation
                {
                    public static IServiceCollection Build(IServiceCollection services)
                    {
                        services.AddSingleton<IFixtureUrlValidator, FixtureUrlValidatorOther>();
                        return services;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsRegistersSingletonWrongImpl", source);
        var declaringType =
            assembly.GetType("Fixture.SecureDefaults.FixtureRegistrationWrongImplementation")!;
        var serviceType = assembly.GetType("Fixture.SecureDefaults.IFixtureUrlValidator")!;
        var implementationType = assembly.GetType("Fixture.SecureDefaults.FixtureUrlValidatorDefault")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton(
                declaringType, "Build", serviceType, implementationType);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "Build registers IFixtureUrlValidator -> FixtureUrlValidatorOther, not " +
                         "the expected FixtureUrlValidatorDefault — the exact pairing is not " +
                         "satisfied")
            .WithMessage("*FixtureRegistrationWrongImplementation*")
            .WithMessage("*FixtureUrlValidatorDefault*");
    }

    // ---------------------------------------------------------------------------
    // T-335 — Real-assembly verification (GATING): SharedKernel.Integration.Webhooks (Technique A)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-335 (<c>SK.00.WebhookSsrfGuardLock</c>/WO-064/P-432): Re-points
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton"/> at the real, shipped
    /// <c>ServiceCollectionExtensions.AddSharedKernelWebhooks</c> registration method and confirms
    /// it still registers <c>IWebhookUrlValidator -> PrivateNetworkWebhookUrlValidator</c> as the
    /// default SSRF guard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Cross-Domain Dependency resolved.</strong> This phase's own authoring-time prose
    /// recorded P-422 (H-06/H-07) as <c>○</c> Not Started against <c>15.Integration/state-map.md</c>
    /// — STALE by the time of this implementation session: that domain had already shipped
    /// <c>IWebhookUrlValidator</c>/<c>PrivateNetworkWebhookUrlValidator</c> and their default
    /// registration inside <c>AddSharedKernelWebhooks</c> before this phase's implementation
    /// session began, mirroring the now-repeated dependency-resolved-before-implementation pattern
    /// this file already records for <c>SK.00.SenderConstrainedCredentialGuard</c>/
    /// <c>SK.00.SecureDefaultsLock</c>/<c>SK.00.TenantAndMtlsBoundaryLock</c>/
    /// <c>SK.00.CorsWildcardCredentialsGuard</c>/<c>SK.00.CorrelationIdValidationGuard</c>.
    /// </para>
    /// <para>
    /// <strong>Design/reality drift found and corrected.</strong> This phase's own design prose
    /// (Implementation Rule 3) assumed a plain <c>AddSingleton</c> call — the real, shipped
    /// registration instead uses <c>services.TryAddSingleton&lt;IWebhookUrlValidator,
    /// PrivateNetworkWebhookUrlValidator&gt;()</c>. <see cref="SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton"/>
    /// was written to accept both method-name shapes from the start (see that method's own
    /// remarks) rather than being narrowed after the fact.
    /// </para>
    /// <para>
    /// <strong>Non-vacuous.</strong> Verified by a temporary sanity check during implementation —
    /// asserting a deliberately-wrong implementation type against this exact real registration
    /// method, confirmed to fail with the same message shape T-333's contrived fixture produces,
    /// then reverted before commit. The real call site lives directly in
    /// <c>AddSharedKernelWebhooks</c>'s own IL body, not inside a lambda closure, so no
    /// closure-scanning extension is exercised by this particular call site (that extension
    /// remains proven by T-318/T-328/T-331's own real call sites).
    /// </para>
    /// </remarks>
    [Fact]
    public void AssertMethodBodyRegistersSingleton_RealAddSharedKernelWebhooks_DefaultUrlValidatorRegistrationHolds()
    {
        var declaringType =
            typeof(SharedKernel.Integration.Webhooks.Extensions.ServiceCollectionExtensions);
        var serviceType = typeof(SharedKernel.Integration.Webhooks.Dispatch.IWebhookUrlValidator);
        var implementationType =
            typeof(SharedKernel.Integration.Webhooks.Dispatch.PrivateNetworkWebhookUrlValidator);

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton(
                declaringType, "AddSharedKernelWebhooks", serviceType, implementationType);

        act.Should().NotThrow(
            because: "the real, shipped AddSharedKernelWebhooks still registers " +
                     "IWebhookUrlValidator -> PrivateNetworkWebhookUrlValidator as the default " +
                     "SSRF guard (WO-064, P-422)");
    }

    // ---------------------------------------------------------------------------
    // T-336 — Real-assembly, EXECUTED verification: PrivateNetworkWebhookUrlValidator (Technique B)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-336 (<c>SK.00.WebhookSsrfGuardLock</c>/WO-064/P-432): invokes the real, compiled
    /// <c>PrivateNetworkWebhookUrlValidator</c> against a fixed table of IP-literal hosts and
    /// confirms it rejects loopback/private/link-local-metadata targets while accepting a
    /// public-internet control address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Deliberate departure from this file's IL-only discipline.</strong> Every other
    /// method in this class proves a STRUCTURAL fact via Mono.Cecil IL inspection, never executing
    /// the assembly under test. "Rejects the documented private/loopback/link-local/metadata IP
    /// ranges" is a COMPUTED BEHAVIOR of range-membership logic whose concrete representation
    /// (hardcoded byte-range comparisons, as shipped, or something else) is not something a sound,
    /// representation-agnostic static IL technique could honestly prove — see
    /// <c>SecureDefaultsAssertion</c>'s own class remarks (Technique 5) and this phase's
    /// Implementation Rule 4. This test instead resolves and directly invokes the real, compiled
    /// <c>PrivateNetworkWebhookUrlValidator</c>'s public <c>IWebhookUrlValidator</c> contract
    /// method — the first genuinely EXECUTED real-assembly test in this file.
    /// </para>
    /// <para>
    /// <strong>Offline and deterministic.</strong> Every host below is an IP literal, never a DNS
    /// hostname — <c>Dns.GetHostAddressesAsync</c> against an IP-literal host resolves purely
    /// locally per BCL contract, with no actual network I/O, mirroring <c>15.Integration</c>'s own
    /// H-08 test-design constraint ("never a real DNS lookup or network call") applied here to a
    /// governance test instead of a domain test.
    /// </para>
    /// <para>
    /// <strong>Non-vacuous.</strong> Verified by a temporary sanity check during implementation —
    /// inverting the expected accept/reject outcomes, confirmed to fail, then reverted before
    /// commit.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("127.0.0.1", false)] // loopback
    [InlineData("10.0.0.1", false)] // RFC 1918 private
    [InlineData("169.254.169.254", false)] // link-local / cloud-metadata (AWS/Azure/GCP)
    [InlineData("8.8.8.8", true)] // public-internet control address
    public async Task PrivateNetworkWebhookUrlValidator_RealValidator_RejectsPrivateAcceptsPublic(
        string ipLiteralHost,
        bool expectedAllowed)
    {
        var options = Microsoft.Extensions.Options.Options.Create(
            new SharedKernel.Integration.Webhooks.Options.WebhookDeliveryOptions());
        var validator =
            new SharedKernel.Integration.Webhooks.Dispatch.PrivateNetworkWebhookUrlValidator(options);

        var url = new Uri($"https://{ipLiteralHost}/webhook");

        var allowed = await validator.ValidateAsync(url, CancellationToken.None);

        allowed.Should().Be(
            expectedAllowed,
            because: $"the real, shipped PrivateNetworkWebhookUrlValidator must " +
                     $"{(expectedAllowed ? "allow" : "reject")} {ipLiteralHost} (WO-064, P-422)");
    }

    // ---------------------------------------------------------------------------
    // T-337 — Real-assembly, EXECUTED verification: AddCacheEncryption() composition ordering
    // (Technique A)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-337 (<c>SK.00.CacheEncryptionAndRedisValidationLock</c>/WO-065/P-437; RE-LOCKED
    /// 2026-09-08 against <c>02.Caching</c>'s <c>SK.02.CacheEncryptionAadBinding</c> phase, see
    /// this class's own type-level remarks for the full incident record): builds the real, shipped
    /// <c>ICachingBuilder.AddBrotliCompression()</c> + <c>.AddCacheEncryption()</c> composed
    /// pipeline (<c>SharedKernel.Caching.FusionCache</c>) and confirms compression genuinely runs
    /// before encryption on write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Re-pointed at the real, current architecture.</strong>
    /// <c>SK.02.CacheEncryptionAadBinding</c> deleted <c>CacheEncryptionSerializer</c> entirely:
    /// <c>IFusionCacheSerializer.Serialize</c>/<c>Deserialize</c> never receive the cache key, so a
    /// serializer-level decorator is structurally incapable of deriving key-bound associated data
    /// (AAD). <c>AddCacheEncryption()</c> now wraps <c>ICacheService</c> with a new
    /// <c>Encryption.EncryptedCacheService</c> instead — the one layer where the cache key is an
    /// explicit method parameter on every member. This test therefore intercepts at the
    /// <c>ICacheService</c> layer, not the serializer layer.
    /// </para>
    /// <para>
    /// <strong>Deliberate departure from this file's IL-only discipline.</strong> Every
    /// <see cref="SecureDefaultsAssertion"/> method proves a STRUCTURAL fact via Mono.Cecil IL
    /// inspection, never executing the assembly under test. "Compression ran before encryption" is
    /// a COMPUTED BEHAVIOR of <c>EncryptedCacheService</c>'s own runtime branch
    /// (<c>_compression</c>, the <c>CacheCompressionOptions</c> captured at resolution time by <c>AddCacheEncryption()</c>
    /// from whether the PRE-<c>AddCacheEncryption()</c> <c>IFusionCacheSerializer</c> registration was
    /// a <c>BrotliCacheSerializer</c>) whose concrete representation is not something a sound,
    /// representation-agnostic static IL technique could honestly prove — mirroring
    /// <c>SK.00.WebhookSsrfGuardLock</c>'s own T-336 precedent exactly. This remains the SECOND
    /// genuinely EXECUTED real-assembly test in this file, living directly here rather than as a
    /// reusable <see cref="SecureDefaultsAssertion"/> method, since it is a one-off assertion tied to
    /// one real pipeline.
    /// </para>
    /// <para>
    /// <strong>Black-box, representation-agnostic, at the correct layer.</strong> This test makes no
    /// assumption about <c>EncryptedCacheService</c>'s internal shape beyond the public
    /// <c>ICachingBuilder.AddBrotliCompression()</c>/<c>.AddCacheEncryption()</c> entry points and
    /// the resulting <c>ICacheService.SetAsync</c>/<c>TryGetAsync</c> round trip. Because
    /// <c>EncryptedCacheService</c> wraps <c>ICacheService</c> rather than the serializer, the test
    /// registers its own minimal in-memory <c>ICacheService</c> double
    /// (<see cref="SpyInnerCacheService"/>) BEFORE calling <c>AddSharedKernelCaching()</c> — that
    /// method registers its own default via <c>TryAddSingleton&lt;ICacheService,
    /// FusionCacheService&gt;()</c>, a no-op once a registration already exists, so
    /// <c>AddCacheEncryption()</c>'s "wrap whatever <c>ICacheService</c> is currently registered"
    /// logic ends up wrapping the double directly — a clean interception point for exactly the
    /// <c>EncryptedPayload.ToBytes()</c> output <c>EncryptedCacheService</c> hands to its inner store, with no
    /// FusionCache/MemoryCache storage semantics in the way.
    /// </para>
    /// <para>
    /// It writes a highly-compressible payload (8192 repeated characters) through the composed
    /// pipeline and captures the intercepted <c>EncryptedPayload</c> storage bytes' length. It
    /// separately encrypts the SAME plaintext JSON bytes a bare System.Text.Json serialization step
    /// would produce — using default <c>JsonSerializerOptions</c> (General defaults), the exact options
    /// <c>EncryptedCacheService</c> itself uses when no <c>CachingOptions.SerializerContext</c> is
    /// configured (which this test does not configure) — with the SAME cache-key-derived AAD
    /// <c>EncryptedCacheService</c> uses, directly via
    /// <c>ISymmetricEncryptionService.EncryptAsync</c> (no compression), as a baseline. If the real
    /// composition order were reversed (encrypt-then-attempt-compress) or compression were silently
    /// dropped from the wiring, compressing already-encrypted high-entropy ciphertext would yield
    /// near-zero size reduction — a well-known property of general-purpose compression against
    /// high-entropy input — so this assertion correctly fails and catches the exact regression this
    /// phase exists to prevent.
    /// </para>
    /// <para>
    /// <strong>Round-trip-correctness precondition</strong> (Implementation Rule 3). Reading the
    /// same key back through the composed pipeline's own <c>TryGetAsync</c> path must recover the
    /// original payload exactly — guarding against the size-reduction assertion accidentally
    /// passing against corrupted or no-op output rather than genuine compress-then-encrypt
    /// behavior.
    /// </para>
    /// <para>
    /// <strong>DOCUMENTED LIMITATION</strong> (same class as every other technique in this file): a
    /// size-ratio threshold is a reliable, low-false-positive signal for any straightforward
    /// correct-vs-reversed implementation, not a byte-exact structural proof of instruction
    /// ordering. The acceptance bar this test proves is "the shipped pipeline's observable size
    /// behavior is consistent with compress-then-encrypt," not "the pipeline's IL provably calls
    /// Compress before Encrypt in every code path."
    /// </para>
    /// <para>
    /// <strong>Non-vacuous, re-verified at re-lock time.</strong> Verified by a temporary sanity
    /// check during this re-lock — removing <c>.AddBrotliCompression()</c> from the chain (so
    /// <c>EncryptedCacheService</c>'s <c>_compression</c> resolves to
    /// <see langword="null"/>, reproducing "compression silently stopped being wired into the
    /// pipeline") — confirmed the size assertion then genuinely fails, then reverted before commit.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CacheEncryptionPipeline_RealAddCacheEncryption_CompressesBeforeEncrypting()
    {
        var services = new ServiceCollection();

        var keyProvider = new StaticEncryptionKeyProvider(
            "secure-defaults-fixture-key",
            [new CryptographicKey("secure-defaults-fixture-key", System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))]);
        var encryptionService = new AesGcmEncryptionService(keyProvider);
        services.AddSingleton<ISymmetricEncryptionService>(encryptionService);

        // Registered BEFORE AddSharedKernelCaching() so its own
        // TryAddSingleton<ICacheService, FusionCacheService>() no-ops — AddCacheEncryption()'s
        // "wrap whatever ICacheService is currently registered" logic then wraps this double
        // directly.
        var spyInnerCache = new SpyInnerCacheService();
        services.AddSingleton<ICacheService>(spyInnerCache);

        // EncryptedCacheService takes an ILogger<EncryptedCacheService> dependency (used only to
        // log a decrypt-failure warning — never exercised by this test's happy-path round trip).
        services.AddLogging();

        services
            .AddSharedKernelCaching(o => o.ServiceName = "cache-encryption-ordering-test")
            .AddBrotliCompression()
            .AddCacheEncryption();

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICacheService>();

        const string key = "cache-encryption-ordering-test-key";

        // A highly compressible payload — repeated-character input compresses to a tiny fraction of
        // its original size under Brotli, but is incompressible once already AES-GCM encrypted.
        var payload = new string('A', 8192);

        await cache.SetAsync(key, payload, CachePolicy.Default);

        byte[]? pipelineStoredBytes = spyInnerCache.LastStoredBytes;
        pipelineStoredBytes.Should().NotBeNull(
            because: "EncryptedCacheService.SetAsync must hand its inner ICacheService the " +
                     "EncryptedPayload storage format (EncryptedPayload.ToBytes())");
        EncryptedPayload.TryParse(pipelineStoredBytes, out _).Should().BeTrue(
            because: "the bytes EncryptedCacheService stores must be a parseable EncryptedPayload");

        var pipelineLength = pipelineStoredBytes!.Length;

        // Round-trip-correctness precondition (Implementation Rule 3).
        var roundTripped = await cache.TryGetAsync<string>(key);
        roundTripped.IsHit.Should().BeTrue(
            because: "the entry was just written through the composed pipeline");
        roundTripped.Value.Should().Be(
            payload,
            because: "the composed pipeline's read path must recover the original payload " +
                     "exactly, guarding against the size assertion below passing against " +
                     "corrupted or no-op output");

        // Baseline: the SAME plaintext JSON bytes EncryptedCacheService itself produces when no
        // CachingOptions.SerializerContext is configured (default JsonSerializerOptions, General defaults),
        // encrypted with the SAME cache-key-derived AAD EncryptedCacheService uses, but with no
        // compression.
        var jsonOptions = new System.Text.Json.JsonSerializerOptions();
        var plaintextBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload, jsonOptions);
        var associatedData = System.Text.Encoding.UTF8.GetBytes(key);
        var baseline = await encryptionService.EncryptAsync(plaintextBytes, associatedData);
        var baselineLength = baseline.ToBytes().Length;

        pipelineLength.Should().BeLessThan(
            baselineLength / 2,
            because: "compress-then-encrypt must produce a meaningfully smaller stored payload " +
                     "than encrypt-only for a highly compressible input — if EncryptedCacheService's " +
                     "own compress-before-encrypt branch were reversed or silently disabled, " +
                     "compressing already-encrypted high-entropy ciphertext would yield near-zero " +
                     "size reduction (WO-065/P-433, re-locked against WO-081/SK.02.CacheEncryptionAadBinding)");
    }

    /// <summary>
    /// Minimal in-memory <see cref="ICacheService"/> double used only by T-337 — stores whatever is
    /// written under each key and exposes the most recently stored <see cref="T:byte[]"/> (the
    /// <see cref="EncryptedPayload"/> storage format <c>EncryptedCacheService</c> writes) so the test
    /// can inspect exactly what <c>EncryptedCacheService</c> hands to its wrapped inner cache.
    /// Deliberately local to this file rather than a shared <c>16.Testing</c> fake, mirroring this
    /// file's own established "governance test project supplies its own minimal fixture" convention
    /// for real-assembly checks.
    /// </summary>
    private sealed class SpyInnerCacheService : ICacheService
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object?> _store = new();

        /// <summary>
        /// The most recently stored value across any <see cref="SetAsync{T}"/> call whose value was
        /// a <see cref="T:byte[]"/> — <see langword="null"/> until one has been stored.
        /// </summary>
        public byte[]? LastStoredBytes { get; private set; }

        public ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default) =>
            new(_store.TryGetValue(key, out object? value) ? CacheLookup<T>.Hit((T)value!) : CacheLookup<T>.Miss);

        public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
        {
            _store[key] = value;

            if (value is byte[] bytes)
                LastStoredBytes = bytes;

            return ValueTask.CompletedTask;
        }

        public ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            CachePolicy policy,
            CancellationToken ct = default) =>
            GetOrSetAsync(key, (_, token) => factory(token), policy, ct);

        public async ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
            CachePolicy policy,
            CancellationToken ct = default)
        {
            if (_store.TryGetValue(key, out object? existing))
                return (T)existing!;

            var context = new CacheFactoryContext(key, policy);
            T value = await factory(context, ct).ConfigureAwait(false);
            if (!context.IsCachingSkipped)
                await SetAsync(key, value, policy, ct).ConfigureAwait(false);

            return value;
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask ExpireAsync(string key, CancellationToken ct = default) => RemoveAsync(key, ct);

        public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask ClearAsync(CancellationToken ct = default)
        {
            _store.Clear();
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(
            IEnumerable<string> keys,
            CancellationToken ct = default)
        {
            IReadOnlyDictionary<string, CacheLookup<T>> result = keys.Distinct().ToDictionary(
                k => k,
                k => _store.TryGetValue(k, out object? v) ? CacheLookup<T>.Hit((T)v!) : CacheLookup<T>.Miss);
            return new ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>>(result);
        }

        public ValueTask SetManyAsync<T>(
            IReadOnlyDictionary<string, T> entries,
            CachePolicy policy,
            CancellationToken ct = default)
        {
            foreach ((string k, T v) in entries)
                _store[k] = v;

            return ValueTask.CompletedTask;
        }
    }

    // ---------------------------------------------------------------------------
    // T-338 — AssertMethodBodyInvokesMethod pass path: ValidateOnStart() call present
    // (Technique B)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-338 (<c>SK.00.CacheEncryptionAndRedisValidationLock</c>/WO-065/P-437): A fixture method
    /// body shaped after the real <c>AddRedisConnection</c>'s options-registration pattern —
    /// calling <c>OptionsBuilder&lt;T&gt;.ValidateOnStart()</c> directly in its own fluent chain —
    /// must pass <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyInvokesMethod_RedisValidateOnStartCallPresent_DoesNotThrow()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Fixture.SecureDefaults
            {
                public sealed class FixtureRedisConnectionOptions
                {
                    public string ConnectionString { get; set; } = string.Empty;
                }

                // Compliant: mirrors the real AddRedisConnection shape — registers the options
                // type and genuinely calls ValidateOnStart() so Microsoft.Extensions.Options's own
                // startup-validation machinery runs eagerly at IHost.StartAsync().
                public static class FixtureRedisConnectionExtensionsPresent
                {
                    public static IServiceCollection AddFixtureRedisConnection(
                        this IServiceCollection services, string connectionString)
                    {
                        services
                            .AddOptions<FixtureRedisConnectionOptions>()
                            .Configure(o => o.ConnectionString = connectionString)
                            .ValidateOnStart();

                        return services;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsRedisValidateOnStartPresent", source);
        var declaringType =
            assembly.GetType("Fixture.SecureDefaults.FixtureRedisConnectionExtensionsPresent")!;
        var calleeDeclaringType =
            typeof(Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions);

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType, "AddFixtureRedisConnection", calleeDeclaringType, "ValidateOnStart");

        act.Should().NotThrow(
            because: "the fixture's AddFixtureRedisConnection method genuinely calls " +
                     "ValidateOnStart() in its own fluent options-registration chain");
    }

    // ---------------------------------------------------------------------------
    // T-339 — AssertMethodBodyInvokesMethod fire path: ValidateOnStart() call removed
    // (Technique B)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-339 (<c>SK.00.CacheEncryptionAndRedisValidationLock</c>/WO-065/P-437): An otherwise-
    /// identical fixture method body with the <c>ValidateOnStart()</c> call removed — reproducing
    /// the exact "eager startup validation silently deleted/never wired in a future edit"
    /// regression this check exists to prevent — must fail
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/>.
    /// </summary>
    [Fact]
    public void AssertMethodBodyInvokesMethod_RedisValidateOnStartCallRemoved_ThrowsNamingMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Fixture.SecureDefaults
            {
                public sealed class FixtureRedisConnectionOptions
                {
                    public string ConnectionString { get; set; } = string.Empty;
                }

                // Violation: identical in shape to the compliant fixture, but the
                // ValidateOnStart() call was silently deleted — RedisConnectionOptions's
                // DataAnnotations would only ever be checked lazily on first options access, if at
                // all, never eagerly at host startup.
                public static class FixtureRedisConnectionExtensionsRemoved
                {
                    public static IServiceCollection AddFixtureRedisConnection(
                        this IServiceCollection services, string connectionString)
                    {
                        services
                            .AddOptions<FixtureRedisConnectionOptions>()
                            .Configure(o => o.ConnectionString = connectionString);

                        return services;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SecureDefaultsRedisValidateOnStartRemoved", source);
        var declaringType =
            assembly.GetType("Fixture.SecureDefaults.FixtureRedisConnectionExtensionsRemoved")!;
        var calleeDeclaringType =
            typeof(Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions);

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType, "AddFixtureRedisConnection", calleeDeclaringType, "ValidateOnStart");

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's AddFixtureRedisConnection method no longer calls " +
                         "ValidateOnStart() — the eager startup-validation call site was removed")
            .WithMessage("*AddFixtureRedisConnection*")
            .WithMessage("*ValidateOnStart*");
    }

    // ---------------------------------------------------------------------------
    // T-340 — Real-assembly verification (GATING): SharedKernel.Caching.Redis.Core
    // (Technique B)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-340 (<c>SK.00.CacheEncryptionAndRedisValidationLock</c>/WO-065/P-437, re-locked for the P-547
    /// Redis redesign): Re-points <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> at
    /// the real, shipped <c>RedisConnectionCoreExtensions.AddRedisConnection(IServiceCollection,
    /// Action&lt;RedisConnectionOptions&gt;)</c> overload and confirms it still calls
    /// <c>OptionsBuilderExtensions.ValidateOnStart</c> — proving <c>RedisConnectionOptions</c>'s
    /// <c>DataAnnotations</c> and validator are genuinely enforced eagerly at host startup when the
    /// connection is configured in code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Overload disambiguation (P-547).</strong> <c>AddRedisConnection</c> now has two
    /// overloads, so the method is selected by exact parameter types. The configuration overload
    /// validates through <c>AddValidatedOptions</c> instead; its lock is
    /// <see cref="AssertMethodBodyInvokesMethod_RealAddRedisConnectionConfigurationOverload_ValidatesOnStartThroughAddValidatedOptions"/>.
    /// </para>
    /// <para>
    /// <strong>Declaring-type/namespace correction applied.</strong> The real extension method
    /// <c>ValidateOnStart&lt;TOptions&gt;(this OptionsBuilder&lt;TOptions&gt;)</c> is declared in
    /// the <c>Microsoft.Extensions.Options</c> NuGet package, but under the
    /// <c>Microsoft.Extensions.DependencyInjection</c> namespace — not
    /// <c>Microsoft.Extensions.Options.OptionsBuilderExtensions</c> as this phase's authoring-time
    /// Rule 6 first named it.
    /// </para>
    /// <para>
    /// <strong>Non-vacuous.</strong> The call site
    /// (<c>services.AddOptions&lt;RedisConnectionOptions&gt;().Configure(configure)
    /// .ValidateDataAnnotations().ValidateOnStart()</c>) lives directly in the overload's own IL body,
    /// so neither closure scanning nor sibling delegation can satisfy it by accident.
    /// </para>
    /// </remarks>
    [Fact]
    public void AssertMethodBodyInvokesMethod_RealAddRedisConnection_ValidateOnStartCallSiteHolds()
    {
        var declaringType =
            typeof(SharedKernel.Caching.Redis.Core.Extensions.RedisConnectionCoreExtensions);
        var calleeDeclaringType =
            typeof(Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions);

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType,
                "AddRedisConnection",
                calleeDeclaringType,
                "ValidateOnStart",
                [typeof(IServiceCollection), typeof(Action<SharedKernel.Caching.Redis.Core.RedisConnectionOptions>)]);

        act.Should().NotThrow(
            because: "the real, shipped delegate overload of AddRedisConnection still calls ValidateOnStart() so " +
                     "RedisConnectionOptions's DataAnnotations are genuinely enforced eagerly at " +
                     "host startup (WO-065, P-436, P-547)");
    }

    /// <summary>
    /// T-340b (P-547): the configuration overload
    /// <c>AddRedisConnection(IServiceCollection, IConfiguration, Action&lt;RedisConnectionOptions&gt;?)</c>
    /// registers its options through <c>SharedKernel.Configuration</c>'s <c>AddValidatedOptions</c>,
    /// and <c>AddValidatedOptions</c>'s shared binding helper calls <c>ValidateOnStart</c> — so a
    /// connection bound from configuration is validated at host startup too.
    /// </summary>
    /// <remarks>
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> follows delegation only
    /// within one type, so the chain is locked in two links: the overload calls
    /// <c>OptionsExtensions.AddValidatedOptions</c>, and every <c>AddValidatedOptions</c> overload
    /// funnels into the single private <c>OptionsExtensions.BindAndValidateOnStart</c>, which calls
    /// <c>ValidateOnStart</c>. Removing either call fails this test.
    /// </remarks>
    [Fact]
    public void AssertMethodBodyInvokesMethod_RealAddRedisConnectionConfigurationOverload_ValidatesOnStartThroughAddValidatedOptions()
    {
        var registersThroughAddValidatedOptions = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                typeof(SharedKernel.Caching.Redis.Core.Extensions.RedisConnectionCoreExtensions),
                "AddRedisConnection",
                typeof(SharedKernel.Configuration.Extensions.OptionsExtensions),
                "AddValidatedOptions",
                [
                    typeof(IServiceCollection),
                    typeof(IConfiguration),
                    typeof(Action<SharedKernel.Caching.Redis.Core.RedisConnectionOptions>),
                ]);

        var addValidatedOptionsValidatesOnStart = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                typeof(SharedKernel.Configuration.Extensions.OptionsExtensions),
                "BindAndValidateOnStart",
                typeof(Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions),
                "ValidateOnStart");

        registersThroughAddValidatedOptions.Should().NotThrow(
            because: "the configuration overload of AddRedisConnection must register RedisConnectionOptions " +
                     "through AddValidatedOptions (P-547)");
        addValidatedOptionsValidatesOnStart.Should().NotThrow(
            because: "AddValidatedOptions arms ValidateOnStart for every options type it registers (P-530)");
    }

    // ---------------------------------------------------------------------------
    // T-360 — AssertMethodBodyInvokesMethod/AssertMethodBodyThrowsExceptionType contrived pass
    // path: constructor-evaluated check plus a private throw helper (WO-081 P-504, Technique A)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-360 (<c>SK.00.SyncCryptoGateAndArgon2ConfinementLock</c>/WO-081/P-504): a fixture whose
    /// constructor invokes a check method and caches the result, and whose guarded member delegates
    /// to a private helper that constructs-and-throws <see cref="NotSupportedException"/>, must pass
    /// both <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> (including against
    /// <c>.ctor</c>) and <see cref="SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType"/>.
    /// </summary>
    /// <remarks>
    /// This shape was first modelled on <c>SharedKernel.Cryptography</c>'s runtime synchronous-provider
    /// gate. That gate no longer exists: the redesigned package (P-545) makes a synchronous service
    /// require an <c>ISynchronousEncryptionKeyProvider</c> by constructor type instead, which
    /// <see cref="SynchronousEncryption_RealTypes_RequireASynchronousKeyProviderByConstructorType"/>
    /// locks. The fixture is kept because it is this file's only coverage of <c>.ctor</c> resolution
    /// and of a throw centralized in a private helper.
    /// </remarks>
    [Fact]
    public void ConstructorCheckFixtures_CheckAndGuardPresent_BothAssertionsPass()
    {
        const string source = """
            namespace Fixture.ConstructorCheck
            {
                public static class FixturePolicyCheck
                {
                    public static bool IsAllowed(object dependency) => false;
                }

                // Compliant: the constructor evaluates and caches the check, and the guarded member
                // delegates to a private helper that constructs and throws NotSupportedException.
                public sealed class FixtureGuardedServicePresent
                {
                    private readonly bool _isAllowed;

                    public FixtureGuardedServicePresent(object dependency)
                    {
                        _isAllowed = FixturePolicyCheck.IsAllowed(dependency);
                    }

                    public void GuardedMember()
                    {
                        ThrowIfNotAllowed();
                    }

                    private void ThrowIfNotAllowed()
                    {
                        if (!_isAllowed)
                        {
                            throw new System.NotSupportedException("not allowed");
                        }
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ConstructorCheckPresent", source);
        var declaringType = assembly.GetType("Fixture.ConstructorCheck.FixtureGuardedServicePresent")!;
        var checkType = assembly.GetType("Fixture.ConstructorCheck.FixturePolicyCheck")!;

        var ctorInvokesCheck = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(declaringType, ".ctor", checkType, "IsAllowed");

        ctorInvokesCheck.Should().NotThrow(
            because: "the fixture's constructor genuinely calls FixturePolicyCheck.IsAllowed");

        var guardedMemberInvokesGuard = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType, "GuardedMember", declaringType, "ThrowIfNotAllowed");

        guardedMemberInvokesGuard.Should().NotThrow(
            because: "the fixture's GuardedMember genuinely calls the ThrowIfNotAllowed guard helper");

        var guardThrowsNotSupported = () =>
            SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType(
                declaringType, "ThrowIfNotAllowed", typeof(NotSupportedException));

        guardThrowsNotSupported.Should().NotThrow(
            because: "the fixture's ThrowIfNotAllowed guard genuinely constructs and throws " +
                     "NotSupportedException");
    }

    // ---------------------------------------------------------------------------
    // T-361 — AssertMethodBodyInvokesMethod contrived fire path: constructor check call removed
    // (WO-081 P-504, Technique A)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-361 (<c>SK.00.SyncCryptoGateAndArgon2ConfinementLock</c>/WO-081/P-504): an otherwise-identical
    /// fixture with the constructor's check call removed must fail
    /// <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> against <c>.ctor</c>.
    /// </summary>
    [Fact]
    public void ConstructorCheckFixtures_CheckCallRemoved_ThrowsNamingMethod()
    {
        const string source = """
            namespace Fixture.ConstructorCheck
            {
                public static class FixturePolicyCheck
                {
                    public static bool IsAllowed(object dependency) => false;
                }

                // Violation: the constructor no longer evaluates the check, silently leaving _isAllowed
                // at its default. The guard still throws here; the point of this fixture is that the
                // ABSENCE of the check call site is itself detected.
                public sealed class FixtureGuardedServiceCheckRemoved
                {
                    private readonly bool _isAllowed;

                    public FixtureGuardedServiceCheckRemoved(object dependency)
                    {
                    }

                    public void GuardedMember()
                    {
                        ThrowIfNotAllowed();
                    }

                    private void ThrowIfNotAllowed()
                    {
                        if (!_isAllowed)
                        {
                            throw new System.NotSupportedException("not allowed");
                        }
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ConstructorCheckRemoved", source);
        var declaringType = assembly.GetType("Fixture.ConstructorCheck.FixtureGuardedServiceCheckRemoved")!;
        var checkType = assembly.GetType("Fixture.ConstructorCheck.FixturePolicyCheck")!;

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(declaringType, ".ctor", checkType, "IsAllowed");

        act.Should()
            .Throw<InvalidOperationException>(
                because: "the fixture's constructor no longer calls FixturePolicyCheck.IsAllowed — the " +
                         "check call site was removed")
            .WithMessage("*.ctor*")
            .WithMessage("*IsAllowed*");
    }

    // ---------------------------------------------------------------------------
    // Real-assembly verification (GATING): SharedKernel.Cryptography secure defaults after the
    // P-545 redesign
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Locks that synchronous encryption can only be built over a synchronous key provider, by type:
    /// <see cref="SynchronousAesGcmEncryptionService"/>'s only constructor takes an
    /// <see cref="ISynchronousEncryptionKeyProvider"/>, that interface is a separate contract rather than a
    /// marker on <see cref="IEncryptionKeyProvider"/>, <see cref="ISymmetricEncryptionService"/> exposes no
    /// synchronous member, and neither the key-service provider nor the caching decorator claims to be
    /// synchronous.
    /// </summary>
    /// <remarks>
    /// Replaces the retired runtime gate (a constructor capability check and a <see cref="NotSupportedException"/>
    /// guard on every synchronous member). The guarantee it protected — a key-service provider is never
    /// driven from a synchronous call by blocking on it — now holds at compile time, so this test locks the
    /// type shapes that make it hold. Adding a synchronous overload to <see cref="ISymmetricEncryptionService"/>,
    /// widening the constructor to <see cref="IEncryptionKeyProvider"/>, or making a remote provider implement
    /// <see cref="ISynchronousEncryptionKeyProvider"/> each fails it.
    /// </remarks>
    [Fact]
    public void SynchronousEncryption_RealTypes_RequireASynchronousKeyProviderByConstructorType()
    {
        var constructors = typeof(SynchronousAesGcmEncryptionService).GetConstructors();
        constructors.Should().ContainSingle(
            because: "SynchronousAesGcmEncryptionService must offer exactly one way to be constructed");
        constructors[0].GetParameters().Select(p => p.ParameterType).Should().Equal(
            [typeof(ISynchronousEncryptionKeyProvider)],
            because: "a synchronous encryption service must be built over a synchronous key provider, " +
                     "never over an asynchronous one it would have to block on");

        typeof(IEncryptionKeyProvider).IsAssignableFrom(typeof(ISynchronousEncryptionKeyProvider)).Should().BeFalse(
            because: "ISynchronousEncryptionKeyProvider is a contract with its own members, not a marker " +
                     "any asynchronous provider could claim");

        typeof(ISymmetricEncryptionService).GetMethods()
            .Where(method => !IsValueTask(method.ReturnType))
            .Select(method => method.Name)
            .Should().BeEmpty(
                because: "ISymmetricEncryptionService is asynchronous only; synchronous callers use " +
                         "ISynchronousSymmetricEncryptionService");

        foreach (var remoteProvider in new[]
                 {
                     typeof(SharedKernel.Cryptography.KeyVault.Azure.AzureKeyVaultEncryptionKeyProvider),
                     typeof(CachedEncryptionKeyProvider),
                 })
        {
            typeof(ISynchronousEncryptionKeyProvider).IsAssignableFrom(remoteProvider).Should().BeFalse(
                because: $"{remoteProvider.Name} resolves keys asynchronously and must never be usable " +
                         "by the synchronous encryption service");
        }
    }

    /// <summary>
    /// Locks the RSA key-size floor and the async-only signing surface: <see cref="SigningKey.FromRsa"/> rejects
    /// any RSA key below <see cref="SigningKey.MinimumRsaKeySize"/> (2048) bits for every RSA algorithm, accepts
    /// a 2048-bit key, and <see cref="IAsymmetricSignatureService"/> exposes no synchronous member.
    /// </summary>
    /// <remarks>
    /// Executed rather than IL-inspected: the floor is a computed comparison against the key's own size, which
    /// only running the factory proves. The retired per-service <c>EnsureMinimumKeySize</c> check moved into
    /// this single construction path, so a key that passes it is the only kind any signing service can use.
    /// </remarks>
    [Fact]
    public void AsymmetricSigning_RealSigningKey_RejectsRsaKeysBelow2048Bits()
    {
        SigningKey.MinimumRsaKeySize.Should().BeGreaterThanOrEqualTo(2048);

        var rsaAlgorithms = new[]
        {
            SignatureAlgorithm.PS256, SignatureAlgorithm.PS384, SignatureAlgorithm.PS512,
            SignatureAlgorithm.RS256, SignatureAlgorithm.RS384, SignatureAlgorithm.RS512,
        };

        using (var weak = System.Security.Cryptography.RSA.Create(1024))
        {
            foreach (var algorithm in rsaAlgorithms)
            {
                var act = () => SigningKey.FromRsa("weak", weak, algorithm, ownsKey: false);
                act.Should().Throw<ArgumentException>(
                    because: $"a 1024-bit RSA key must be rejected for {algorithm}");
            }
        }

        using (var strong = System.Security.Cryptography.RSA.Create(2048))
        {
            using var key = SigningKey.FromRsa("strong", strong, SignatureAlgorithm.PS256, ownsKey: false);
            key.Algorithm.Should().Be(SignatureAlgorithm.PS256);
        }

        typeof(IAsymmetricSignatureService).GetMethods()
            .Where(method => !IsValueTask(method.ReturnType))
            .Select(method => method.Name)
            .Should().BeEmpty(because: "IAsymmetricSignatureService is asynchronous only");
    }

    /// <summary>
    /// Locks the HMAC key-length floor: <see cref="HmacSha256Signer"/> rejects keys shorter than
    /// <see cref="HmacSha256Signer.MinimumKeyLength"/> (32 bytes, the SHA-256 output size) on both signing and
    /// verification, and accepts a 32-byte key.
    /// </summary>
    [Fact]
    public void HmacSigning_RealHmacSha256Signer_RejectsKeysShorterThan32Bytes()
    {
        HmacSha256Signer.MinimumKeyLength.Should().BeGreaterThanOrEqualTo(32);

        var signer = new HmacSha256Signer();
        byte[] data = "payload"u8.ToArray();
        byte[] shortKey = new byte[HmacSha256Signer.MinimumKeyLength - 1];
        byte[] minimumKey = new byte[HmacSha256Signer.MinimumKeyLength];

        var sign = () => signer.Sign(data, shortKey);
        var verify = () => signer.Verify(data, new byte[32], shortKey);

        sign.Should().Throw<ArgumentException>(because: "a 31-byte HMAC key must be rejected when signing");
        verify.Should().Throw<ArgumentException>(because: "a 31-byte HMAC key must be rejected when verifying");
        signer.Verify(data, signer.Sign(data, minimumKey), minimumKey).Should().BeTrue();
    }

    /// <summary>
    /// Locks the one-way hashing cost bounds: <see cref="Pbkdf2Options"/> rejects an iteration count below its
    /// floor, and both <see cref="Pbkdf2OneWayHashAlgorithm"/> and <c>Argon2idOneWayHashAlgorithm</c> refuse a
    /// stored hash whose cost exceeds their ceiling before doing any work.
    /// </summary>
    /// <remarks>
    /// A stored hash can be written by an attacker. The ceiling cases use costs no implementation could finish
    /// (<see cref="int.MaxValue"/> PBKDF2 iterations; <see cref="int.MaxValue"/> KiB of Argon2 memory), so if a
    /// ceiling were removed this test would hang or run out of memory rather than pass.
    /// </remarks>
    [Fact]
    public void OneWayHashing_RealAlgorithms_BoundStoredCostsBeforeDeriving()
    {
        Pbkdf2Options.MinimumIterations.Should().BeGreaterThanOrEqualTo(100_000);
        IsValid(new Pbkdf2Options { Iterations = Pbkdf2Options.MinimumIterations - 1 }).Should().BeFalse(
            because: "an iteration count below the floor must fail options validation");
        IsValid(new Pbkdf2Options()).Should().BeTrue(because: "the default iteration count must be valid");

        byte[] salt = new byte[16];
        byte[] hash = new byte[32];
        byte[] secret = "secret"u8.ToArray();

        var pbkdf2 = new Pbkdf2OneWayHashAlgorithm(new StaticOptionsMonitor<Pbkdf2Options>(new Pbkdf2Options()));
        foreach (var iterations in new[] { Pbkdf2Options.MaximumIterations + 1, int.MaxValue })
        {
            var stored = new PhcHashString(
                Pbkdf2OneWayHashAlgorithm.Id,
                version: null,
                [new("i", iterations.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                salt,
                hash);

            pbkdf2.Verify(stored, secret).Should().BeFalse(
                because: $"a stored PBKDF2 hash claiming {iterations} iterations exceeds the verify ceiling");
        }

        var argon2 = new SharedKernel.Cryptography.Argon2.Argon2idOneWayHashAlgorithm(
            new StaticOptionsMonitor<SharedKernel.Cryptography.Argon2.Argon2Options>(
                new SharedKernel.Cryptography.Argon2.Argon2Options()));
        var argon2Stored = new PhcHashString(
            SharedKernel.Cryptography.Argon2.Argon2idOneWayHashAlgorithm.Id,
            version: 19,
            [new("m", int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("t", "2"), new("p", "1")],
            salt,
            hash);

        argon2.Verify(argon2Stored, secret).Should().BeFalse(
            because: "a stored Argon2id hash claiming more memory than the ceiling must be refused before deriving");
    }

    private static bool IsValueTask(Type type) =>
        type == typeof(ValueTask)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));

    private static bool IsValid(object options) =>
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options,
            new System.ComponentModel.DataAnnotations.ValidationContext(options),
            validationResults: null,
            validateAllProperties: true);

    /// <summary>An <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> that always returns one value.</summary>
    private sealed class StaticOptionsMonitor<TOptions>(TOptions value) : Microsoft.Extensions.Options.IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = value;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    // ---------------------------------------------------------------------------
    // Fixture compilation helper
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> to an in-memory assembly, writes it to a uniquely-named
    /// temp file, and loads it via <see cref="Assembly.LoadFrom(string)"/> so
    /// <see cref="Assembly.Location"/> resolves to a real path — required because
    /// <see cref="SecureDefaultsAssertion"/> loads assemblies via
    /// <c>Mono.Cecil.AssemblyDefinition.ReadAssembly(assembly.Location)</c>. Same technique as
    /// <c>SecurityArchitectureRulesTests.CompileInMemory</c>/<c>LoggingEventIdIntegrityAssertionTests.CompileInMemory</c>.
    /// Explicitly pins <see cref="LanguageVersion.Latest"/> so target-typed collection expressions
    /// (<c>["PS256", "ES256"]</c>) parse identically to the real, shipped <c>12.Security</c>
    /// source, which compiles under this repo's own <c>LangVersion=latest</c> project setting.
    /// </summary>
    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            // Provides IServiceCollection plus both ServiceCollectionServiceExtensions
            // (AddSingleton) and ServiceCollectionDescriptorExtensions (TryAddSingleton) — both
            // live in this one Abstractions-only package — needed to compile T-332–T-334's
            // AssertMethodBodyRegistersSingleton fixture assemblies.
            MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
            // Provides OptionsBuilder<T>/AddOptions<T>/ValidateOnStart() — needed to compile
            // T-338/T-339's AssertMethodBodyInvokesMethod fixture assemblies (WO-065 P-437,
            // SK.00.CacheEncryptionAndRedisValidationLock, Technique B).
            MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
        }

        stream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(tempPath, stream.ToArray());

        return Assembly.LoadFrom(tempPath);
    }
}
