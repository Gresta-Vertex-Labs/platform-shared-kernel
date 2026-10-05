using Microsoft.Extensions.Configuration;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// Builds the <see cref="IConfiguration"/> that <c>AddSharedKernelNpgsql(configuration)</c> binds, for a test
/// container's connection string.
/// </summary>
/// <remarks>
/// Test containers serve plain TCP, so the configuration sets <c>SslMode=Disable</c> together with the
/// explicit <c>AcknowledgeInsecureSslMode</c> opt-down the options validator requires.
/// </remarks>
public static class TestNpgsqlConfiguration
{
    private const string Section = "SharedKernel:Persistence:Npgsql";

    /// <summary>Creates the configuration.</summary>
    /// <param name="connectionString">The container's connection string.</param>
    /// <param name="useVector">Whether to enable pgvector on the shared data source.</param>
    /// <param name="additionalSettings">Further <c>SharedKernel:Persistence:Npgsql:*</c> keys (key relative to the section).</param>
    /// <returns>An in-memory configuration root.</returns>
    public static IConfiguration Create(
        string connectionString,
        bool useVector = false,
        IReadOnlyDictionary<string, string?>? additionalSettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var values = new Dictionary<string, string?>
        {
            [$"{Section}:ConnectionString"] = connectionString,
            [$"{Section}:SslMode"] = "Disable",
            [$"{Section}:AcknowledgeInsecureSslMode"] = "true",
            [$"{Section}:UseVector"] = useVector ? "true" : "false",
        };

        if (additionalSettings is not null)
        {
            foreach (var (key, value) in additionalSettings)
                values[$"{Section}:{key}"] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
