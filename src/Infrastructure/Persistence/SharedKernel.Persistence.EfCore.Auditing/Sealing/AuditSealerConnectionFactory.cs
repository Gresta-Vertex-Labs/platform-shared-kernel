using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.Persistence.EfCore.Auditing.Sealing;

/// <summary>
/// The connections the sealer writes links and checkpoints on: the keyed <see cref="IDbConnectionFactory"/> named by
/// <see cref="AuditSealerOptions.DataSourceName"/> (a separate sealer role), or the application's default one.
/// </summary>
/// <remarks>
/// With a separate sealer role, the application role keeps only <c>SELECT</c> on <c>audit_chain_links</c> and
/// <c>audit_checkpoints</c>: code running as the application — a bug, an injected statement — can then neither forge a
/// link nor a checkpoint. Register the sealer's data source with
/// <c>services.AddSharedKernelNpgsql(configuration.GetSection("SharedKernel:Persistence:audit-sealer"), "audit-sealer")</c>
/// (any service key) and set <c>Sealer:DataSourceName</c> to that key.
/// </remarks>
internal sealed class AuditSealerConnectionFactory : IDbConnectionFactory
{
    private readonly Lazy<IDbConnectionFactory> _inner;

    public AuditSealerConnectionFactory(IServiceProvider services, IOptions<AuditLedgerOptions> options)
    {
        DataSourceName = options.Value.Sealer.DataSourceName;
        var name = DataSourceName;
        _inner = new Lazy<IDbConnectionFactory>(() => name is null
            ? services.GetRequiredService<IDbConnectionFactory>()
            : services.GetKeyedService<IDbConnectionFactory>(name)
                ?? throw new InvalidOperationException(
                    $"'{AuditLedgerOptions.SectionName}:Sealer:DataSourceName' is '{name}', but no IDbConnectionFactory is " +
                    $"registered under that key. Register the sealer role's database with " +
                    $"'services.AddSharedKernelNpgsql(configuration.GetSection(\"SharedKernel:Persistence:{name}\"), \"{name}\")'."));
    }

    /// <summary>Gets the service key of the sealer's own data source, or <see langword="null"/> for the application's.</summary>
    public string? DataSourceName { get; }

    /// <summary>Gets whether the sealer connects as a role of its own.</summary>
    public bool IsSeparate => DataSourceName is not null;

    public Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default) =>
        _inner.Value.CreateConnectionAsync(cancellationToken);
}
