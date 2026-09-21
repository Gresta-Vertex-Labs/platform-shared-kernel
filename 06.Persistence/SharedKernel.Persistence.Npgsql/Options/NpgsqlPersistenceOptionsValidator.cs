using global::Npgsql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.Npgsql.Options;

/// <summary>
/// Validates <see cref="NpgsqlPersistenceOptions"/> at host startup: a connection string is present and
/// parses, every connection string's TLS mode is acceptable, and row-level security is not combined with
/// connection settings that defeat it.
/// </summary>
/// <remarks>
/// A TLS mode below <c>VerifyFull</c> is accepted for a loopback host, in the <c>Development</c>
/// environment, or with <see cref="NpgsqlPersistenceOptions.AcknowledgeInsecureSslMode"/>. With
/// row-level security enabled, <c>Multiplexing</c> and <c>No Reset On Close</c> are rejected on the
/// application connection strings (default and read-only).
/// </remarks>
internal sealed class NpgsqlPersistenceOptionsValidator : IValidateOptions<NpgsqlPersistenceOptions>
{
    private readonly IHostEnvironment? _hostEnvironment;

    /// <summary>Initialises a new <see cref="NpgsqlPersistenceOptionsValidator"/>.</summary>
    /// <param name="hostEnvironment">
    /// Optional. When it reports <c>Development</c>, a TLS downgrade needs no acknowledgement.
    /// </param>
    public NpgsqlPersistenceOptionsValidator(IHostEnvironment? hostEnvironment = null)
    {
        _hostEnvironment = hostEnvironment;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, NpgsqlPersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add(options.ConnectionStringName is { Length: > 0 } connectionStringName
                ? $"No connection string was found: 'ConnectionStrings:{connectionStringName}' is not configured "
                    + $"(nor '{options.SectionPath}:{nameof(NpgsqlPersistenceOptions.ConnectionString)}')."
                : $"No connection string was found: set '{options.SectionPath}:{nameof(NpgsqlPersistenceOptions.ConnectionString)}' or "
                    + $"'{options.SectionPath}:{nameof(NpgsqlPersistenceOptions.ConnectionStringName)}'.");
            return ValidateOptionsResult.Fail(failures);
        }

        ValidateConnectionString(options, nameof(NpgsqlPersistenceOptions.ConnectionString), options.ConnectionString, checkRowLevelSecurity: true, failures);
        ValidateConnectionString(options, nameof(NpgsqlPersistenceOptions.ReadOnlyConnectionString), options.ReadOnlyConnectionString, checkRowLevelSecurity: true, failures);
        ValidateConnectionString(options, nameof(NpgsqlPersistenceOptions.MigrationConnectionString), options.MigrationConnectionString, checkRowLevelSecurity: false, failures);
        ValidateConnectionString(options, "RowLevelSecurity:CrossTenantConnectionString", options.RowLevelSecurity.CrossTenantConnectionString, checkRowLevelSecurity: false, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateConnectionString(
        NpgsqlPersistenceOptions options,
        string settingName,
        string? connectionString,
        bool checkRowLevelSecurity,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        try
        {
            _ = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // The exception text can echo parts of the connection string, so it is not included.
            failures.Add($"'{settingName}' is not a valid PostgreSQL connection string.");
            return;
        }

        var sslMode = NpgsqlConnectionStringPolicy.EffectiveSslMode(options, connectionString);
        if (sslMode < SslMode.VerifyFull
            && !options.AcknowledgeInsecureSslMode
            && !NpgsqlConnectionStringPolicy.IsLoopback(connectionString)
            && _hostEnvironment?.IsDevelopment() != true)
        {
            failures.Add(
                $"'{settingName}' uses SSL mode '{sslMode}', below the secure default '{SslMode.VerifyFull}', for a "
                    + "host that is not on this machine. Install the server's root certificate and keep "
                    + $"'{SslMode.VerifyFull}', or set '{nameof(NpgsqlPersistenceOptions.AcknowledgeInsecureSslMode)}' "
                    + "to true to accept the downgrade.");
        }

        if (checkRowLevelSecurity && options.RowLevelSecurity.Enabled)
        {
            foreach (var incompatibility in NpgsqlConnectionStringPolicy.RowLevelSecurityIncompatibilities(connectionString))
                failures.Add($"Row-level security is enabled but '{settingName}' is incompatible: {incompatibility}");
        }
    }
}
