using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// <see cref="IDbContextOptionsExtension"/> that carries this context's
/// <see cref="IEncryptionVersionOverride"/> instance into EF Core's internal service provider, so
/// that internally-resolved services (e.g. <see cref="EncryptionAwareModelCacheKeyFactory"/>) can
/// access it without requiring <c>UseApplicationServiceProvider</c>.
/// </summary>
/// <remarks>
/// EF Core's internal service provider cannot resolve arbitrary application-registered DI services
/// such as the <see cref="IEncryptionVersionOverride"/> singleton. Wrapping the instance in a
/// <see cref="IDbContextOptionsExtension"/> and registering it via
/// <see cref="ModelCacheKeyDbContextOptionsBuilderExtensions.WithEncryptionVersionOverride"/> makes it
/// available to EF-internal services through <c>IDbContextOptions.FindExtension&lt;T&gt;()</c> and
/// the extension's own <see cref="ApplyServices"/> registration — without changing the application's
/// DbContext lifetime/scoping semantics.
/// </remarks>
internal sealed class EncryptionVersionOverrideOptionsExtension : IDbContextOptionsExtension
{
    public EncryptionVersionOverrideOptionsExtension(IEncryptionVersionOverride encryptionVersionOverride)
    {
        EncryptionVersionOverride = encryptionVersionOverride;
        Info = new ExtensionInfo(this);
    }

    /// <summary>The <see cref="IEncryptionVersionOverride"/> instance for this context.</summary>
    public IEncryptionVersionOverride EncryptionVersionOverride { get; }

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info { get; }

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
        // Registers the override instance into EF Core's internal service provider so that
        // EncryptionAwareModelCacheKeyFactory (an internally-resolved IModelCacheKeyFactory) can
        // take it as a constructor dependency.
        services.AddSingleton(EncryptionVersionOverride);
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        // No validation required — the override instance is always non-null
        // (SharedKernelDbContext defaults to the shared no-op instance).
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using SharedKernel encryption version override";

        public override int GetServiceProviderHashCode() =>
            ((EncryptionVersionOverrideOptionsExtension)Extension).EncryptionVersionOverride.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo otherInfo
            && ReferenceEquals(
                ((EncryptionVersionOverrideOptionsExtension)Extension).EncryptionVersionOverride,
                ((EncryptionVersionOverrideOptionsExtension)otherInfo.Extension).EncryptionVersionOverride);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["SharedKernel:EncryptionVersionOverride"] =
                ((EncryptionVersionOverrideOptionsExtension)Extension).EncryptionVersionOverride.GetHashCode().ToString();
    }
}
