using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.Npgsql.Options;

namespace SharedKernel.Persistence.EfCore.Extensions;

/// <summary>
/// The platform's PostgreSQL provider setup for EF Core.
/// </summary>
/// <remarks>
/// <para>
/// <c>UsePostgreSQL</c> configures Npgsql with everything this platform always wants:
/// </para>
/// <list type="bullet">
/// <item><description>snake_case table, column, key, index and constraint names (<c>EFCore.NamingConventions</c>), kept within PostgreSQL's 63-byte identifier limit;</description></item>
/// <item><description>the <c>xmin</c> system column as the concurrency token of every aggregate root and every <c>IHasConcurrency</c> entity;</description></item>
/// <item><description>Npgsql's retrying execution strategy, <strong>on by default</strong> (see <see cref="PostgreSqlRetryOptions"/>);</description></item>
/// <item><description>optional pgvector support (<see cref="PostgreSqlProviderOptions.UseVector"/>).</description></item>
/// </list>
/// <para>
/// SQLSTATE exception classification (unique violation → Conflict, foreign key → Validation or Conflict,...)
/// is part of every <c>SharedKernelDbContext</c> (registered or built with <c>PersistenceContextDependencies.Create</c>).
/// Most services never call this method: <c>AddSharedKernelPostgres</c> does. Use it for a design-time factory or a
/// hand-built context.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The two UsePostgreSQL overloads take IServiceProvider or NpgsqlDataSource as their first required parameter:
// unrelated types, so the argument already selects the overload and the optional trailing parameter cannot
// make a call ambiguous.
public static class PostgreSQLPersistenceExtensions
{
    /// <summary>
    /// Configures <paramref name="optionsBuilder"/> for PostgreSQL over the shared, DI-registered
    /// <see cref="NpgsqlDataSource"/> (<c>AddSharedKernelNpgsql(configuration)</c>), so EF Core and Dapper share
    /// one connection pool.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder.</param>
    /// <param name="serviceProvider">
    /// The provider to resolve the data source from (the <c>sp</c> of an options callback).
    /// </param>
    /// <param name="configure">Optional provider settings (retry, pgvector).</param>
    /// <returns>The same <paramref name="optionsBuilder"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="NpgsqlDataSource"/> is registered, or pgvector was requested while the shared data source
    /// was built without it.
    /// </exception>
    /// <example>
    /// <code>
    /// services.AddSharedKernelNpgsql(configuration);
    /// services.AddDbContextFactory&lt;ReportingContext&gt;((sp, options) =&gt; options.UsePostgreSQL(sp));
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UsePostgreSQL(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider,
        Action<PostgreSqlProviderOptions>? configure = null)
        => optionsBuilder.UsePostgreSQL(serviceProvider, dataSourceName: null, configure);

    // dataSourceName: null for the default (unkeyed) data source, else the key of a named one.
    internal static DbContextOptionsBuilder UsePostgreSQL(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider,
        string? dataSourceName,
        Action<PostgreSqlProviderOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var dataSource = (dataSourceName is null
                ? serviceProvider.GetService<NpgsqlDataSource>()
                : serviceProvider.GetKeyedService<NpgsqlDataSource>(dataSourceName))
            ?? throw new InvalidOperationException(
                $"No '{nameof(NpgsqlDataSource)}' is registered. Register the context with "
                    + "'AddSharedKernelPostgres<TContext>(name)' (or call 'services.AddSharedKernelNpgsql(configuration)') before "
                    + $"'{nameof(UsePostgreSQL)}(DbContextOptionsBuilder, IServiceProvider,...)'.");

        var providerOptions = BuildOptions(configure);

        // The shared data source carries the ADO-level vector mapping; follow it, and refuse a request for
        // vectors the data source cannot read or write.
        var dataSourceOptions = serviceProvider.GetService<IOptionsMonitor<NpgsqlPersistenceOptions>>()
            ?.Get(dataSourceName ?? Microsoft.Extensions.Options.Options.DefaultName);
        var dataSourceIsShared = !string.IsNullOrEmpty(dataSourceOptions?.ConnectionString);

        if (dataSourceIsShared && dataSourceOptions!.UseVector)
        {
            providerOptions.UseVector = true;
        }
        else if (dataSourceIsShared && providerOptions.UseVector)
        {
            throw new InvalidOperationException(
                "pgvector was requested through 'UsePostgreSQL(sp, o => o.UseVector = true)' but the shared "
                    + "NpgsqlDataSource was built without it, so Vector values could not be read or written. Set "
                    + $"'{NpgsqlPersistenceOptions.SectionName}:{nameof(NpgsqlPersistenceOptions.UseVector)}' to true.");
        }

        return optionsBuilder.UsePostgreSQLCore(dataSource, providerOptions);
    }

    /// <summary>
    /// Configures <paramref name="optionsBuilder"/> for PostgreSQL over an explicit <see cref="NpgsqlDataSource"/>.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder.</param>
    /// <param name="dataSource">
    /// The data source. Keep one per database for the process lifetime; a data source per context would open
    /// a connection pool per context. When <see cref="PostgreSqlProviderOptions.UseVector"/> is set, build it
    /// with <c>dataSourceBuilder.UseVector()</c>.
    /// </param>
    /// <param name="configure">Optional provider settings (retry, pgvector).</param>
    /// <returns>The same <paramref name="optionsBuilder"/>.</returns>
    public static DbContextOptionsBuilder UsePostgreSQL(
        this DbContextOptionsBuilder optionsBuilder,
        NpgsqlDataSource dataSource,
        Action<PostgreSqlProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(dataSource);

        return optionsBuilder.UsePostgreSQLCore(dataSource, BuildOptions(configure));
    }

    private static PostgreSqlProviderOptions BuildOptions(Action<PostgreSqlProviderOptions>? configure)
    {
        var options = new PostgreSqlProviderOptions();
        configure?.Invoke(options);

        if (options.Retry.Enabled)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(options.Retry.MaxRetryCount, "Retry.MaxRetryCount");
            ArgumentOutOfRangeException.ThrowIfLessThan(options.Retry.MaxRetryDelay, TimeSpan.Zero, "Retry.MaxRetryDelay");
        }

        return options;
    }

    private static DbContextOptionsBuilder UsePostgreSQLCore(
        this DbContextOptionsBuilder optionsBuilder,
        NpgsqlDataSource dataSource,
        PostgreSqlProviderOptions options)
    {
        optionsBuilder.UseNpgsql(dataSource, npgsql =>
        {
            if (options.UseVector)
                npgsql.UseVector();

            if (options.Retry.Enabled)
            {
                npgsql.EnableRetryOnFailure(
                    options.Retry.MaxRetryCount,
                    options.Retry.MaxRetryDelay,
                    options.Retry.AdditionalTransientErrorCodes.ToArray());
            }
        });

        optionsBuilder.UseSnakeCaseNamingConvention(CultureInfo.InvariantCulture);

        // Adds the xmin concurrency-token convention, the 63-byte identifier convention and (opt-in) the pgvector
        // extension annotation through an IConventionSetPlugin — idempotent (AddOrUpdateExtension replaces any
        // earlier registration of the same extension type).
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            new PostgreSQLConventionsOptionsExtension(
                options.UseVector,
                options.Retry.Enabled ? options.Retry.MaxRetryCount : null));

        return optionsBuilder;
    }
}
#pragma warning restore RS0026
