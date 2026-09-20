using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Persistence.Npgsql.Context;
using SharedKernel.Persistence.Npgsql.Coordination;
using SharedKernel.Persistence.Npgsql.Diagnostics;
using SharedKernel.Persistence.Npgsql.Options;

namespace SharedKernel.Persistence.Npgsql.Extensions;

/// <summary>
/// DI extension methods for the SharedKernel Npgsql (EF-Core-free) persistence layer.
/// </summary>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The default-database AddSharedKernelNpgsql(IConfiguration, ...) overload and the second-database
// AddSharedKernelNpgsql(IConfigurationSection, string name, ...) overload take incompatible required
// leading parameters (IConfiguration vs. IConfigurationSection + a mandatory name) — a caller's
// argument list already selects the correct overload; there is no shared call shape across the two
// for a trailing optional parameter to ever disambiguate incorrectly.
public static class NpgsqlPersistenceExtensions
{
    private const string LoggerCategoryName = "SharedKernel.Persistence.Npgsql";

    /// <summary>
    /// Registers the shared, options-bound <see cref="NpgsqlDataSource"/> — bound from
    /// <see cref="NpgsqlPersistenceOptions.SectionName"/> and validated at host startup — plus
    /// <see cref="IDbConnectionFactory"/>, <see cref="IMigrationLock"/>,
    /// <see cref="ITenantSessionBinder"/>, and <see cref="IAdvisoryTransactionLock"/> (consumed by
    /// <c>SharedKernel.Persistence.EfCore.Auditing</c>'s audit-chain writer).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// The root configuration to resolve <see cref="NpgsqlPersistenceOptions.SectionName"/> against.
    /// </param>
    /// <param name="configureDataSource">
    /// Optional hook applied last, after every other configuration step, so a caller can extend the
    /// <see cref="NpgsqlDataSourceBuilder"/> with capabilities this method does not itself expose
    /// (e.g. <c>MapEnum&lt;T&gt;</c>, <c>MapComposite&lt;T&gt;</c>, pgvector's <c>UseVector()</c> —
    /// see <c>SharedKernel.Persistence.PostgreSQL</c>'s opt-in vector support for the recommended way
    /// to enable that specifically).
    /// </param>
    /// <param name="periodicPasswordProvider">
    /// Optional periodically-refreshed password provider (Entra ID / AWS RDS IAM tokens) — see
    /// <see cref="NpgsqlPeriodicPasswordProviderOptions"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="NpgsqlDataSource"/> is registered as a singleton, owned and disposed by the
    /// container at shutdown. <see cref="IDbConnectionFactory"/> is registered as a singleton
    /// (<see cref="NpgsqlConnectionFactory"/> itself is stateless — it only wraps the data source).
    /// </para>
    /// <para>
    /// Downstream services using EF Core call <c>AddSharedKernelEfCore&lt;TContext&gt;((sp, options)
    /// =&gt; options.UsePostgreSQL(sp))</c> — <c>SharedKernel.Persistence.PostgreSQL</c>'s
    /// <c>IServiceProvider</c>-accepting overload resolves the SAME <see cref="NpgsqlDataSource"/>
    /// this method registers, so EF Core and Dapper share one connection pool per database.
    /// </para>
    /// <para>
    /// For a second database in the same service, use the
    /// <see cref="AddSharedKernelNpgsql(IServiceCollection,IConfigurationSection,string,Action{NpgsqlDataSourceBuilder}?,NpgsqlPeriodicPasswordProviderOptions?)"/>
    /// overload with a distinct section and a <c>name</c> — resolve that instance with
    /// <c>serviceProvider.GetRequiredKeyedService&lt;NpgsqlDataSource&gt;(name)</c>/
    /// <c>GetRequiredKeyedService&lt;IDbConnectionFactory&gt;(name)</c>.
    /// <see cref="IMigrationLock"/>/<see cref="ITenantSessionBinder"/> are registered only for the
    /// default (unnamed) database — a named second database wires those itself if it needs them.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelNpgsql(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<NpgsqlDataSourceBuilder>? configureDataSource = null,
        NpgsqlPeriodicPasswordProviderOptions? periodicPasswordProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        RegisterOptions(services, configuration.GetSection(NpgsqlPersistenceOptions.SectionName), name: null);
        RegisterDefaultDataSource(services, configureDataSource, periodicPasswordProvider);

        services.AddSingleton<IMigrationLock, NpgsqlAdvisoryMigrationLock>();
        services.AddSingleton<ITenantSessionBinder>(sp =>
            new NpgsqlTenantSessionBinder(
                sp.GetRequiredService<IOptionsMonitor<NpgsqlPersistenceOptions>>()
                    .Get(Microsoft.Extensions.Options.Options.DefaultName).CrossTenantEscapeToken));
        services.AddSingleton<IAdvisoryTransactionLock, NpgsqlAdvisoryTransactionLock>();

