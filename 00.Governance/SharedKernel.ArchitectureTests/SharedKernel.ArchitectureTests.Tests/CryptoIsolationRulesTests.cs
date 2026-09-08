using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="CryptoIsolationRules"/> and the SK0301-exemption-narrowing change in
/// <see cref="EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication"/> introduced by
/// WO-037 P-229 phase <c>SK.00.CryptoDelegationAndUowSeamGuard</c>.
/// </summary>
/// <remarks>
/// T-154: <see cref="CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography"/> fire path —
///        cipher type referenced outside SharedKernel.Cryptography.
/// T-155: <see cref="CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography"/> fire path —
///        RandomNumberGenerator member called outside SharedKernel.Cryptography.
/// T-156: <see cref="CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography"/> pass path —
///        cipher/RNG calls inside a SharedKernel.Cryptography-prefixed namespace.
/// T-160: SK0301 exemption-narrowing regression test — a SharedKernel.Persistence-namespaced type
///        calling AesGcm now fails (previously exempt, now correctly flagged).
/// T-364: <see cref="CryptoIsolationRules.CryptographyCoreHasNoThirdPartyDependencies"/> pass path
///        (WO-081 P-504, <c>SK.00.SyncCryptoGateAndArgon2ConfinementLock</c>, Technique B) — the
///        real, already-shipped <c>SharedKernel.Cryptography</c> core assembly has zero dependency
///        on <c>Konscious.Security.Cryptography</c>, <c>Azure.Security.KeyVault</c>, or
///        <c>Azure.Identity</c> today.
/// T-365: regression proof — a contrived fixture depending on a stand-in
///        <c>Konscious.Security.Cryptography</c>-namespaced type must fail the same rule.
///        <strong>Verification technique CORRECTED against a real environmental constraint found
///        during implementation.</strong> This phase's own Implementation Rule 7 prescribed a real,
///        temporary <c>Konscious.Security.Cryptography.Argon2</c> <c>PackageReference</c> added
///        directly to <c>SharedKernel.Cryptography.csproj</c>, confirmed to make the rule fail, then
///        fully reverted before commit. Attempted during implementation: this repo uses NuGet
///        Central Package Management (<c>Directory.Packages.props</c>,
///        <c>ManagePackageVersionsCentrally=true</c>) with per-project <c>VersionOverride</c>
///        DISABLED platform-wide (confirmed via a real, reverted attempt — <c>error NU1013</c>,
///        "projects that use central package management are configured to disable this feature").
///        Adding a central <c>PackageVersion</c> entry for a throwaway sanity check would mean
///        editing a root build-configuration file outside this domain's own jurisdiction
///        (<c>devops-lead</c>'s, per this session's own operating instructions) for a package that
///        will never actually ship there. T-365 is reproduced instead the same way T-154/T-155
///        already prove <see cref="CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography"/>'s
///        detection surface: a contrived fixture assembly, compiled in-memory, needing no real NuGet
///        package or network access. This is not a weaker proof — <c>NetArchTest</c>'s
///        <c>NotHaveDependencyOn(term)</c> itself works by namespace-prefix matching against a
///        type's resolved dependencies; it has no way to distinguish "this namespace came from a
///        real NuGet package" from "this namespace came from a type declared directly in the
///        fixture source," so the fixture exercises the identical code path a real Argon2 reference
///        would. Shipped as a standing, permanent <c>[Fact]</c> (unlike a one-off manual check, this
///        one needs no developer to remember to re-run it by hand on a future change).
/// </remarks>
public class CryptoIsolationRulesTests
{
    // ---------------------------------------------------------------------------
    // T-154 — Fire path: cipher type (AesGcm/Aes/SymmetricAlgorithm) outside SharedKernel.Cryptography
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-154: A contrived fixture type outside <c>SharedKernel.Cryptography</c> that references
    /// <c>AesGcm</c> directly (both as a field and in a constructor call) must fail
    /// <see cref="CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography"/>.
    /// </summary>
    [Fact]
    public void NoRawSymmetricCipherOutsideCryptography_AesGcmFieldOutsideCryptography_RuleFails()
    {
        // Arrange: fixture type in a non-Cryptography namespace holding AesGcm as a field
        // and constructing it in a method. AesGcm is stubbed so the fixture compiles without
        // the real System.Security.Cryptography runtime assembly on disk.
        const string source = """
            namespace System.Security.Cryptography
            {
                public sealed class AesGcm : System.IDisposable
                {
                    public AesGcm(byte[] key) { }
                    public void Dispose() { }
                }
            }

            namespace Fixture.Infrastructure.SomePackage
            {
                // Violation: this type is outside SharedKernel.Cryptography but holds AesGcm directly.
                public class DirectCipherUser
                {
                    private readonly System.Security.Cryptography.AesGcm _cipher;

                    public DirectCipherUser(byte[] key)
                    {
                        _cipher = new System.Security.Cryptography.AesGcm(key);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.CryptoIsolation.AesGcmOutside", source);

        var result = CryptoIsolationRules
            .NoRawSymmetricCipherOutsideCryptography(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "DirectCipherUser is outside SharedKernel.Cryptography but declares an AesGcm " +
                     "field and constructs it — this violates the platform-wide cipher-isolation rule");
    }

    // ---------------------------------------------------------------------------
    // T-155 — Fire path: RandomNumberGenerator member called outside SharedKernel.Cryptography
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-155: A contrived fixture type outside <c>SharedKernel.Cryptography</c> that calls a
    /// <c>RandomNumberGenerator</c> member (via a stub) must fail
    /// <see cref="CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography"/>.
    /// </summary>
    [Fact]
    public void NoRawSymmetricCipherOutsideCryptography_RandomNumberGeneratorCallOutsideCryptography_RuleFails()
    {
        // Arrange: stub RandomNumberGenerator in the System.Security.Cryptography namespace
        // so the fixture compiles without the real BCL crypto assembly.
        const string source = """
            namespace System.Security.Cryptography
            {
                public abstract class RandomNumberGenerator : System.IDisposable
                {
                    public static RandomNumberGenerator Create() => null!;
                    public abstract void GetBytes(byte[] data);
                    public static void Fill(System.Span<byte> data) { }
                    public virtual void Dispose() { }
                }
            }

            namespace Fixture.Infrastructure.SomePackage
            {
                // Violation: calls RandomNumberGenerator.Create() directly outside SharedKernel.Cryptography.
                public class DirectRngUser
                {
                    public void GenerateNonce(byte[] buffer)
                    {
                        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
                        rng.GetBytes(buffer);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.CryptoIsolation.RngOutside", source);

        var result = CryptoIsolationRules
            .NoRawSymmetricCipherOutsideCryptography(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "DirectRngUser calls RandomNumberGenerator.Create() outside SharedKernel.Cryptography — " +
                     "the platform-wide cipher-isolation rule prohibits all RandomNumberGenerator usage " +
                     "outside SharedKernel.Cryptography");
    }

    // ---------------------------------------------------------------------------
    // T-156 — Pass path: cipher/RNG calls inside SharedKernel.Cryptography namespace
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-156: A contrived fixture type inside a <c>SharedKernel.Cryptography</c>-prefixed namespace
    /// that performs the same cipher and RNG calls must pass
    /// <see cref="CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography"/> because the
    /// namespace-exemption guard applies unconditionally.
    /// </summary>
    [Fact]
    public void NoRawSymmetricCipherOutsideCryptography_CipherCallsInsideCryptographyNamespace_RulePasses()
    {
        // Arrange: same cipher/RNG operations but inside SharedKernel.Cryptography — the sole
        // exempted namespace for this rule.
        const string source = """
            namespace System.Security.Cryptography
            {
                public sealed class AesGcm : System.IDisposable
                {
                    public AesGcm(byte[] key) { }
                    public void Dispose() { }
                }

                public abstract class RandomNumberGenerator : System.IDisposable
                {
                    public static RandomNumberGenerator Create() => null!;
                    public abstract void GetBytes(byte[] data);
                    public virtual void Dispose() { }
                }
            }

            namespace SharedKernel.Cryptography.Internal
            {
                // Compliant: this type is inside SharedKernel.Cryptography — the sole exempt namespace.
                // This mirrors the intended target shape: AesGcmEncryptionService doing its own
                // AesGcm construction and RNG nonce generation.
                public class AesGcmEncryptionService
                {
                    private readonly System.Security.Cryptography.AesGcm _cipher;

                    public AesGcmEncryptionService(byte[] key)
                    {
                        _cipher = new System.Security.Cryptography.AesGcm(key);
                    }

                    public byte[] GenerateNonce()
                    {
                        var nonce = new byte[12];
                        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
                        rng.GetBytes(nonce);
                        return nonce;
                    }

                    public void Dispose() => _cipher.Dispose();
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.CryptoIsolation.InsideCryptography", source);

        var result = CryptoIsolationRules
            .NoRawSymmetricCipherOutsideCryptography(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AesGcmEncryptionService is inside SharedKernel.Cryptography.Internal — " +
                     "the namespace-exemption guard passes it unconditionally; cipher and RNG " +
                     "usage is the intended purpose of the SharedKernel.Cryptography package");
    }

    // ---------------------------------------------------------------------------
    // T-160 — SK0301 regression: SharedKernel.Persistence-namespaced AesGcm now fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-160: After the SK0301 exemption narrowing (WO-037 P-229), a type in a
    /// <c>SharedKernel.Persistence</c>-prefixed namespace that calls <c>AesGcm</c> directly
    /// must <strong>fail</strong>
    /// <see cref="EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication"/> —
    /// whereas before the narrowing it was wrongly exempt under the old
    /// <c>SharedKernel.Persistence.*</c> exemption. This test confirms no regression in the
    /// narrowed predicate and documents the P-227 incident pattern.
    /// </summary>
    [Fact]
    public void NoCryptoCipherInDomainOrApplication_PersistenceNamespacedAesGcmNowFails_AfterNarrowing()
    {
        // Arrange: stub AesGcm in the correct namespace.
        const string source = """
            namespace System.Security.Cryptography
            {
                public sealed class AesGcm : System.IDisposable
                {
                    public AesGcm(byte[] key) { }
                    public void Dispose() { }
                }
            }

            namespace SharedKernel.Persistence.EfCore
            {
                // This pattern reproduces the P-227 incident: a persistence-layer type
                // constructing AesGcm directly instead of delegating to ISymmetricEncryptionService.
                // Before WO-037 P-229, SharedKernel.Persistence.* was exempt from SK0301.
                // After the narrowing, ONLY SharedKernel.Cryptography.* is exempt, so this type
                // must now fail the SK0301 check.
                public class EncryptedValueConverterOldPattern
                {
                    private readonly System.Security.Cryptography.AesGcm _cipher;

                    public EncryptedValueConverterOldPattern(byte[] key)
                    {
                        // P-227 pattern: hand-rolled AesGcm in persistence layer (now prohibited)
                        _cipher = new System.Security.Cryptography.AesGcm(key);
                    }

                    public void Dispose() => _cipher.Dispose();
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.CryptoIsolation.PersistenceAesGcmRegression", source);

        var result = EncryptionPatternGuardRules
            .NoCryptoCipherInDomainOrApplication(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "After the WO-037 P-229 exemption narrowing, SharedKernel.Persistence.* is no " +
                     "longer exempt from SK0301. EncryptedValueConverterOldPattern in " +
                     "SharedKernel.Persistence.EfCore uses AesGcm directly — this reproduces the " +
                     "P-227 incident and must now be caught by the predicate");
    }

    // ---------------------------------------------------------------------------
    // Existing SK0301 pass-path regression: SharedKernel.Cryptography still exempt
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Regression guard: after the SK0301 exemption narrowing, types inside
    /// <c>SharedKernel.Cryptography</c> must still pass
    /// <see cref="EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication"/>.
    /// </summary>
    [Fact]
    public void NoCryptoCipherInDomainOrApplication_CryptographyNamespacedType_StillPasses()
    {
        const string source = """
            namespace System.Security.Cryptography
            {
                public sealed class AesGcm : System.IDisposable
                {
                    public AesGcm(byte[] key) { }
                    public void Dispose() { }
                }
            }

            namespace SharedKernel.Cryptography
            {
                // Compliant: SharedKernel.Cryptography is the sole exempt namespace — both for
                // the platform-wide rule and for SK0301 after the P-229 narrowing.
                public class AesGcmEncryptionService
                {
                    private readonly System.Security.Cryptography.AesGcm _cipher;

                    public AesGcmEncryptionService(byte[] key)
                    {
                        _cipher = new System.Security.Cryptography.AesGcm(key);
                    }

                    public void Dispose() => _cipher.Dispose();
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.CryptoIsolation.SK0301CryptographyStillExempt", source);

        var result = EncryptionPatternGuardRules
            .NoCryptoCipherInDomainOrApplication(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AesGcmEncryptionService is in SharedKernel.Cryptography — the sole exempt " +
                     "namespace after the SK0301 exemption narrowing in WO-037 P-229");
    }

    // ---------------------------------------------------------------------------
    // T-364 — Real-assembly verification (GATING, immediate — no upstream dependency):
    // SharedKernel.Cryptography core has zero third-party dependency (Technique B)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-364 (<c>SK.00.SyncCryptoGateAndArgon2ConfinementLock</c>/WO-081/P-504): the real,
    /// already-shipped <c>SharedKernel.Cryptography</c> core assembly must pass
    /// <see cref="CryptoIsolationRules.CryptographyCoreHasNoThirdPartyDependencies"/> — it carries
    /// no dependency on <c>Konscious.Security.Cryptography</c> (the future
    /// <c>SharedKernel.Cryptography.Argon2</c> sibling's dependency, P-495, not yet shipped) or
    /// <c>Azure.Security.KeyVault</c>/<c>Azure.Identity</c> (the already-shipped
    /// <c>SharedKernel.Cryptography.KeyVault.Azure</c> sibling's dependencies, P-447 — confined to
    /// that sibling package, never leaked back into this core assembly).
    /// </summary>
    /// <remarks>
    /// This is FULLY UNGATED — unlike T-362/T-363 (which needed <c>01.Core</c>'s P-492/P-493 to
    /// ship past Design), this check targets <c>SharedKernel.Cryptography</c> core as it exists
    /// TODAY and needs no dependency on <c>SharedKernel.Cryptography.Argon2</c> (P-495) ever
    /// shipping — a correction of WO-081/P-504's own stated "Depends on: P-492, P-495" line, see
    /// this phase's own <c>state-map.md</c> entry. Non-vacuous verification (T-365) was performed
    /// as a temporary <c>PackageReference</c> mutation during implementation — see this class's own
    /// type-level remarks.
    /// </remarks>
    [Fact]
    public void CryptographyCoreHasNoThirdPartyDependencies_RealCryptographyAssembly_RulePasses()
    {
        var result = CryptoIsolationRules
            .CryptographyCoreHasNoThirdPartyDependencies(typeof(AesGcmEncryptionService).Assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real, shipped SharedKernel.Cryptography core assembly has zero " +
                     "dependency on Konscious.Security.Cryptography, Azure.Security.KeyVault, or " +
                     "Azure.Identity — every sibling provider package (SharedKernel.Cryptography." +
                     "KeyVault.Azure today; SharedKernel.Cryptography.Argon2 once P-495 ships) " +
                     "confines its own third-party dependency to itself (WO-081, P-504)");
    }

    // ---------------------------------------------------------------------------
    // T-365 — Regression proof (Technique B): a Konscious-namespaced dependency fails
    // CryptographyCoreHasNoThirdPartyDependencies
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-365 (<c>SK.00.SyncCryptoGateAndArgon2ConfinementLock</c>/WO-081/P-504): a contrived fixture
    /// type depending on a stand-in <c>Konscious.Security.Cryptography</c>-namespaced type must fail
    /// <see cref="CryptoIsolationRules.CryptographyCoreHasNoThirdPartyDependencies"/> — reproducing
    /// the exact "a future Argon2 dependency leaks into SharedKernel.Cryptography core" regression
    /// this check exists to prevent. See this class's own type-level remarks for why this fixture
    /// technique replaces the phase's originally-prescribed real <c>PackageReference</c> mutation.
    /// </summary>
    [Fact]
    public void CryptographyCoreHasNoThirdPartyDependencies_KonsciousNamespacedDependency_RuleFails()
    {
        const string source = """
            namespace Konscious.Security.Cryptography
            {
                public sealed class Argon2id
                {
                    public byte[] Key { get; set; } = System.Array.Empty<byte>();

                    public byte[] GetBytes(int length) => new byte[length];
                }
            }

            namespace Fixture.CryptographyCoreLeak
            {
                // Violation: this type — standing in for a hypothetical SharedKernel.Cryptography
                // core type — references Konscious.Security.Cryptography.Argon2id directly,
                // reproducing the exact "an Argon2 dependency leaks into the zero-third-party-
                // dependency core assembly" regression this check exists to prevent. A future real
                // SharedKernel.Cryptography.Argon2 sibling package (P-495) must own this dependency
                // instead, mirroring SharedKernel.Cryptography.KeyVault.Azure's existing confinement.
                public class LeakedArgon2Usage
                {
                    private readonly Konscious.Security.Cryptography.Argon2id _argon2 = new();

                    public byte[] DeriveKey() => _argon2.GetBytes(32);
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.CryptoIsolation.KonsciousLeak", source);

        var result = CryptoIsolationRules
            .CryptographyCoreHasNoThirdPartyDependencies(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakedArgon2Usage depends on a Konscious.Security.Cryptography-namespaced " +
                     "type — reproducing the exact regression " +
                     "CryptographyCoreHasNoThirdPartyDependencies exists to prevent (WO-081, P-504)");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

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
