using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// <see cref="IModelCacheKeyFactory"/> that extends EF Core's default model cache key with the
/// <see cref="IEncryptionVersionOverride"/> instance injected into the owning
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's default model cache is keyed by context type (plus a couple of provider flags) and is
/// shared <strong>process-wide</strong> across all <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// instances of the same type — including instances created by different
/// <see cref="System.IServiceProvider"/> containers. Without this factory, the
/// <see cref="EncryptedValueConverter"/> instances that
/// <see cref="EncryptionModelConvention"/> bakes into the model during
/// <c>ConfigureConventions</c>/model finalization are captured from whichever
/// <see cref="IEncryptionVersionOverride"/> singleton happened to be resolved by the
/// <em>first</em> container to build that context type's model — every other container's
/// <see cref="IEncryptionVersionOverride"/> singleton (and any
/// <see cref="IEncryptionVersionOverride.OverrideVersion"/> set on it) is then silently ignored.
/// </para>
/// <para>
/// Including the <see cref="IEncryptionVersionOverride"/> instance (by reference) in the cache key
/// ensures each distinct container gets its own cached model, built with its own converters bound
/// to its own <see cref="IEncryptionVersionOverride"/> singleton — at the cost of one extra cached
/// model per container that uses a distinct <see cref="IEncryptionVersionOverride"/> instance. In
/// production, a service has exactly one container and therefore exactly one cached model, so this
/// has no practical memory impact; it primarily matters for test hosts and any process that
/// constructs more than one container for the same context type.
/// </para>
/// <para>
/// Registered via <see cref="EncryptionVersionOverrideOptionsBuilderExtensions.WithEncryptionVersionOverride"/>
/// in <see cref="Context.SharedKernelDbContext.OnConfiguring"/>, which both makes the
/// <see cref="IEncryptionVersionOverride"/> instance resolvable by EF Core's internal service
/// provider (via <see cref="EncryptionVersionOverrideOptionsExtension"/>) and replaces
/// <see cref="IModelCacheKeyFactory"/> with this type.
/// </para>
/// </remarks>
internal sealed class EncryptionAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    private readonly IEncryptionVersionOverride _encryptionVersionOverride;

    /// <summary>
    /// Initialises a new <see cref="EncryptionAwareModelCacheKeyFactory"/>.
    /// </summary>
    /// <param name="encryptionVersionOverride">
    /// The <see cref="IEncryptionVersionOverride"/> instance resolved for the owning context — the
    /// same instance passed to <see cref="EncryptionModelConvention"/> via
    /// <see cref="Context.SharedKernelDbContext.ConfigureConventions"/>.
    /// </param>
    public EncryptionAwareModelCacheKeyFactory(IEncryptionVersionOverride encryptionVersionOverride)
    {
        _encryptionVersionOverride = encryptionVersionOverride;
    }

    /// <inheritdoc />
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), designTime, _encryptionVersionOverride);
}
