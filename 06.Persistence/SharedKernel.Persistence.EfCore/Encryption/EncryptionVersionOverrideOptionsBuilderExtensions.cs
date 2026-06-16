using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Internal <see cref="DbContextOptionsBuilder"/> extension that wires the per-context
/// <see cref="IEncryptionVersionOverride"/> instance into EF Core's model cache key so that
/// distinct <see cref="System.IServiceProvider"/> containers never share a cached model whose
/// <see cref="EncryptedValueConverter"/> instances are bound to a different container's
/// <see cref="IEncryptionVersionOverride"/> singleton.
/// </summary>
internal static class EncryptionVersionOverrideOptionsBuilderExtensions
{
    /// <summary>
    /// Registers <paramref name="encryptionVersionOverride"/> with EF Core's internal service
    /// provider and replaces <see cref="IModelCacheKeyFactory"/> with
    /// <see cref="EncryptionAwareModelCacheKeyFactory"/>.
    /// </summary>
    /// <param name="optionsBuilder">The options builder for the context being configured.</param>
    /// <param name="encryptionVersionOverride">
    /// The <see cref="IEncryptionVersionOverride"/> instance injected into this context — either a
    /// real singleton registered by <c>.WithEncryption()</c>, or the shared no-op instance.
    /// </param>
    /// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
    public static DbContextOptionsBuilder WithEncryptionVersionOverride(
        this DbContextOptionsBuilder optionsBuilder,
        IEncryptionVersionOverride encryptionVersionOverride)
    {
        var extension = new EncryptionVersionOverrideOptionsExtension(encryptionVersionOverride);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        optionsBuilder.ReplaceService<IModelCacheKeyFactory, EncryptionAwareModelCacheKeyFactory>();

        return optionsBuilder;
    }
}
