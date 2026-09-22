using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.Npgsql.Options;

namespace SharedKernel.Persistence;

/// <summary>
/// Maps connection names to the shared <see cref="NpgsqlDataSource"/> registrations of
/// <c>SharedKernel.Persistence.Npgsql</c>: one data source per connection name, shared by every context (and by
/// Dapper) that uses the same name.
/// </summary>
/// <remarks>
/// <para>
/// The first name registered becomes the default (unkeyed) data source — the one Dapper, the migration lock,
/// the tenant session binder and the advisory lock use. If an unkeyed data source is already registered (the
/// service called <c>AddSharedKernelNpgsql</c> itself), it is reused. Further names get a keyed data source.
/// </para>
/// <para>
/// <strong>Configuration:</strong> the connection string comes from <c>ConnectionStrings:{name}</c> (the Aspire and
/// Testcontainers convention) unless <c>SharedKernel:Persistence:{name}:ConnectionString</c> sets it; every other
/// <c>NpgsqlPersistenceOptions</c> setting (timeouts, <c>UseVector</c>, <c>SslMode</c>, ...) is read from
/// <c>SharedKernel:Persistence:{name}</c>.
/// </para>
/// <para>
/// <strong>TLS and validation</strong> are <c>SharedKernel.Persistence.Npgsql</c>'s single implementation
/// (<c>NpgsqlPersistenceOptions</c>): an explicit <c>SSL Mode</c> in the connection string is honored, a loopback
/// host without one uses <c>Disable</c>, anything else defaults to <c>VerifyFull</c>; a downgrade for a remote host
/// needs the Development environment or an explicit acknowledgement. A missing connection string fails at startup
/// naming <c>ConnectionStrings:{name}</c>.
/// </para>
/// </remarks>
internal static class PostgresDataSources
{
    private const string SectionPrefix = "SharedKernel:Persistence:";

    /// <summary>Registers (or reuses) the data source of <paramref name="name"/> and returns its key (<see langword="null"/> = default).</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The root configuration.</param>
    /// <param name="name">The connection name.</param>
    /// <param name="configureDataSource">Optional data-source hook.</param>
    /// <returns>The service key of the data source, or <see langword="null"/> for the default one.</returns>
    public static string? Register(
        IServiceCollection services,
        IConfiguration? configuration,
        string name,
        Action<IServiceProvider, NpgsqlDataSourceBuilder>? configureDataSource)
    {
        var registry = services
            .Where(sd => sd.ServiceType == typeof(Registry))
            .Select(sd => sd.ImplementationInstance)
            .OfType<Registry>()
            .FirstOrDefault();

        if (registry is null)
        {
            registry = new Registry();
            services.AddSingleton(registry);
        }

        if (registry.Keys.TryGetValue(name, out var existingKey))
            return existingKey;

        var isDefault = !registry.Keys.ContainsValue(null);
        var key = isDefault ? null : name;
        var section = RequireConfiguration(configuration, name).GetSection(SectionPrefix + name);

        if (isDefault)
        {
            var alreadyRegistered = services.Any(sd => sd.ServiceType == typeof(NpgsqlDataSource) && !sd.IsKeyedService);
            if (!alreadyRegistered)
            {
                // Reads ConnectionStrings:{name} and SharedKernel:Persistence:{name} itself.
                services.AddSharedKernelNpgsql(configuration!, name, configureDataSource);
            }
        }
        else
        {
            // Resolved against the configuration passed here, not the container's (which may have none).
            services.AddOptions<NpgsqlPersistenceOptions>(name).Configure(options =>
            {
                options.ConnectionStringName ??= name;
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                    options.ConnectionString = configuration!.GetConnectionString(name) ?? string.Empty;
            });
            services.AddSharedKernelNpgsql(section, name, configureDataSource);
        }

        registry.Keys[name] = key;
        return key;
    }

    private static IConfiguration RequireConfiguration(IConfiguration? configuration, string name) =>
        configuration ?? throw new InvalidOperationException(
            $"No configuration to read 'ConnectionStrings:{name}' from. Pass the application's IConfiguration, or call UseDataSource(...).");

    private sealed class Registry
    {
        public Dictionary<string, string?> Keys { get; } = new(StringComparer.Ordinal);
    }
}
