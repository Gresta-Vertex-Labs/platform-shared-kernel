using System.Data.Common;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Persistence.Npgsql.Options;

namespace SharedKernel.Persistence.EfCore.Extensions;

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
/// <strong>TLS:</strong> <c>VerifyFull</c> by default. An <c>SSL Mode</c> written in the connection string is honored
/// as an explicit choice. A loopback host (or Unix socket) without an explicit mode uses <c>Disable</c>, and in the
/// Development environment a weaker mode needs no extra acknowledgement — local containers work with no TLS setup,
/// production cannot downgrade by accident.
/// </para>
/// </remarks>
internal static class PostgresDataSources
{
    private const string SectionPrefix = "SharedKernel:Persistence:";

    /// <summary>Registers (or reuses) the data source of <paramref name="name"/> and returns its key (<see langword="null"/> = default).</summary>
    public static string? Register(
        IServiceCollection services,
        IConfiguration? configuration,
        string name,
        bool isDevelopment,
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

        if (isDefault)
        {
            var alreadyRegistered = services.Any(sd => sd.ServiceType == typeof(NpgsqlDataSource) && !sd.IsKeyedService);
            if (!alreadyRegistered)
            {
                services.AddSharedKernelNpgsql(RequireConfiguration(configuration, name), configureDataSource);
                AddOverlay(services, configuration!, name, Microsoft.Extensions.Options.Options.DefaultName, isDevelopment, bindNamedSection: true);
            }
        }
        else
        {
            var section = RequireConfiguration(configuration, name).GetSection(SectionPrefix + name);
            services.AddSharedKernelNpgsql(section, name, configureDataSource);
            AddOverlay(services, configuration!, name, name, isDevelopment, bindNamedSection: false);
        }

        registry.Keys[name] = key;
        return key;
    }

    /// <summary>Applies the connection-string fallback and the TLS policy to the named options.</summary>
    internal static void ApplySslPolicy(NpgsqlPersistenceOptions options, bool sslModeConfigured, bool isDevelopment)
    {
        if (!sslModeConfigured && !string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            var raw = new DbConnectionStringBuilder { ConnectionString = options.ConnectionString };
            var explicitSsl = raw.Keys.Cast<string>()
                .Any(k => string.Equals(k.Replace(" ", string.Empty, StringComparison.Ordinal), "sslmode", StringComparison.OrdinalIgnoreCase));

            var parsed = new NpgsqlConnectionStringBuilder(options.ConnectionString);
            if (explicitSsl)
            {
                options.SslMode = parsed.SslMode;
                options.AcknowledgeInsecureSslMode = true;
            }
            else if (IsLoopback(parsed.Host))
            {
                options.SslMode = SslMode.Disable;
                options.AcknowledgeInsecureSslMode = true;
            }
        }

        if (isDevelopment && options.SslMode < SslMode.VerifyFull)
            options.AcknowledgeInsecureSslMode = true;
    }

    internal static bool IsLoopback(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        return host.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(h =>
        {
            var hostOnly = h.StartsWith('[') ? h.TrimStart('[').Split(']')[0] : h.Split(':')[0];
            return hostOnly.StartsWith('/')
                || string.Equals(hostOnly, "localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(hostOnly, out var address) && IPAddress.IsLoopback(address));
        });
    }

    private static void AddOverlay(
        IServiceCollection services,
        IConfiguration configuration,
        string name,
        string optionsName,
        bool isDevelopment,
        bool bindNamedSection)
    {
        services.AddOptions<NpgsqlPersistenceOptions>(optionsName)
            .PostConfigure(options =>
            {
                var section = configuration.GetSection(SectionPrefix + name);
                if (bindNamedSection)
                    section.Bind(options);

                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                    options.ConnectionString = configuration.GetConnectionString(name) ?? string.Empty;

                var sslModeConfigured = section[nameof(NpgsqlPersistenceOptions.SslMode)] is not null
                    || (bindNamedSection && configuration[NpgsqlPersistenceOptions.SectionName + ":" + nameof(NpgsqlPersistenceOptions.SslMode)] is not null);

                ApplySslPolicy(options, sslModeConfigured, isDevelopment);
            })
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"No connection string for '{name}': set 'ConnectionStrings:{name}' (or '{SectionPrefix}{name}:ConnectionString').");
    }

    private static IConfiguration RequireConfiguration(IConfiguration? configuration, string name) =>
        configuration ?? throw new InvalidOperationException(
            $"No configuration to read 'ConnectionStrings:{name}' from. Pass the application's IConfiguration, or call UseDataSource(...).");

    private sealed class Registry
    {
        public Dictionary<string, string?> Keys { get; } = new(StringComparer.Ordinal);
    }
}
