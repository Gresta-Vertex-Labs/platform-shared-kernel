using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    /// (expected <c>"Offline"</c>) and confirms both hardened defaults hold.
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
                optionsType, "RevocationMode", "Offline");

        allowedCertificateTypesAct.Should().NotThrow(
            because: "the real, shipped MtlsAuthenticationOptions.AllowedCertificateTypes " +
                     "defaults to CertificateTypes.Chained (WO-060, C-39)");
        revocationModeAct.Should().NotThrow(
            because: "the real, shipped MtlsAuthenticationOptions.RevocationMode defaults to " +
                     "X509RevocationMode.Offline (WO-060, C-39)");
    }

    // ---------------------------------------------------------------------------
    // T-310 — Real-assembly verification (GATING): SharedKernel.Security.Oidc
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-310: Re-points
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes"/> at the
    /// real, shipped <c>SecurityOptions.JwtOptions.ValidAlgorithms</c> and
    /// <c>DpopOptions.ValidAlgorithms</c> and confirms both hardened allowlists hold.
    /// </summary>
    /// <remarks>
    /// Originally tracked as a Cross-Domain Dependency pending <c>12.Security</c> P-387 — CONFIRMED
    /// RESOLVED on disk before this phase's implementation session:
    /// <c>SharedKernel.Security.Oidc</c> is packed at <c>4.0.0</c> and ships C-40/C-41's
    /// <c>["PS256", "ES256"]</c> allowlists. Non-vacuous: T-307/T-308's contrived fixtures prove
    /// the identical detection technique correctly fires when a forbidden value IS present — this
    /// test proves the real types' actual resolved defaults contain none of them, not merely that
    /// the scan runs without error.
    /// </remarks>
    [Fact]
    public void AssertStringCollectionPropertyDefaultExcludes_RealOidcAllowlists_HardenedDefaultsHold()
    {
        string[] forbiddenValues = ["none", "HS256", "HS384", "HS512"];

        var jwtAlgorithmsAct = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes(
                typeof(SharedKernel.Security.Oidc.Options.SecurityOptions.JwtOptions),
                "ValidAlgorithms",
                forbiddenValues);

        var dpopAlgorithmsAct = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes(
                typeof(SharedKernel.Security.Oidc.Dpop.DpopOptions),
                "ValidAlgorithms",
                forbiddenValues);

        jwtAlgorithmsAct.Should().NotThrow(
            because: "the real, shipped SecurityOptions.JwtOptions.ValidAlgorithms defaults to " +
                     "[\"PS256\", \"ES256\"] — the FAPI 2.0 baseline (WO-060, C-40)");
        dpopAlgorithmsAct.Should().NotThrow(
            because: "the real, shipped DpopOptions.ValidAlgorithms defaults to " +
                     "[\"PS256\", \"ES256\"] — the same FAPI 2.0 baseline (WO-060, C-41)");
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
    /// T-317: Re-points
    /// <see cref="SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals"/> at the
    /// real, shipped <c>TenantResolutionOptions.StrategyOrder</c> and confirms the corrected
    /// <c>[Claim, Header, Database]</c> order holds.
    /// </summary>
    /// <remarks>
    /// Originally tracked as a Cross-Domain Dependency pending <c>13.ServiceDefaults</c> P-393 —
    /// CONFIRMED RESOLVED on disk before this phase's implementation session:
    /// <c>SharedKernel.MultiTenancy</c> ships C-49's corrected default. Non-vacuous: verified by a
    /// temporary sanity check during implementation — asserting the deliberately-wrong
    /// <c>["Header", "Claim", "Database"]</c> order against this exact real type, confirmed to
    /// fail with the same message shape T-312's contrived fixture produces, then removed before
    /// commit — this test proves the real type's actual resolved default, not merely that the
    /// scan runs without error.
    /// </remarks>
    [Fact]
    public void AssertStringCollectionPropertyDefaultEquals_RealTenantResolutionOptions_HardenedOrderHolds()
    {
        var optionsType = typeof(SharedKernel.MultiTenancy.Resolution.TenantResolutionOptions);

        var act = () =>
            SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals(
                optionsType, "StrategyOrder", ["Claim", "Header", "Database"]);

        act.Should().NotThrow(
            because: "the real, shipped TenantResolutionOptions.StrategyOrder defaults to " +
                     "[\"Claim\", \"Header\", \"Database\"] — the corrected, secure order " +
                     "(WO-061, C-49)");
    }

    // ---------------------------------------------------------------------------
    // T-318 — Real-assembly verification (GATING): SharedKernel.ServiceDefaults
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-318: Re-points <see cref="SecureDefaultsAssertion.AssertMethodBodyInvokesMethod"/> at the
    /// real, shipped <c>MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate</c>
    /// registration method and confirms it still calls
    /// <c>ServiceDefaultsLog.ForwardedHeaderTrustBoundaryUnconfigured</c>.
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
    /// </remarks>
    [Fact]
    public void AssertMethodBodyInvokesMethod_RealMtlsForwardedHeaderExtensions_WarningCallSiteHolds()
    {
        var declaringType =
            typeof(SharedKernel.ServiceDefaults.Security.MtlsForwardedHeaderExtensions);

        // ServiceDefaultsLog is `internal` to SharedKernel.ServiceDefaults — no
        // InternalsVisibleTo grant exists (or should exist) to this governance test project, so
        // `typeof(...)` cannot name it directly. Assembly.GetType(string) resolves a Type object
        // by name regardless of accessibility — this helper only ever compares the resolved
        // Type's FullName against Mono.Cecil's TypeReference.FullName, never invokes a member
        // through it, so no accessibility violation occurs at runtime either.
        var calleeDeclaringType =
            declaringType.Assembly.GetType("SharedKernel.ServiceDefaults.Logging.ServiceDefaultsLog")
            ?? throw new InvalidOperationException(
                "Could not resolve SharedKernel.ServiceDefaults.Logging.ServiceDefaultsLog via " +
                "Assembly.GetType — has it been renamed or moved?");

        var act = () =>
            SecureDefaultsAssertion.AssertMethodBodyInvokesMethod(
                declaringType,
                "AddMtlsForwardedHeaderCertificate",
                calleeDeclaringType,
                "ForwardedHeaderTrustBoundaryUnconfigured");

        act.Should().NotThrow(
            because: "the real, shipped AddMtlsForwardedHeaderCertificate's PostConfigure lambda " +
                     "still calls ServiceDefaultsLog.ForwardedHeaderTrustBoundaryUnconfigured " +
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
    /// technique for <c>ServiceDefaultsLog</c> — this helper only ever compares
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
