using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.PostgreSQL.Connections;
using SharedKernel.Persistence.PostgreSQL.Conventions;

namespace SharedKernel.Persistence.PostgreSQL.Extensions;

/// <summary>
/// DI and EF Core extension methods for the SharedKernel PostgreSQL persistence layer.
/// </summary>
public static class PostgreSQLPersistenceExtensions
{
    /// <summary>
    /// Configures the <see cref="DbContextOptionsBuilder"/> to use the Npgsql PostgreSQL provider
    /// with SharedKernel defaults: pgvector support via <c>UseVector()</c>, automatic
    /// <see cref="SnakeCaseNamingConvention"/> and <see cref="XminConcurrencyTokenConvention"/>
    /// registration, and (opt-in) transient-fault retry.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder to configure.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="maxRetryCount">
    /// Optional maximum number of retry attempts for transient Npgsql failures (WO-051/P-320).
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
    /// services.AddSharedKernelEfCore&lt;OrderDbContext&gt;(options =>
    ///     options.UsePostgreSQL(connectionString))
    ///     .Build();
    /// </code>
    /// </para>
    /// <para>
    /// <see cref="SnakeCaseNamingConvention"/> and <see cref="XminConcurrencyTokenConvention"/> are
    /// applied automatically via an <see cref="Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsExtension"/>
    /// — no manual <c>ConfigureConventions</c> override is required in the consuming
    /// <c>DbContext</c> (WO-051/P-315, correcting the prior manual-registration-only documentation).
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UsePostgreSQL(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        int? maxRetryCount = null,
        TimeSpan? maxRetryDelay = null,
        ICollection<string>? errorCodesToAdd = null)
    {
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.UseVector();
            ApplyRetry(npgsqlOptions, maxRetryCount, maxRetryDelay, errorCodesToAdd);
        });

        ApplyConventions(optionsBuilder);

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the <see cref="DbContextOptionsBuilder"/> to use the Npgsql PostgreSQL provider
    /// with a <see cref="NpgsqlDataSource"/> (shared connection pool). Preferred for services that
    /// also use <see cref="AddSharedKernelPostgreSQL"/>.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder to configure.</param>
    /// <param name="dataSource">The <see cref="NpgsqlDataSource"/> managing the connection pool.</param>
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
        int? maxRetryCount = null,
        TimeSpan? maxRetryDelay = null,
        ICollection<string>? errorCodesToAdd = null)
    {
        optionsBuilder.UseNpgsql(dataSource, npgsqlOptions =>
        {
            npgsqlOptions.UseVector();
            ApplyRetry(npgsqlOptions, maxRetryCount, maxRetryDelay, errorCodesToAdd);
        });

        ApplyConventions(optionsBuilder);

        return optionsBuilder;
    }

    // Threads EnableRetryOnFailure into the Npgsql options builder when maxRetryCount is supplied.
    // The ONLY legal call site for this Npgsql extension method — see UsePostgreSQL's XML doc.
    private static void ApplyRetry(
        Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsqlOptions,
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

    // Registers the IDbContextOptionsExtension that auto-applies SnakeCaseNamingConvention and
    // XminConcurrencyTokenConvention via IConventionSetPlugin — idempotent (AddOrUpdateExtension
    // replaces any prior registration of the same extension type).
    private static void ApplyConventions(DbContextOptionsBuilder optionsBuilder)
    {
        var extension = new PostgreSQLConventionsOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
    }

    /// <summary>
    /// Registers the shared <see cref="NpgsqlDataSource"/> and <see cref="IDbConnectionFactory"/>
    /// for Dapper and other raw-connection consumers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Downstream services call <c>AddDbContext&lt;TContext&gt;</c> separately using
    /// <c>options.UsePostgreSQL(connectionString)</c> or
    /// <c>options.UsePostgreSQL(sp.GetRequiredService&lt;NpgsqlDataSource&gt;())</c> — this
    /// extension only wires the shared <see cref="NpgsqlDataSource"/> and
    /// <see cref="IDbConnectionFactory"/>.
    /// </para>
    /// <para>
    /// <see cref="NpgsqlDataSource"/> is registered as a singleton so that all consumers
    /// share the same connection pool.
    /// </para>
    /// <para>
    /// <see cref="IDbConnectionFactory"/> is registered as scoped and resolves to
    /// <see cref="NpgsqlConnectionFactory"/>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelPostgreSQL(
        this IServiceCollection services,
        string connectionString)
    {
        // Build NpgsqlDataSource with dynamic JSON (STJ) enabled.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();

        var dataSource = dataSourceBuilder.Build();

        services.AddSingleton(dataSource);
        services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

        return services;
    }
}
