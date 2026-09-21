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
/// <c>UsePostgres</c> configures Npgsql with everything this platform always wants:
/// </para>
/// <list type="bullet">
/// <item><description>snake_case table, column, key, index and constraint names (<c>EFCore.NamingConventions</c>), kept within PostgreSQL's 63-byte identifier limit;</description></item>
/// <item><description>the <c>xmin</c> system column as the concurrency token of every aggregate root and every <c>IHasConcurrency</c> entity;</description></item>
/// <item><description>Npgsql's retrying execution strategy, <strong>on by default</strong> (<see cref="PostgresProviderOptions.MaxRetryCount"/> 0 turns it off);</description></item>
/// <item><description>optional pgvector support (<see cref="PostgresProviderOptions.UseVector"/>).</description></item>
/// </list>
/// <para>
/// SQLSTATE exception classification (unique violation → Conflict, foreign key → Validation or Conflict,...)
/// is part of every <c>SharedKernelDbContext</c> (registered or built with <c>PersistenceContextDependencies.Create</c>).
/// Most services never call this method: <c>AddSharedKernelPostgres</c> does. Use it for a design-time factory or a
/// hand-built context.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The two UsePostgres overloads take IServiceProvider or NpgsqlDataSource as their first required parameter:
// unrelated types, so the argument already selects the overload and the optional trailing parameter cannot
// make a call ambiguous.
public static partial class PostgresPersistenceExtensions
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
    /// services.AddDbContextFactory&lt;ReportingContext&gt;((sp, options) =&gt; options.UsePostgres(sp));
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UsePostgres(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider,
        Action<PostgresProviderOptions>? configure = null)
        => optionsBuilder.UsePostgres(serviceProvider, dataSourceName: null, configure);

    // dataSourceName: null for the default (unkeyed) data source, else the key of a named one.
    internal static DbContextOptionsBuilder UsePostgres(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider,
        string? dataSourceName,
        Action<PostgresProviderOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var dataSource = (dataSourceName is null
                ? serviceProvider.GetService<NpgsqlDataSource>()
                : serviceProvider.GetKeyedService<NpgsqlDataSource>(dataSourceName))
            ?? throw new InvalidOperationException(
                $"No '{nameof(NpgsqlDataSource)}' is registered. Register the context with "
                    + "'AddSharedKernelPostgres<TContext>(name)' (or call 'services.AddSharedKernelNpgsql(configuration)') before "
                    + $"'{nameof(UsePostgres)}(DbContextOptionsBuilder, IServiceProvider,...)'.");

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
                "pgvector was requested through 'UsePostgres(sp, o => o.UseVector = true)' but the shared "
                    + "NpgsqlDataSource was built without it, so Vector values could not be read or written. Set "
                    + $"'SharedKernel:Persistence:{dataSourceName ?? "{connection name}"}:{nameof(NpgsqlPersistenceOptions.UseVector)}' to true.");
        }

        return optionsBuilder.UsePostgresCore(dataSource, providerOptions);
    }

    /// <summary>
    /// Configures <paramref name="optionsBuilder"/> for PostgreSQL over an explicit <see cref="NpgsqlDataSource"/>.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder.</param>
    /// <param name="dataSource">
    /// The data source. Keep one per database for the process lifetime; a data source per context would open
    /// a connection pool per context. When <see cref="PostgresProviderOptions.UseVector"/> is set, build it
    /// with <c>dataSourceBuilder.UseVector()</c>.
    /// </param>
    /// <param name="configure">Optional provider settings (retry, pgvector).</param>
    /// <returns>The same <paramref name="optionsBuilder"/>.</returns>
    public static DbContextOptionsBuilder UsePostgres(
        this DbContextOptionsBuilder optionsBuilder,
        NpgsqlDataSource dataSource,
        Action<PostgresProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(dataSource);

        return optionsBuilder.UsePostgresCore(dataSource, BuildOptions(configure));
    }

    private static PostgresProviderOptions BuildOptions(Action<PostgresProviderOptions>? configure)
    {
        var options = new PostgresProviderOptions();
        configure?.Invoke(options);

        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxRetryCount, nameof(options.MaxRetryCount));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRetryDelay, TimeSpan.Zero, nameof(options.MaxRetryDelay));

        return options;
    }

    private static DbContextOptionsBuilder UsePostgresCore(
        this DbContextOptionsBuilder optionsBuilder,
        NpgsqlDataSource dataSource,
        PostgresProviderOptions options)
    {
        optionsBuilder.UseNpgsql(dataSource, npgsql =>
        {
            if (options.UseVector)
                npgsql.UseVector();

            if (options.MaxRetryCount > 0)
            {
                npgsql.EnableRetryOnFailure(
                    options.MaxRetryCount,
                    options.MaxRetryDelay,
                    options.AdditionalTransientErrorCodes.ToArray());
            }
        });

        optionsBuilder.UseSnakeCaseNamingConvention(CultureInfo.InvariantCulture);

        // Adds the xmin concurrency-token convention, the 63-byte identifier convention and (opt-in) the pgvector
        // extension annotation through an IConventionSetPlugin — idempotent (AddOrUpdateExtension replaces any
        // earlier registration of the same extension type).
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            new PostgresConventionsOptionsExtension(
                options.UseVector,
                options.MaxRetryCount > 0 ? options.MaxRetryCount : null));

        return optionsBuilder;
    }
}
#pragma warning restore RS0026
