using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Persistence.PostgreSQL.Conventions;

/// <summary>
/// <see cref="IDbContextOptionsExtension"/> that registers <see cref="PostgreSQLConventionSetPlugin"/>
/// into EF Core's internal service provider, so <see cref="SnakeCaseNamingConvention"/> and
/// <see cref="XminConcurrencyTokenConvention"/> are applied automatically to every model built for a
/// <c>DbContext</c> configured via <c>UsePostgreSQL()</c> — no manual <c>ConfigureConventions</c>
/// override required.
/// </summary>
internal sealed class PostgreSQLConventionsOptionsExtension : IDbContextOptionsExtension
{
    /// <summary>Initialises a new <see cref="PostgreSQLConventionsOptionsExtension"/>.</summary>
    public PostgreSQLConventionsOptionsExtension()
    {
        Info = new ExtensionInfo(this);
    }

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info { get; }

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services) =>
        services.AddSingleton<IConventionSetPlugin, PostgreSQLConventionSetPlugin>();

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        // No validation required — this extension carries no configurable state.
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment =>
            "using SharedKernel PostgreSQL conventions (snake_case naming, xmin concurrency token) ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["SharedKernel:PostgreSQLConventions"] = "true";
    }
}
