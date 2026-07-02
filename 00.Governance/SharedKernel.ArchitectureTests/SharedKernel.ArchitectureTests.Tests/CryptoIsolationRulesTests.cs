using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
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
