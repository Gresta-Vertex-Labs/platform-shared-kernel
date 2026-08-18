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
