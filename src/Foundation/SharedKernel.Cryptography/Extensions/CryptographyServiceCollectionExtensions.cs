using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Cryptography.Extensions;

/// <summary>Chains the optional parts of <c>SharedKernel.Cryptography</c> onto <see cref="CryptographyServiceCollectionExtensions.AddSharedKernelCryptography"/>.</summary>
public interface ICryptographyBuilder
{
    /// <summary>The service collection being configured.</summary>
    IServiceCollection Services { get; }
}

/// <summary>Registers <c>SharedKernel.Cryptography</c> services.</summary>
/// <remarks>
/// <para>
/// <see cref="AddSharedKernelCryptography"/> registers everything that needs no keys. Services that need keys are
/// opt-in, because each requires a provider only the consuming service can register:
/// </para>
/// <list type="table">
/// <listheader><term>Call</term><description>Registers, and requires</description></listheader>
/// <item><term><see cref="AddSymmetricEncryption"/></term><description><see cref="ISymmetricEncryptionService"/>; requires <see cref="IEncryptionKeyProvider"/>.</description></item>
/// <item><term><see cref="AddSynchronousSymmetricEncryption"/></term><description><see cref="ISynchronousSymmetricEncryptionService"/>; requires <see cref="ISynchronousEncryptionKeyProvider"/>.</description></item>
/// <item><term><see cref="AddEnvelopeEncryption"/></term><description><see cref="IEnvelopeEncryptionService"/>; requires <see cref="IEnvelopeEncryptionProvider"/>.</description></item>
/// <item><term><see cref="AddAsymmetricSigning"/></term><description><see cref="IAsymmetricSignatureService"/>; requires <see cref="ISigningKeyProvider"/>.</description></item>
/// <item><term><see cref="AddTotpVerification"/></term><description><see cref="ITotpVerifier"/>; requires <see cref="ITotpReplayGuard"/>.</description></item>
/// </list>
/// <para>
/// Every registration uses <c>TryAdd</c>, so calling a method twice is harmless and a registration made earlier wins.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddSingleton&lt;IEncryptionKeyProvider&gt;(keyProvider);
/// services.AddSharedKernelCryptography(configuration)
///     .AddSymmetricEncryption();
/// </code>
/// </example>
public static class CryptographyServiceCollectionExtensions
{
    /// <summary>
    /// Registers and validates <see cref="CryptographyOptions"/> and <see cref="Pbkdf2Options"/>, and registers the
    /// services that need no keys:
    /// <see cref="IOneWayHasher"/> with <see cref="Pbkdf2OneWayHashAlgorithm"/>, <see cref="ISecureRandomGenerator"/>,
    /// <see cref="IContentHasher"/>, <see cref="IHmacSigner"/>, <see cref="IHotpGenerator"/>,
    /// <see cref="ITotpGenerator"/>, <see cref="IRecoveryCodeGenerator"/> and, when none is registered, <see cref="IClock"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root; options bind from <c>SharedKernel:Cryptography</c>.</param>
    /// <returns>A builder for the optional services.</returns>
    [RequiresUnreferencedCode("Binds configuration by reflection.")]
    [RequiresDynamicCode("Binds configuration by reflection.")]
    public static ICryptographyBuilder AddSharedKernelCryptography(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<CryptographyOptions, CryptographyOptionsValidator>(configuration, validateDataAnnotations: true);
        services.AddValidatedOptions<Pbkdf2Options>(configuration);

        services.TryAddSingleton<ISecureRandomGenerator, SecureRandomGenerator>();
        services.TryAddSingleton<IContentHasher, Sha256ContentHasher>();
        services.TryAddSingleton<IHmacSigner, HmacSha256Signer>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOneWayHashAlgorithm, Pbkdf2OneWayHashAlgorithm>());
        services.TryAddSingleton<IOneWayHasher, OneWayHasher>();
        services.AddClock();
        services.TryAddSingleton<IHotpGenerator, HotpGenerator>();
        services.TryAddSingleton<ITotpGenerator, TotpGenerator>();
        services.TryAddSingleton<IRecoveryCodeGenerator, RecoveryCodeGenerator>();

        return new CryptographyBuilder(services);
    }

    /// <summary>Registers <see cref="ISymmetricEncryptionService"/>. Register an <see cref="IEncryptionKeyProvider"/> as well.</summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The same builder.</returns>
    public static ICryptographyBuilder AddSymmetricEncryption(this ICryptographyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<ISymmetricEncryptionService, AesGcmEncryptionService>();
        return builder;
    }

    /// <summary>
    /// Registers <see cref="ISynchronousSymmetricEncryptionService"/>. Register an
    /// <see cref="ISynchronousEncryptionKeyProvider"/> as well.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The same builder.</returns>
    public static ICryptographyBuilder AddSynchronousSymmetricEncryption(this ICryptographyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<ISynchronousSymmetricEncryptionService, SynchronousAesGcmEncryptionService>();
        return builder;
    }

    /// <summary>Registers <see cref="IEnvelopeEncryptionService"/>. Register an <see cref="IEnvelopeEncryptionProvider"/> as well.</summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The same builder.</returns>
    public static ICryptographyBuilder AddEnvelopeEncryption(this ICryptographyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IEnvelopeEncryptionService, EnvelopeEncryptionService>();
        return builder;
    }

    /// <summary>Registers <see cref="IAsymmetricSignatureService"/>. Register an <see cref="ISigningKeyProvider"/> as well.</summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The same builder.</returns>
    public static ICryptographyBuilder AddAsymmetricSigning(this ICryptographyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IAsymmetricSignatureService, AsymmetricSignatureService>();
        return builder;
    }

    /// <summary>Registers <see cref="ITotpVerifier"/>. Register an <see cref="ITotpReplayGuard"/> as well.</summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The same builder.</returns>
    public static ICryptographyBuilder AddTotpVerification(this ICryptographyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<ITotpVerifier, TotpVerifier>();
        return builder;
    }

    /// <summary>
    /// Registers an additional <see cref="IOneWayHashAlgorithm"/>. Select it for new hashes with
    /// <see cref="OneWayHashingOptions.Algorithm"/>; hashes from every registered algorithm keep verifying.
    /// </summary>
    /// <remarks>
    /// The algorithm must not depend on <c>IOptions&lt;CryptographyOptions&gt;</c>, which validates against the
    /// registered algorithms; bind its settings to its own options type.
    /// </remarks>
    /// <typeparam name="TAlgorithm">The algorithm.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns>The same builder.</returns>
    public static ICryptographyBuilder AddOneWayHashAlgorithm<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAlgorithm>(this ICryptographyBuilder builder)
        where TAlgorithm : class, IOneWayHashAlgorithm
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOneWayHashAlgorithm, TAlgorithm>());
        return builder;
    }

    private sealed class CryptographyBuilder(IServiceCollection services) : ICryptographyBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
