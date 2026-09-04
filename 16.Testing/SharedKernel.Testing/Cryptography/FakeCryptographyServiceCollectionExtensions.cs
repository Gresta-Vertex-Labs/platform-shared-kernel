using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Testing</c>'s fake
/// <c>SharedKernel.Cryptography</c> test doubles.
/// </summary>
/// <remarks>
/// Named distinctly from the real <see cref="CryptographyServiceCollectionExtensions"/> to avoid any
/// static-member ambiguity, since this class deliberately reuses that real class's
/// <see cref="CryptographyServiceCollectionExtensions.RsaSignatureServiceKey"/>/
/// <see cref="CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey"/> keyed-service key
/// constants rather than redeclaring fake-only key strings.
/// </remarks>
public static class FakeCryptographyServiceCollectionExtensions
{
    /// <summary>
    /// Registers every <c>SharedKernel.Cryptography</c> fake as a singleton:
    /// <see cref="IOneWayHasher"/> → <see cref="FakeOneWayHasher"/>,
    /// <see cref="IEncryptionKeyProvider"/> → <see cref="FakeEncryptionKeyProvider"/>,
    /// <see cref="ISymmetricEncryptionService"/> → <see cref="FakeSymmetricEncryptionService"/>,
    /// <see cref="IAsymmetricKeyProvider"/> → <see cref="FakeAsymmetricKeyProvider"/>,
    /// <see cref="IAsymmetricSignatureService"/> → <see cref="FakeAsymmetricSignatureService"/>
    /// (unkeyed default plus both
    /// <see cref="CryptographyServiceCollectionExtensions.RsaSignatureServiceKey"/>/
    /// <see cref="CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey"/>-keyed
    /// singletons), <see cref="IHmacSigner"/> → <see cref="FakeHmacSigner"/>,
    /// <see cref="ISecureRandomGenerator"/> → <see cref="FakeSecureRandomGenerator"/> (non-seeded
    /// constructor — genuinely random by default; register a seeded instance manually for
    /// deterministic tokens), <see cref="IContentHasher"/> → <see cref="FakeContentHasher"/>, and
    /// <see cref="IEnvelopeEncryptionProvider"/> → <see cref="FakeEnvelopeEncryptionProvider"/>
    /// (P-450/WO-068).
    /// </summary>
    /// <remarks>
    /// Deliberately diverges from
    /// <see cref="CryptographyServiceCollectionExtensions.AddSharedKernelCryptography"/>: production
    /// intentionally does NOT register <see cref="IEncryptionKeyProvider"/>/
    /// <see cref="IAsymmetricKeyProvider"/>/<see cref="IEnvelopeEncryptionProvider"/>
    /// (consumer-supplied by design) — this fake bundle DOES, since a test wanting
    /// <see cref="AddFakeCryptography"/> to work end-to-end with zero extra wiring needs some
    /// functioning key material.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFakeCryptography(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOneWayHasher, FakeOneWayHasher>();
        services.AddSingleton<IEncryptionKeyProvider, FakeEncryptionKeyProvider>();
        services.AddSingleton<ISymmetricEncryptionService, FakeSymmetricEncryptionService>();
        services.AddSingleton<IAsymmetricKeyProvider, FakeAsymmetricKeyProvider>();
        services.AddSingleton<IHmacSigner, FakeHmacSigner>();
        services.AddSingleton<ISecureRandomGenerator, FakeSecureRandomGenerator>();
        services.AddSingleton<IContentHasher, FakeContentHasher>();
        services.AddSingleton<IEnvelopeEncryptionProvider, FakeEnvelopeEncryptionProvider>();

        services.AddSingleton<IAsymmetricSignatureService, FakeAsymmetricSignatureService>();
        services.AddKeyedSingleton<IAsymmetricSignatureService, FakeAsymmetricSignatureService>(
            CryptographyServiceCollectionExtensions.RsaSignatureServiceKey);
        services.AddKeyedSingleton<IAsymmetricSignatureService, FakeAsymmetricSignatureService>(
            CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey);

        return services;
    }
}
