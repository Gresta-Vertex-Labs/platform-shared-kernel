using global::Npgsql;

namespace SharedKernel.Persistence.Npgsql.Options;

/// <summary>
/// Wires a periodically-refreshed password provider (e.g. an Entra ID or AWS RDS IAM token) into the
/// data source <c>AddSharedKernelNpgsql</c> builds, via Npgsql's own
/// <see cref="NpgsqlDataSourceBuilder.UsePeriodicPasswordProvider"/>.
/// </summary>
/// <remarks>
/// The connection string's own credential fields are ignored for authentication once this
/// is supplied — Npgsql calls <see cref="Provider"/> to obtain the password used for every new
/// physical connection, refreshing it on the schedule below.
/// </remarks>
public sealed class NpgsqlPeriodicPasswordProviderOptions
{
    /// <summary>
    /// Produces the current password. Invoked on the schedule <see cref="SuccessRefreshInterval"/>/
    /// <see cref="FailureRefreshInterval"/> describe, never once per connection.
    /// </summary>
    public required Func<NpgsqlConnectionStringBuilder, CancellationToken, ValueTask<string>> Provider { get; init; }

    /// <summary>How often to refresh the password after a successful fetch.</summary>
    public required TimeSpan SuccessRefreshInterval { get; init; }

    /// <summary>How soon to retry after a failed fetch.</summary>
    public required TimeSpan FailureRefreshInterval { get; init; }
}
