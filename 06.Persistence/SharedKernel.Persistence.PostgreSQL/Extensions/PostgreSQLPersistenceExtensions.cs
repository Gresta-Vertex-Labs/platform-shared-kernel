using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Persistence.Npgsql.Options;
using SharedKernel.Persistence.PostgreSQL.Conventions;
using SharedKernel.Persistence.PostgreSQL.Exceptions;

namespace SharedKernel.Persistence.PostgreSQL.Extensions;

/// <summary>
/// DI and EF Core extension methods for the SharedKernel PostgreSQL persistence layer.
/// </summary>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The three UsePostgreSQL overloads take IServiceProvider, string (a connection string), or
// NpgsqlDataSource as their first required parameter — three mutually exclusive types with no
// implicit conversion between them, so the argument a caller passes already selects the correct
// overload; there is no shared call shape across the three for a trailing optional parameter to
// ever disambiguate incorrectly.
public static class PostgreSQLPersistenceExtensions
{
    /// <summary>
    /// Configures the <see cref="DbContextOptionsBuilder"/> to use the Npgsql PostgreSQL provider,
    /// resolving the <em>same</em> DI-registered <see cref="NpgsqlDataSource"/>
    /// <c>AddSharedKernelNpgsql</c> registered — the single-pool-per-database design that
    /// lets EF Core and Dapper share one connection pool.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder to configure.</param>
    /// <param name="serviceProvider">
    /// The <see cref="IServiceProvider"/> to resolve <see cref="NpgsqlDataSource"/> from. Pass the
    /// <c>sp</c> parameter of
    /// <c>AddSharedKernelEfCore&lt;TContext&gt;((sp, options) =&gt; options.UsePostgreSQL(sp))</c>'s
    /// <see cref="IServiceProvider"/>-accepting overload.
    /// </param>
    /// <param name="useVector">
    /// Opts in to pgvector support: enables Npgsql's <c>vector</c> CLR-type mapping and registers the
    /// <c>CREATE EXTENSION IF NOT EXISTS vector</c> model annotation automatically. Off by default —
    /// a service that never maps a <see cref="Pgvector.Vector"/>-typed column should not pay for the
    /// extension or the CLR-type mapping.
    /// </param>
    /// <param name="maxRetryCount">See the connection-string overload's XML doc.</param>
    /// <param name="maxRetryDelay">See the connection-string overload's XML doc.</param>
    /// <param name="errorCodesToAdd">See the connection-string overload's XML doc.</param>
    /// <returns>The same <paramref name="optionsBuilder"/> for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="NpgsqlDataSource"/> is registered in <paramref name="serviceProvider"/> — call
    /// <c>AddSharedKernelNpgsql(configuration)</c> first.
    /// </exception>
    public static DbContextOptionsBuilder UsePostgreSQL(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider,
        bool useVector = false,
        int? maxRetryCount = null,
        TimeSpan? maxRetryDelay = null,
        ICollection<string>? errorCodesToAdd = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var dataSource = serviceProvider.GetService<NpgsqlDataSource>()
            ?? throw new InvalidOperationException(
                $"No '{nameof(NpgsqlDataSource)}' is registered. Call "
                    + "'services.AddSharedKernelNpgsql(configuration)' before "
                    + $"'{nameof(UsePostgreSQL)}(DbContextOptionsBuilder, IServiceProvider,...)'.");

        return optionsBuilder.UsePostgreSQL(dataSource, useVector, maxRetryCount, maxRetryDelay, errorCodesToAdd);
    }