        return services;
    }

    /// <summary>
    /// Registers a second, independently-configured, keyed <see cref="NpgsqlDataSource"/> for a
    /// service that talks to more than one PostgreSQL database.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="section">The configuration section for this database's connection options.</param>
    /// <param name="name">
    /// The key this data source and connection factory are registered under. Resolve with
    /// <c>GetRequiredKeyedService&lt;NpgsqlDataSource&gt;(name)</c>/
    /// <c>GetRequiredKeyedService&lt;IDbConnectionFactory&gt;(name)</c>.
    /// </param>
    /// <param name="configureDataSource">See the primary overload.</param>
    /// <param name="periodicPasswordProvider">See the primary overload.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    public static IServiceCollection AddSharedKernelNpgsql(
        this IServiceCollection services,
        IConfigurationSection section,
        string name,
        Action<NpgsqlDataSourceBuilder>? configureDataSource = null,
        NpgsqlPeriodicPasswordProviderOptions? periodicPasswordProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RegisterOptions(services, section, name);

        services.AddKeyedSingleton(name, (sp, key) =>
            BuildDataSource(
                sp.GetRequiredService<IOptionsMonitor<NpgsqlPersistenceOptions>>().Get((string)key!),
                sp,
                configureDataSource,
                periodicPasswordProvider));

        services.AddKeyedSingleton<IDbConnectionFactory>(name, (sp, key) =>
            new NpgsqlConnectionFactory(sp.GetRequiredKeyedService<NpgsqlDataSource>(key)));

        return services;
    }

    /// <summary>
    /// Registers the shared <see cref="NpgsqlDataSource"/> and <see cref="IDbConnectionFactory"/>
    /// from a raw connection string, applying only PostgreSQL's own defaults (no
    /// <see cref="NpgsqlPersistenceOptions"/> validation, no statement/lock timeouts, no SSL-mode
    /// enforcement beyond whatever the connection string itself specifies).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// Kept for callers that have not yet migrated to configuration-bound options. Prefer
    /// <see cref="AddSharedKernelNpgsql(IServiceCollection,IConfiguration,Action{NpgsqlDataSourceBuilder}?,NpgsqlPeriodicPasswordProviderOptions?)"/>
    /// for startup-validated SSL/timeout/password-rotation configuration.
    /// </remarks>
    public static IServiceCollection AddSharedKernelNpgsql(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();

        var dataSource = dataSourceBuilder.Build();

        services.AddSingleton(dataSource);
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();

        return services;
    }

    private static void RegisterOptions(IServiceCollection services, IConfigurationSection section, string? name)
    {
        services.AddValidatedOptions<NpgsqlPersistenceOptions, NpgsqlPersistenceOptionsValidator>(
            section,
            validateDataAnnotations: true,
            name: name);
    }

    private static void RegisterDefaultDataSource(
        IServiceCollection services,
        Action<NpgsqlDataSourceBuilder>? configureDataSource,
        NpgsqlPeriodicPasswordProviderOptions? periodicPasswordProvider)
    {
        services.AddSingleton(sp =>
            BuildDataSource(
                sp.GetRequiredService<IOptionsMonitor<NpgsqlPersistenceOptions>>()
                    .Get(Microsoft.Extensions.Options.Options.DefaultName),
                sp,
                configureDataSource,
                periodicPasswordProvider));

        services.AddSingleton<IDbConnectionFactory>(sp =>
            new NpgsqlConnectionFactory(sp.GetRequiredService<NpgsqlDataSource>()));
    }

    // Builds one NpgsqlDataSource from validated options: forces PersistSecurityInfo=false and the
    // configured SslMode, applies statement_timeout/lock_timeout/idle_in_transaction_session_timeout
    // via the libpq "Options" startup keyword, wires a resolved ILoggerFactory when one is
    // registered, wires an optional periodic password provider, and finally invokes the caller's own
    // configureDataSource hook so it can extend the builder further.
    private static NpgsqlDataSource BuildDataSource(
        NpgsqlPersistenceOptions options,
        IServiceProvider serviceProvider,
        Action<NpgsqlDataSourceBuilder>? configureDataSource,
        NpgsqlPeriodicPasswordProviderOptions? periodicPasswordProvider)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            SslMode = options.SslMode,
            PersistSecurityInfo = false,
        };

        ApplyServerSideTimeouts(connectionStringBuilder, options);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString);
        dataSourceBuilder.EnableDynamicJson();

        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        if (loggerFactory is not null)
            dataSourceBuilder.UseLoggerFactory(loggerFactory);

        if (periodicPasswordProvider is not null)
        {
            dataSourceBuilder.UsePeriodicPasswordProvider(
                periodicPasswordProvider.Provider,
                periodicPasswordProvider.SuccessRefreshInterval,
                periodicPasswordProvider.FailureRefreshInterval);
        }

        if (options.SslMode < SslMode.VerifyFull && options.AcknowledgeInsecureSslMode)
        {
            var logger = loggerFactory?.CreateLogger(LoggerCategoryName) ?? NullLogger.Instance;
            logger.InsecureSslModeAcknowledged(
                connectionStringBuilder.Host ?? "unknown",
                options.SslMode.ToString());
        }

        configureDataSource?.Invoke(dataSourceBuilder);

        return dataSourceBuilder.Build();
    }

    private static void ApplyServerSideTimeouts(
        NpgsqlConnectionStringBuilder connectionStringBuilder,
        NpgsqlPersistenceOptions options)
    {
        List<string> serverOptions = [];

        if (options.StatementTimeoutMilliseconds is { } statementTimeout)
            serverOptions.Add($"-c statement_timeout={statementTimeout}");

        if (options.LockTimeoutMilliseconds is { } lockTimeout)
            serverOptions.Add($"-c lock_timeout={lockTimeout}");

        if (options.IdleInTransactionSessionTimeoutMilliseconds is { } idleTimeout)
            serverOptions.Add($"-c idle_in_transaction_session_timeout={idleTimeout}");

        if (serverOptions.Count == 0)
            return;

        connectionStringBuilder.Options = string.IsNullOrEmpty(connectionStringBuilder.Options)
            ? string.Join(' ', serverOptions)
            : connectionStringBuilder.Options + " " + string.Join(' ', serverOptions);
    }
}
#pragma warning restore RS0026
