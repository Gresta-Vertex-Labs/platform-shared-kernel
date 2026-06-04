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
    /// with SharedKernel defaults: pgvector support via <c>UseVector()</c>.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder to configure.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
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
    /// To apply <see cref="SnakeCaseNamingConvention"/>, override
    /// <c>ConfigureConventions(ModelConfigurationBuilder)</c> in the DbContext and call:
    /// <code>
    /// configurationBuilder.Conventions.Add(provider => new SnakeCaseNamingConvention());
    /// </code>
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UsePostgreSQL(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString)
    {
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.UseVector();
        });

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the <see cref="DbContextOptionsBuilder"/> to use the Npgsql PostgreSQL provider
    /// with a <see cref="NpgsqlDataSource"/> (shared connection pool). Preferred for services that
    /// also use <see cref="AddSharedKernelPostgreSQL"/>.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder to configure.</param>
    /// <param name="dataSource">The <see cref="NpgsqlDataSource"/> managing the connection pool.</param>
    /// <returns>The same <paramref name="optionsBuilder"/> for fluent chaining.</returns>
    public static DbContextOptionsBuilder UsePostgreSQL(
        this DbContextOptionsBuilder optionsBuilder,
        NpgsqlDataSource dataSource)
    {
        optionsBuilder.UseNpgsql(dataSource, npgsqlOptions =>
        {
            npgsqlOptions.UseVector();
        });

        return optionsBuilder;
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
