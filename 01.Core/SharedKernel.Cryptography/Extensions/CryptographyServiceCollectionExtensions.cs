using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Cryptography.Extensions;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Cryptography</c> services.
/// </summary>
public static class CryptographyServiceCollectionExtensions
{
    /// <summary>
    /// The keyed-service key under which <see cref="RsaSignatureService"/> is registered for
    /// <see cref="IAsymmetricSignatureService"/>.
    /// </summary>
    public const string RsaSignatureServiceKey = "Rsa";

    /// <summary>
    /// The keyed-service key under which <see cref="EcdsaSignatureService"/> is registered for
    /// <see cref="IAsymmetricSignatureService"/>.
    /// </summary>
    public const string EcdsaSignatureServiceKey = "Ecdsa";

    /// <summary>
    /// Registers <see cref="CryptographyOptions"/> (validated, eagerly checked at startup via
    /// <c>ValidateOnStart()</c>) and the nine stateless, thread-safe cryptographic services —
    /// <see cref="IOneWayHasher"/>, <see cref="ISymmetricEncryptionService"/>,
    /// <see cref="IAsymmetricSignatureService"/> (both RSA and ECDSA variants, see remarks),
    /// <see cref="IHmacSigner"/>, <see cref="ISecureRandomGenerator"/>,
    /// <see cref="IContentHasher"/>, <see cref="IHotpGenerator"/>, <see cref="ITotpGenerator"/>,
    /// and <see cref="TotpVerifier"/> — as singletons.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>. <see cref="CryptographyOptions"/> is bound from the
    /// <see cref="CryptographyOptions.SectionName"/> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Two concrete implementations exist for <see cref="IAsymmetricSignatureService"/>
    /// (<see cref="RsaSignatureService"/> and <see cref="EcdsaSignatureService"/>). Both are
    /// registered as keyed singletons — resolve via
    /// <c>provider.GetRequiredKeyedService&lt;IAsymmetricSignatureService&gt;(RsaSignatureServiceKey)</c>
    /// or the <c>EcdsaSignatureServiceKey</c> equivalent. <see cref="RsaSignatureService"/> is
    /// additionally registered as the unkeyed default for callers that only need one algorithm.
    /// </para>
    /// <para>
    /// This method does <b>not</b> register <see cref="IEncryptionKeyProvider"/> or
    /// <see cref="IAsymmetricKeyProvider"/>, and ships no key material — the consuming service
    /// must separately register its own implementations of those interfaces (Key Vault,
    /// environment configuration, certificate store, etc.) before resolving
    /// <see cref="ISymmetricEncryptionService"/> or <see cref="IAsymmetricSignatureService"/>.
    /// </para>
    /// <para>
    /// This method also does <b>not</b> register <c>SharedKernel.Primitives.Clocks.IClock</c> —
    /// required by <see cref="ITotpGenerator"/> — or <see cref="ITotpReplayGuard"/> — required by
    /// <see cref="TotpVerifier"/>. The consuming service must separately register an
    /// <c>IClock</c> (e.g. via <c>services.AddClock()</c>) and its own
    /// <see cref="ITotpReplayGuard"/> implementation before resolving <see cref="ITotpGenerator"/>
    /// or <see cref="TotpVerifier"/> respectively; this package ships no default
    /// <see cref="ITotpReplayGuard"/> at all (it inherently requires a backing store this
    /// dependency-free package cannot own).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelCryptography(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<CryptographyOptions>(
            configuration.GetSection(CryptographyOptions.SectionName));

        services.AddSingleton<IOneWayHasher, Pbkdf2OneWayHasher>();
        services.AddSingleton<ISymmetricEncryptionService, AesGcmEncryptionService>();
        services.AddSingleton<IHmacSigner, HmacSha256Signer>();
        services.AddSingleton<ISecureRandomGenerator, CryptoRandomGenerator>();
        services.AddSingleton<IContentHasher, Sha256ContentHasher>();

        services.AddSingleton<IAsymmetricSignatureService, RsaSignatureService>();
        services.AddKeyedSingleton<IAsymmetricSignatureService, RsaSignatureService>(RsaSignatureServiceKey);
        services.AddKeyedSingleton<IAsymmetricSignatureService, EcdsaSignatureService>(EcdsaSignatureServiceKey);

        services.AddSingleton<IHotpGenerator, HotpGenerator>();
        services.AddSingleton<ITotpGenerator, TotpGenerator>();
        services.AddSingleton<TotpVerifier>();

        return services;
    }
}
