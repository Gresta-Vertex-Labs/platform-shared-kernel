using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Migrations;

/// <summary>
/// Base class of the design-time factory <c>dotnet ef</c> uses for a context registered with
/// <c>AddSharedKernelPostgres</c>: it builds the context on the <strong>migration</strong> connection (the owner role),
/// not the runtime one, so split-role deployments can add and script migrations.
/// </summary>
/// <typeparam name="TContext">The context.</typeparam>
/// <remarks>
/// <code>
/// public sealed class OrderDbContextFactory() : PostgresDesignTimeDbContextFactory&lt;OrderDbContext&gt;("orders")
/// {
///     protected override OrderDbContext Create(DbContextOptions&lt;OrderDbContext&gt; options, PersistenceContextDependencies dependencies)
///         =&gt; new(options, dependencies);
/// }
/// </code>
/// <para>
/// <strong>Connection string</strong>, first found wins: the <c>--connection "…"</c> argument
/// (<c>dotnet ef migrations script -- --connection "…"</c>); <c>SharedKernel:Persistence:{name}:MigrationConnectionString</c>;
/// <c>SharedKernel:Persistence:Npgsql:MigrationConnectionString</c>; <c>ConnectionStrings:{name}</c>. Read from
/// <c>appsettings.json</c>, <c>appsettings.{DOTNET_ENVIRONMENT or ASPNETCORE_ENVIRONMENT}.json</c> in the working
/// directory and environment variables (<c>ConnectionStrings__orders</c>). Adding a migration needs no database at all;
/// <c>migrations script --idempotent</c> neither.
/// </para>
/// <para>
/// Reference <c>Microsoft.EntityFrameworkCore.Design</c> (<c>PrivateAssets="all"</c>) from the project that holds the
/// migrations. In CI, generate an idempotent script (<c>dotnet ef migrations script --idempotent -o migrate.sql</c>)
/// and apply it as the owner role, or apply migrations at startup with <c>MigrateOnStartup()</c> and a
/// <c>MigrationConnectionString</c>.
/// </para>
/// </remarks>
public abstract class PostgresDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
    where TContext : SharedKernelDbContext
{
    private const string ConnectionArgument = "--connection";

    /// <summary>Initializes the factory for the connection name the context is registered with.</summary>
    /// <param name="connectionName">The name passed to <c>AddSharedKernelPostgres</c>.</param>
    protected PostgresDesignTimeDbContextFactory(string connectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ConnectionName = connectionName;
    }

    /// <summary>Gets the connection name the context is registered with.</summary>
    protected string ConnectionName { get; }

    /// <inheritdoc />
    public TContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString(args ?? [], BuildConfiguration())
            ?? throw new InvalidOperationException(
                $"No connection string for '{ConnectionName}'. Pass '-- {ConnectionArgument} \"Host=…\"' to dotnet ef, or set "
                + $"'SharedKernel:Persistence:{ConnectionName}:MigrationConnectionString' or 'ConnectionStrings:{ConnectionName}' "
                + "(appsettings.json or environment variables).");

        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        var options = new DbContextOptionsBuilder<TContext>();
        options.UsePostgres(dataSource, provider =>
        {
            provider.MaxRetryCount = 0;
            ConfigureProvider(provider);
        });

        return Create(options.Options, PersistenceContextDependencies.Create());
    }

    /// <summary>Creates the context: <c>=&gt; new(options, dependencies)</c>.</summary>
    /// <param name="options">The design-time options on the migration connection.</param>
    /// <param name="dependencies">The platform dependencies.</param>
    /// <returns>The context.</returns>
    protected abstract TContext Create(DbContextOptions<TContext> options, PersistenceContextDependencies dependencies);

    /// <summary>Changes the provider settings (for example <c>UseVector = true</c> for pgvector columns).</summary>
    /// <param name="options">The provider options; retry is off at design time.</param>
    protected virtual void ConfigureProvider(PostgresProviderOptions options)
    {
    }

    internal string? ResolveConnectionString(string[] args, IConfiguration configuration)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], ConnectionArgument, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return NonEmpty(configuration[$"SharedKernel:Persistence:{ConnectionName}:MigrationConnectionString"])
            ?? NonEmpty(configuration["SharedKernel:Persistence:Npgsql:MigrationConnectionString"])
            ?? NonEmpty(configuration.GetConnectionString(ConnectionName));
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true);

        if (!string.IsNullOrWhiteSpace(environment))
            builder.AddJsonFile($"appsettings.{environment}.json", optional: true);

        // Environment variables with the standard '__' separator, without an extra configuration package.
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                variables[key.Replace("__", ":", StringComparison.Ordinal)] = value;
        }

        return builder.AddInMemoryCollection(variables).Build();
    }
}
