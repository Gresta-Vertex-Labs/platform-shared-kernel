using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// <see cref="IDbContextOptionsExtension"/> that registers <see cref="PostgreSQLConventionSetPlugin"/>
/// into EF Core's internal service provider, so the 63-byte identifier-length convention,
/// <see cref="XminConcurrencyTokenConvention"/>, and (opt-in) the pgvector extension annotation are
/// applied automatically to every model built for a <c>DbContext</c> configured via
/// <c>UsePostgreSQL()</c> — no manual <c>ConfigureConventions</c> override required.
/// </summary>
internal sealed class PostgreSQLConventionsOptionsExtension : IDbContextOptionsExtension
{
    /// <summary>Initialises a new <see cref="PostgreSQLConventionsOptionsExtension"/>.</summary>
    /// <param name="useVector">
    /// Whether pgvector support (<c>VectorExtensionConvention</c>) was opted into. Included in the
    /// internal-service-provider cache key so a context without vectors never reuses a cached service
    /// provider built for one with vectors, or vice versa.
    /// </param>
    /// <param name="maxRetryCount">
    /// The configured retry count, or <see langword="null"/> when retry is off. Informational only (retry-exhaustion
    /// logging); it changes no service, so it is not part of the cache key.
    /// </param>
    public PostgreSQLConventionsOptionsExtension(bool useVector = false, int? maxRetryCount = null)
    {
        UseVector = useVector;
        MaxRetryCount = maxRetryCount;
        Info = new ExtensionInfo(this);
    }

    /// <summary>Whether pgvector support was opted into.</summary>
    public bool UseVector { get; }

    /// <summary>The configured retry count, or <see langword="null"/> when retry is off.</summary>
    public int? MaxRetryCount { get; }

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info { get; }

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services) =>
        services.AddSingleton<IConventionSetPlugin>(new PostgreSQLConventionSetPlugin(UseVector));

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        // No validation required — this extension carries no invalid-state-reachable configuration.
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private PostgreSQLConventionsOptionsExtension TypedExtension => (PostgreSQLConventionsOptionsExtension)Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment =>
            "using SharedKernel PostgreSQL conventions (xmin concurrency token, 63-byte identifiers"
                + (TypedExtension.UseVector ? ", pgvector extension" : string.Empty) + ") ";

        // Distinct hash codes for useVector=true/false — two DbContexts differing only in this flag
        // must never share EF Core's cached internal service provider.
        public override int GetServiceProviderHashCode() => TypedExtension.UseVector ? 1 : 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo otherInfo && otherInfo.TypedExtension.UseVector == TypedExtension.UseVector;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["SharedKernel:PostgreSQLConventions"] = "true";
            debugInfo["SharedKernel:PostgreSQLConventions:UseVector"] = TypedExtension.UseVector.ToString();
        }
    }
}
