using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Testing.Cryptography;

/// <summary>Registers every <c>SharedKernel.Cryptography</c> test double.</summary>
public static class FakeCryptographyServiceCollectionExtensions
{
    /// <summary>
    /// Registers fakes for every cryptography contract, removing earlier registrations of those contracts first. One
    /// <see cref="FakeEncryptionKeyProvider"/> backs both encryption services, and one
    /// <see cref="FakeSigningKeyProvider"/> backs signing, so tests can resolve them to rotate keys.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddFakeCryptography(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IOneWayHasher>();
        services.AddSingleton<IOneWayHasher, FakeOneWayHasher>();
        services.RemoveAll<ISecureRandomGenerator>();
        services.AddSingleton<ISecureRandomGenerator, FakeSecureRandomGenerator>();
        services.RemoveAll<IContentHasher>();
        services.AddSingleton<IContentHasher, FakeContentHasher>();
        services.RemoveAll<IHmacSigner>();
        services.AddSingleton<IHmacSigner, FakeHmacSigner>();

        services.TryAddSingleton<FakeEncryptionKeyProvider>();
        services.RemoveAll<IEncryptionKeyProvider>();
        services.AddSingleton<IEncryptionKeyProvider>(sp => sp.GetRequiredService<FakeEncryptionKeyProvider>());
        services.RemoveAll<ISynchronousEncryptionKeyProvider>();
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(sp => sp.GetRequiredService<FakeEncryptionKeyProvider>());
        services.TryAddSingleton(sp => new FakeSymmetricEncryptionService(sp.GetRequiredService<FakeEncryptionKeyProvider>()));
        services.RemoveAll<ISymmetricEncryptionService>();
        services.AddSingleton<ISymmetricEncryptionService>(sp => sp.GetRequiredService<FakeSymmetricEncryptionService>());
        services.RemoveAll<ISynchronousSymmetricEncryptionService>();
        services.AddSingleton<ISynchronousSymmetricEncryptionService>(sp => sp.GetRequiredService<FakeSymmetricEncryptionService>());

        services.TryAddSingleton<FakeEnvelopeEncryptionProvider>();
        services.RemoveAll<IEnvelopeEncryptionProvider>();
        services.AddSingleton<IEnvelopeEncryptionProvider>(sp => sp.GetRequiredService<FakeEnvelopeEncryptionProvider>());
        services.RemoveAll<IEnvelopeEncryptionService>();
        services.AddSingleton<IEnvelopeEncryptionService, EnvelopeEncryptionService>();

        services.TryAddSingleton<FakeSigningKeyProvider>();
        services.RemoveAll<ISigningKeyProvider>();
        services.AddSingleton<ISigningKeyProvider>(sp => sp.GetRequiredService<FakeSigningKeyProvider>());
        services.TryAddSingleton(sp => new FakeAsymmetricSignatureService(sp.GetRequiredService<FakeSigningKeyProvider>()));
        services.RemoveAll<IAsymmetricSignatureService>();
        services.AddSingleton<IAsymmetricSignatureService>(sp => sp.GetRequiredService<FakeAsymmetricSignatureService>());

        services.TryAddSingleton<FakeTotpReplayGuard>();
        services.RemoveAll<ITotpReplayGuard>();
        services.AddSingleton<ITotpReplayGuard>(sp => sp.GetRequiredService<FakeTotpReplayGuard>());

        return services;
    }
}
