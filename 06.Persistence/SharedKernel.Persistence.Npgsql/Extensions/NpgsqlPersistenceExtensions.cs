using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Npgsql;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Persistence.Npgsql.Context;
using SharedKernel.Persistence.Npgsql.Coordination;
using SharedKernel.Persistence.Npgsql.Diagnostics;
using SharedKernel.Persistence.Npgsql.Options;
using SharedKernel.Persistence.Npgsql.RowLevelSecurity;

namespace SharedKernel.Persistence;

/// <summary>
/// DI extension methods for the SharedKernel Npgsql (EF-Core-free) persistence layer.
/// </summary>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The overloads differ in their required leading parameters (IConfiguration; IConfiguration + a
// connection-string name; IConfigurationSection + a service key), which already select one overload.
public static class NpgsqlPersistenceExtensions
{
    private const string LoggerCategoryName = "SharedKernel.Persistence.Npgsql";
    private const string DefaultDataSourceName = "default";

    /// <summary>
    /// Registers the default database without a connection name: an options-bound <see cref="NpgsqlDataSource"/>
    /// (every setting, the connection string included, from <see cref="NpgsqlPersistenceOptions.SectionName"/>,
    /// validated at startup), <see cref="IDbConnectionFactory"/>, <see cref="IMigrationLock"/>,
    /// <see cref="IAdvisoryTransactionLock"/>, <see cref="ITenantSessionBinder"/>, the keyed secondary data sources of
    /// <see cref="NpgsqlDataSourceKeys"/>, and the row-level security startup check.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The root configuration.</param>
    /// <param name="configureDataSource">
    /// Optional hook applied last to every <see cref="NpgsqlDataSourceBuilder"/> this registration builds,
    /// with the resolving <see cref="IServiceProvider"/> — for <c>MapEnum&lt;T&gt;()</c>,
    /// <c>UsePeriodicPasswordProvider</c> (Entra ID / RDS IAM tokens), and similar.
    /// </param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <remarks>
    /// <para>
    /// EF Core (<c>UsePostgreSQL(serviceProvider)</c>) resolves the same <see cref="NpgsqlDataSource"/>, so
    /// EF Core and Dapper share one pool. The data source is a singleton owned by the container.
    /// </para>
    /// <para>
    /// <strong>PgBouncer (transaction mode)</strong> is supported: every SharedKernel tenant binding is
    /// transaction-local. Point <see cref="NpgsqlPersistenceOptions.MigrationConnectionString"/> at the
    /// database directly, because migrations and session-level advisory locks need one server session.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelNpgsql(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, NpgsqlDataSourceBuilder>? configureDataSource = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return RegisterDefault(services, configuration, NpgsqlPersistenceOptions.SectionName, connectionName: null, configureDataSource);
    }

    /// <summary>
    /// Registers the default database under a connection name — the same configuration shape as
    /// <c>AddSharedKernelPostgres&lt;TContext&gt;(name)</c>: the connection string from
    /// <c>ConnectionStrings:{<paramref name="connectionStringName"/>}</c> (the .NET Aspire and Testcontainers
    /// convention) and every other setting from <c>SharedKernel:Persistence:{<paramref name="connectionStringName"/>}</c>
    /// (see <see cref="NpgsqlPersistenceOptions"/>), where a <c>ConnectionString</c> key overrides
    /// <c>ConnectionStrings</c>. Registers what the unnamed overload registers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The root configuration.</param>
    /// <param name="connectionStringName">The connection name, e.g. <c>"orders"</c>.</param>
    /// <param name="configureDataSource">See the unnamed overload.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    public static IServiceCollection AddSharedKernelNpgsql(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName,
        Action<IServiceProvider, NpgsqlDataSourceBuilder>? configureDataSource = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        return RegisterDefault(
            services, configuration, NpgsqlPersistenceOptions.SectionFor(connectionStringName), connectionStringName, configureDataSource);
    }

    private static IServiceCollection RegisterDefault(
        IServiceCollection services,
        IConfiguration configuration,
        string sectionPath,
        string? connectionName,
        Action<IServiceProvider, NpgsqlDataSourceBuilder>? configureDataSource)
    {
        RegisterOptions(services, configuration.GetSection(sectionPath), name: null);
        services.AddOptions<NpgsqlPersistenceOptions>()
            .Configure(options =>
            {
                options.SectionPath = sectionPath;
                if (connectionName is not null)
                    options.ConnectionStringName ??= connectionName;
            })
            .PostConfigure(options => ResolveConnectionString(options, configuration));

        RegisterDefaultDatabase(services, configureDataSource);
        return services;
    }

    /// <summary>
    /// Registers a second, independently configured database under the service key
    /// <paramref name="name"/>: a keyed <see cref="NpgsqlDataSource"/> and <see cref="IDbConnectionFactory"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="section">The configuration section of this database's <see cref="NpgsqlPersistenceOptions"/>.</param>
    /// <param name="name">The service key (and options name).</param>
    /// <param name="configureDataSource">See the primary overload.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <remarks>
    /// Secondary data sources, locks, the tenant binder and the row-level security check belong to the
    /// default database only. <see cref="NpgsqlPersistenceOptions.ConnectionStringName"/> is resolved
    /// against the <see cref="IConfiguration"/> registered in the container.
    /// </remarks>
    public static IServiceCollection AddSharedKernelNpgsql(
        this IServiceCollection services,
        IConfigurationSection section,
        string name,
        Action<IServiceProvider, NpgsqlDataSourceBuilder>? configureDataSource = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RegisterOptions(services, section, name);
        services.AddOptions<NpgsqlPersistenceOptions>(name)
            .Configure(options => options.SectionPath = section.Path)
            .PostConfigure<IServiceProvider>((options, sp) => ResolveConnectionString(options, sp.GetService<IConfiguration>()));

        services.AddKeyedSingleton(name, (sp, key) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<NpgsqlPersistenceOptions>>().Get((string)key!);
            return BuildDataSource(options, options.ConnectionString, (string)key!, sp, configureDataSource);
        });

        services.AddKeyedSingleton<IDbConnectionFactory>(name, (sp, key) =>
            new NpgsqlConnectionFactory(sp.GetRequiredKeyedService<NpgsqlDataSource>(key)));

        return services;
    }

    private static void RegisterOptions(IServiceCollection services, IConfigurationSection section, string? name)
    {
        services.AddValidatedOptions<NpgsqlPersistenceOptions, NpgsqlPersistenceOptionsValidator>(
            section,
            validateDataAnnotations: true,
            name: name);
    }

    private static void ResolveConnectionString(NpgsqlPersistenceOptions options, IConfiguration? configuration)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString) || string.IsNullOrWhiteSpace(options.ConnectionStringName))
            return;

        options.ConnectionString = configuration?.GetConnectionString(options.ConnectionStringName) ?? string.Empty;
    }

    private static void RegisterDefaultDatabase(
        IServiceCollection services,
        Action<IServiceProvider, NpgsqlDataSourceBuilder>? configureDataSource)
    {
        services.TryAddSingleton(sp =>
        {
            var options = DefaultOptions(sp);
            return BuildDataSource(options, options.ConnectionString, DefaultDataSourceName, sp, configureDataSource);
        });

        services.TryAddSingleton<IDbConnectionFactory>(sp =>
            new NpgsqlConnectionFactory(sp.GetRequiredService<NpgsqlDataSource>()));

        // Secondary data sources are registered unconditionally; a factory returns null (GetKeyedService
        // yields null) when its connection string is not configured.
        RegisterOptionalDataSource(services, NpgsqlDataSourceKeys.Migration, o => o.MigrationConnectionString, configureDataSource);
        RegisterOptionalDataSource(services, NpgsqlDataSourceKeys.ReadOnly, o => o.ReadOnlyConnectionString, configureDataSource);
        RegisterOptionalDataSource(services, NpgsqlDataSourceKeys.CrossTenant, o => o.RowLevelSecurity.CrossTenantConnectionString, configureDataSource);

        services.TryAddKeyedSingleton<IDbConnectionFactory>(NpgsqlDataSourceKeys.ReadOnly, (sp, _) =>
            new NpgsqlConnectionFactory(ResolveReadOnlyDataSource(sp)));

        services.TryAddKeyedSingleton<IDbConnectionFactory>(NpgsqlDataSourceKeys.CrossTenant, (sp, _) =>
            sp.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.CrossTenant) is { } crossTenant
                ? new NpgsqlConnectionFactory(crossTenant)
                : null!);

        services.TryAddSingleton<IMigrationLock>(sp => new NpgsqlAdvisoryMigrationLock(
            sp.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.Migration) ?? sp.GetRequiredService<NpgsqlDataSource>(),
            sp.GetService<ILogger<NpgsqlAdvisoryMigrationLock>>()));
        services.TryAddSingleton<ITenantSessionBinder, NpgsqlTenantSessionBinder>();
        services.TryAddSingleton<IAdvisoryTransactionLock, NpgsqlAdvisoryTransactionLock>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RowLevelSecurityStartupCheck>());
    }

    private static void RegisterOptionalDataSource(
        IServiceCollection services,
        string key,
        Func<NpgsqlPersistenceOptions, string?> connectionString,
        Action<IServiceProvider, NpgsqlDataSourceBuilder>? configureDataSource)
    {
        services.TryAddKeyedSingleton<NpgsqlDataSource>(key, (sp, _) =>
        {
            var options = DefaultOptions(sp);
            return connectionString(options) is { Length: > 0 } value
                ? BuildDataSource(options, value, key, sp, configureDataSource)
                : null!;
        });
    }

    // Read-only traffic: a configured replica, else a standby of a multi-host primary, else the primary.
    private static NpgsqlDataSource ResolveReadOnlyDataSource(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.ReadOnly) is { } replica)
            return replica;

        var primary = serviceProvider.GetRequiredService<NpgsqlDataSource>();
        return primary is NpgsqlMultiHostDataSource multiHost
            ? multiHost.WithTargetSession(TargetSessionAttributes.PreferStandby)
            : primary;
    }

    private static NpgsqlPersistenceOptions DefaultOptions(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<IOptionsMonitor<NpgsqlPersistenceOptions>>()
            .Get(Microsoft.Extensions.Options.Options.DefaultName);

    // Builds one data source: forces Persist Security Info off and the effective SSL mode, applies the
    // server-side timeouts through the startup "Options" keyword, wires logging and the opt-in type
    // mappings, then runs the caller's hook.
    private static NpgsqlDataSource BuildDataSource(
        NpgsqlPersistenceOptions options,
        string connectionString,
        string dataSourceName,
        IServiceProvider serviceProvider,
        Action<IServiceProvider, NpgsqlDataSourceBuilder>? configureDataSource)
    {
        var sslMode = NpgsqlConnectionStringPolicy.EffectiveSslMode(options, connectionString);
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SslMode = sslMode,
            PersistSecurityInfo = false,
        };

        ApplyServerSideTimeouts(connectionStringBuilder, options);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString)
        {
            Name = dataSourceName,
        };

        if (options.EnableDynamicJson)
            dataSourceBuilder.EnableDynamicJson();

        if (options.UseVector)
            dataSourceBuilder.UseVector();

        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        if (loggerFactory is not null)
            dataSourceBuilder.UseLoggerFactory(new NpgsqlCommandLogLevel(loggerFactory));

        if (sslMode < SslMode.VerifyFull && !NpgsqlConnectionStringPolicy.IsLoopback(connectionString))
        {
            var logger = loggerFactory?.CreateLogger(LoggerCategoryName) ?? NullLogger.Instance;
            logger.InsecureSslMode(dataSourceName, sslMode.ToString());
        }

        configureDataSource?.Invoke(serviceProvider, dataSourceBuilder);

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
