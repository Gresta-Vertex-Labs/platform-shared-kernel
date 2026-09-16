using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Extensions;

namespace SharedKernel.Cryptography.Argon2;

/// <summary>Registers Argon2id with <c>SharedKernel.Cryptography</c>.</summary>
public static class Argon2CryptographyBuilderExtensions
{
    /// <summary>
    /// Registers and validates <see cref="Argon2Options"/> and adds <see cref="Argon2idOneWayHashAlgorithm"/> to the
    /// one-way hash algorithms. To hash new secrets with it, set <c>SharedKernel:Cryptography:OneWayHashing:Algorithm</c>
    /// to <c>argon2id</c>; hashes from other algorithms keep verifying and report <c>SuccessRehashNeeded</c>.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddSharedKernelCryptography</c>.</param>
    /// <param name="configuration">The configuration root; options bind from <c>SharedKernel:Cryptography:Argon2</c>.</param>
    /// <returns>The same builder.</returns>
    /// <example>
    /// <code>
    /// services.AddSharedKernelCryptography(configuration)
    ///     .AddArgon2id(configuration);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Binds configuration by reflection.")]
    [RequiresDynamicCode("Binds configuration by reflection.")]
    public static ICryptographyBuilder AddArgon2id(this ICryptographyBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddValidatedOptions<Argon2Options>(configuration);
        return builder.AddOneWayHashAlgorithm<Argon2idOneWayHashAlgorithm>();
    }
}
