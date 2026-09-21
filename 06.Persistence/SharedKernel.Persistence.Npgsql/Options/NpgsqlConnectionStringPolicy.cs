using System.Data.Common;
using System.Net;
using global::Npgsql;

namespace SharedKernel.Persistence.Npgsql.Options;

/// <summary>
/// The TLS and row-level-security rules shared by <see cref="NpgsqlPersistenceOptionsValidator"/> (at
/// startup) and the data source builder (when the data source is created), so both see the same answer.
/// </summary>
internal static class NpgsqlConnectionStringPolicy
{
    /// <summary>The SSL mode a connection string will actually use under <paramref name="options"/>.</summary>
    public static SslMode EffectiveSslMode(NpgsqlPersistenceOptions options, string connectionString)
    {
        if (options.SslMode is { } configured)
            return configured;

        if (HasExplicitSslMode(connectionString))
            return new NpgsqlConnectionStringBuilder(connectionString).SslMode;

        return SslMode.VerifyFull;
    }

    /// <summary>
    /// Whether every host of <paramref name="connectionString"/> is on this machine: <c>localhost</c>, a
    /// loopback address, or a Unix-domain socket directory.
    /// </summary>
    public static bool IsLoopback(string connectionString)
    {
        var hosts = new NpgsqlConnectionStringBuilder(connectionString).Host;
        if (string.IsNullOrWhiteSpace(hosts))
            return false;

        foreach (var entry in hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsLoopbackHost(StripPort(entry)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The row-level-security incompatibilities of <paramref name="connectionString"/>, empty when there
    /// are none.
    /// </summary>
    public static IEnumerable<string> RowLevelSecurityIncompatibilities(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        if (builder.Multiplexing)
        {
            yield return "'Multiplexing' is enabled: statements of different callers share one physical "
                + "connection, so a transaction-local tenant binding cannot be relied on.";
        }

        if (builder.NoResetOnClose)
        {
            yield return "'No Reset On Close' is enabled: session state is not reset when a connection "
                + "returns to the pool.";
        }
    }

    // NpgsqlConnectionStringBuilder reports every known keyword as present, so presence is checked on a
    // plain builder, whose keys are exactly what the string contained.
    private static bool HasExplicitSslMode(string connectionString)
    {
        var raw = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return raw.ContainsKey("SSL Mode") || raw.ContainsKey("SslMode");
    }

    private static string StripPort(string host)
    {
        if (host.StartsWith('['))
        {
            var end = host.IndexOf(']', StringComparison.Ordinal);
            return end > 0 ? host[1..end] : host;
        }

        var colon = host.LastIndexOf(':');
        return colon > 0 && host.IndexOf(':', StringComparison.Ordinal) == colon ? host[..colon] : host;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.StartsWith('/') || host.StartsWith('@'))
            return true; // Unix-domain socket directory or abstract socket

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