    /// <summary>
    /// Configures the <see cref="DbContextOptionsBuilder"/> to use the Npgsql PostgreSQL provider
    /// with SharedKernel defaults: automatic <see cref="SnakeCaseNamingConvention"/> and
    /// <see cref="XminConcurrencyTokenConvention"/> registration, opt-in pgvector support, and
    /// (opt-in) transient-fault retry.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder to configure.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="useVector">See the <see cref="IServiceProvider"/> overload's XML doc.</param>
    /// <param name="maxRetryCount">
    /// Optional maximum number of retry attempts for transient Npgsql failures.
    /// When <see langword="null"/> (the default), behavior is byte-for-byte unchanged from before
    /// this parameter existed — no <c>EnableRetryOnFailure</c> call is made. This is the <strong>only</strong>
    /// legal call site for Npgsql's <c>EnableRetryOnFailure</c> — <c>SharedKernel.Persistence.EfCore</c>
    /// never references Npgsql, so it cannot enable retry itself. Pair this with
    /// <c>EfCorePersistenceBuilder.WithTransientFaultRetry(maxRetryCount)</c> for DI-level
    /// discoverability of the same retry configuration — see that method's XML doc for the required
    /// two-call pairing and the explicit-transaction retry-safety correction it depends on.
    /// </param>
    /// <param name="maxRetryDelay">
    /// Optional maximum delay between retry attempts. Defaults to 30 seconds (Npgsql's own default)
    /// when <paramref name="maxRetryCount"/> is supplied but this parameter is <see langword="null"/>.
    /// Ignored when <paramref name="maxRetryCount"/> is <see langword="null"/>.
    /// </param>
    /// <param name="errorCodesToAdd">
    /// Optional additional PostgreSQL SQLSTATE error codes to treat as transient, beyond Npgsql's
    /// built-in transient-error set. Ignored when <paramref name="maxRetryCount"/> is <see langword="null"/>.
    /// </param>
    /// <returns>The same <paramref name="optionsBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Call this inside the <c>configureDb</c> action passed to
    /// <c>AddSharedKernelEfCore&lt;TContext&gt;</c>:
    /// <code>
    /// services.AddSharedKernelEfCore&lt;OrderDbContext&gt;(options =&gt;
    /// options.UsePostgreSQL(connectionString))
    ///     .Build();
    /// </code>
    /// Prefer the <see cref="IServiceProvider"/> overload when the service also uses
    /// <c>AddSharedKernelNpgsql(configuration)</c>/Dapper — it shares one connection pool instead of
    /// opening a second, independently-configured one from a raw connection string.
    /// </para>
    /// <para>
    /// <see cref="SnakeCaseNamingConvention"/> and <see cref="XminConcurrencyTokenConvention"/> are
    /// applied automatically via an <see cref="Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsExtension"/>
    /// — no manual <c>ConfigureConventions</c> override is required in the consuming
    /// <c>DbContext</c>.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UsePostgreSQL(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        bool useVector = false,
        int? maxRetryCount = null,
        TimeSpan? maxRetryDelay = null,
        ICollection<string>? errorCodesToAdd = null)
    {
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            if (useVector)
                npgsqlOptions.UseVector();

            ApplyRetry(npgsqlOptions, maxRetryCount, maxRetryDelay, errorCodesToAdd);
        });

        ApplyConventions(optionsBuilder, useVector);

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the <see cref="DbContextOptionsBuilder"/> to use the Npgsql PostgreSQL provider
    /// with a <see cref="NpgsqlDataSource"/> (shared connection pool). Preferred for services that
    /// also use <see cref="AddSharedKernelPostgreSQL(IServiceCollection,IConfiguration,Action{NpgsqlDataSourceBuilder}?,NpgsqlPeriodicPasswordProviderOptions?)"/>.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder to configure.</param>
    /// <param name="dataSource">The <see cref="NpgsqlDataSource"/> managing the connection pool.</param>
    /// <param name="useVector">See the <see cref="IServiceProvider"/> overload's XML doc.</param>
    /// <param name="maxRetryCount">
    /// Optional maximum number of retry attempts for transient Npgsql failures — see the
    /// connection-string overload's XML doc for the full explanation and required
    /// <c>EfCorePersistenceBuilder.WithTransientFaultRetry(...)</c> pairing.
    /// </param>
    /// <param name="maxRetryDelay">Optional maximum delay between retry attempts.</param>
    /// <param name="errorCodesToAdd">Optional additional transient PostgreSQL SQLSTATE error codes.</param>
    /// <returns>The same <paramref name="optionsBuilder"/> for fluent chaining.</returns>
    public static DbContextOptionsBuilder UsePostgreSQL(
        this DbContextOptionsBuilder optionsBuilder,
        NpgsqlDataSource dataSource,
        bool useVector = false,
        int? maxRetryCount = null,
        TimeSpan? maxRetryDelay = null,
        ICollection<string>? errorCodesToAdd = null)
    {
        optionsBuilder.UseNpgsql(dataSource, npgsqlOptions =>
        {
            if (useVector)
                npgsqlOptions.UseVector();

            ApplyRetry(npgsqlOptions, maxRetryCount, maxRetryDelay, errorCodesToAdd);
        });

        ApplyConventions(optionsBuilder, useVector);

        return optionsBuilder;
    }

    // Threads EnableRetryOnFailure into the Npgsql options builder when maxRetryCount is supplied.
    // The ONLY legal call site for this Npgsql extension method — see UsePostgreSQL's XML doc.
    private static void ApplyRetry(
        global::Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsqlOptions,
        int? maxRetryCount,
        TimeSpan? maxRetryDelay,
        ICollection<string>? errorCodesToAdd)
    {
        if (maxRetryCount is null)
            return;

        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount.Value,
            maxRetryDelay ?? TimeSpan.FromSeconds(30),
            errorCodesToAdd);
    }

    // Registers the IDbContextOptionsExtension that auto-applies SnakeCaseNamingConvention,
    // XminConcurrencyTokenConvention, and (opt-in) the pgvector extension convention via
    // IConventionSetPlugin — idempotent (AddOrUpdateExtension replaces any prior registration of the
    // same extension type).
    private static void ApplyConventions(DbContextOptionsBuilder optionsBuilder, bool useVector)
    {
        var extension = new PostgreSQLConventionsOptionsExtension(useVector);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
    }

    /// <summary>
    /// Registers the shared, options-bound <see cref="NpgsqlDataSource"/>, <see cref="SharedKernel.Persistence.Abstractions.Connections.IDbConnectionFactory"/>,
    /// <see cref="SharedKernel.Persistence.Abstractions.Coordination.IMigrationLock"/>, and
    /// <see cref="SharedKernel.Persistence.Abstractions.Context.ITenantSessionBinder"/> (via
    /// <c>SharedKernel.Persistence.Npgsql</c>'s <c>AddSharedKernelNpgsql</c>), plus this package's
    /// <see cref="IDbUpdateExceptionClassifier"/> implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// The root configuration to resolve <c>NpgsqlPersistenceOptions.SectionName</c> against.
    /// </param>
    /// <param name="configureDataSource">See <c>AddSharedKernelNpgsql</c>'s XML doc.</param>
    /// <param name="periodicPasswordProvider">See <c>AddSharedKernelNpgsql</c>'s XML doc.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    public static IServiceCollection AddSharedKernelPostgreSQL(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<NpgsqlDataSourceBuilder>? configureDataSource = null,
        NpgsqlPeriodicPasswordProviderOptions? periodicPasswordProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSharedKernelNpgsql(configuration, configureDataSource, periodicPasswordProvider);
        RegisterClassifier(services);

        return services;
    }

    /// <summary>
    /// Registers the shared <see cref="NpgsqlDataSource"/> and
    /// <see cref="SharedKernel.Persistence.Abstractions.Connections.IDbConnectionFactory"/> for
    /// Dapper and other raw-connection consumers, plus this package's
    /// <see cref="IDbUpdateExceptionClassifier"/> implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// Delegates to <c>SharedKernel.Persistence.Npgsql</c>'s
    /// <see cref="NpgsqlPersistenceExtensions.AddSharedKernelNpgsql(IServiceCollection,string)"/> — the shared
    /// <see cref="NpgsqlDataSource"/>/connection-factory registration now lives entirely in that
    /// EF-Core-free package. This method is kept as the existing, documented entry point for
    /// consumers already calling <c>AddSharedKernelPostgreSQL(...)</c>. Prefer the
    /// <see cref="IConfiguration"/> overload for startup-validated SSL/timeout configuration.
    /// </remarks>
    public static IServiceCollection AddSharedKernelPostgreSQL(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSharedKernelNpgsql(connectionString);
        RegisterClassifier(services);

        return services;
    }

    private static void RegisterClassifier(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDbUpdateExceptionClassifier, PostgreSqlDbUpdateExceptionClassifier>());
    }
}
#pragma warning restore RS0026
