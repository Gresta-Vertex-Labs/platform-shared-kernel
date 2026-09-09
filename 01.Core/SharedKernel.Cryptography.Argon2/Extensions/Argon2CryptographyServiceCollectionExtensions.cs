using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Argon2.Options;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;

namespace SharedKernel.Cryptography.Argon2.Extensions;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Cryptography.Argon2</c> services.
/// </summary>
public static class Argon2CryptographyServiceCollectionExtensions
{
    /// <summary>
    /// The keyed-service key under which <see cref="Argon2idOneWayHasher"/> is registered for
    /// <see cref="IOneWayHasher"/>. There is no unkeyed registration for this implementation —
    /// <c>SharedKernel.Cryptography</c>'s <c>Pbkdf2OneWayHasher</c> remains the unkeyed default.
    /// </summary>
    public const string Argon2idOneWayHasherKey = "Argon2id";

    /// <summary>
    /// Registers <see cref="Argon2CryptographyOptions"/> (validated, eagerly checked at startup
    /// via <c>ValidateOnStart()</c>) and <see cref="Argon2idOneWayHasher"/> as the
    /// <see cref="Argon2idOneWayHasherKey"/> ("Argon2id") keyed singleton only, never the unkeyed
    /// <see cref="IOneWayHasher"/> default — mirrors
    /// <c>SharedKernel.Compression</c>'s <c>GZipPayloadCompressor</c> keyed-only registration
    /// pattern.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>. <see cref="Argon2CryptographyOptions"/> is bound
    /// from the <see cref="Argon2CryptographyOptions.SectionName"/> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method does <b>not</b> replace or alter <c>SharedKernel.Cryptography</c>'s own
    /// <c>AddSharedKernelCryptography</c> registration — the unkeyed <see cref="IOneWayHasher"/>
    /// resolved by that method remains <c>Pbkdf2OneWayHasher</c> regardless of whether this
    /// method is also called. Resolve the Argon2id implementation explicitly via
    /// <c>provider.GetRequiredKeyedService&lt;IOneWayHasher&gt;(Argon2idOneWayHasherKey)</c>.
    /// </para>
    /// <para>
    /// Also registers <see cref="ISecureRandomGenerator"/>/<c>CryptoRandomGenerator</c> via
    /// <c>TryAddSingleton</c> (never overriding an existing registration) so this method is
    /// self-sufficient even when the consumer has not separately called
    /// <c>SharedKernel.Cryptography</c>'s own <c>AddSharedKernelCryptography</c>, mirroring
    /// <c>SharedKernel.Cryptography.KeyVault.Azure</c>'s identical self-sufficiency pattern.
    /// </para>
    /// <para>
    /// Every registration in this method — including the keyed <see cref="Argon2idOneWayHasher"/>
    /// registration — uses <c>TryAddSingleton</c>/<c>TryAddKeyedSingleton</c>: calling this method
    /// more than once never double-registers, and a consumer registration made
    /// <b>before</b> this call always wins over the platform default.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelArgon2Cryptography(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<Argon2CryptographyOptions>(
            configuration.GetSection(Argon2CryptographyOptions.SectionName));

        services.TryAddSingleton<ISecureRandomGenerator, CryptoRandomGenerator>();

        services.TryAddKeyedSingleton<IOneWayHasher, Argon2idOneWayHasher>(Argon2idOneWayHasherKey);

        return services;
    }
}
