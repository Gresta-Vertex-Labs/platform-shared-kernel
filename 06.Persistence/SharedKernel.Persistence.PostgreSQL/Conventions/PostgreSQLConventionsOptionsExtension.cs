using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Persistence.PostgreSQL.Conventions;

/// <summary>
/// <see cref="IDbContextOptionsExtension"/> that registers <see cref="PostgreSQLConventionSetPlugin"/>
/// into EF Core's internal service provider, so <see cref="SnakeCaseNamingConvention"/>,
/// <see cref="XminConcurrencyTokenConvention"/>, and (opt-in) the pgvector extension annotation are
/// applied automatically to every model built for a <c>DbContext</c> configured via
/// <c>UsePostgreSQL()</c> — no manual <c>ConfigureConventions</c> override required.
/// </summary>
internal sealed class PostgreSQLConventionsOptionsExtension : IDbContextOptionsExtension
{
    /// <summary>Initialises a new <see cref="PostgreSQLConventionsOptionsExtension"/>.</summary>
    /// <param name="useVector">
    /// Whether pgvector support (<c>Vector/VectorExtensionConvention</c>) was opted into via
    /// <c>UsePostgreSQL(..., useVector: true)</c>. Included in the internal-service-provider cache
    /// key so a <c>useVector: false</c> context never accidentally reuses a cached
    /// service provider built for a <c>useVector: true</c> one, or vice versa.
    /// </param>
    public PostgreSQLConventionsOptionsExtension(bool useVector = false)
    {
        UseVector = useVector;
        Info = new ExtensionInfo(this);
    }

    /// <summary>Whether pgvector support was opted into.</summary>
    public bool UseVector { get; }

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
            "using SharedKernel PostgreSQL conventions (snake_case naming, xmin concurrency token"
                + (TypedExtension.UseVector ? ", pgvector extension": string.Empty) + ") ";

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
