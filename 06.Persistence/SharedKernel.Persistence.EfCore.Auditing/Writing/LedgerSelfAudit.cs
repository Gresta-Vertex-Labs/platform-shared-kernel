using System.Data.Common;
using System.Text.Json;
using SharedKernel.Execution.Auditing;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Storage;

namespace SharedKernel.Persistence.EfCore.Auditing.Writing;

/// <summary>
/// Writes the ledger's own records (<see cref="AuditLedgerActions"/>) for sensitive ledger operations:
/// exports, cross-tenant reads, erasure, resealing. These are facts about the ledger, not about a business
/// write, so they are committed on their own (or inside the maintenance operation's own transaction).
/// </summary>
internal sealed class LedgerSelfAudit(AuditRecordFactory factory, IDbConnectionFactory connectionFactory)
{
    public AuditRecordFactory Factory => factory;

    /// <summary>Records <paramref name="action"/> in the chain of <paramref name="tenantId"/>.</summary>
    public async Task<AuditRecord> RecordAsync(
        Guid? tenantId,
        string resourceType,
        string resourceId,
        string action,
        IReadOnlyDictionary<string, string?> details,
        DbConnection? connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var entry = new AuditEntry
        {
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Outcome = AuditOutcome.Succeeded,
            AfterSnapshot = JsonSerializer.Serialize(details, LedgerJsonContext.Default.IReadOnlyDictionaryStringString),
        };

        var pending = factory.Create(entry, tenantId);

        if (connection is not null)
        {
            await LedgerDb.InsertAsync(connection, transaction, pending, cancellationToken).ConfigureAwait(false);
            return EfAuditTrailWriter.ToRecord(pending);
        }

        var owned = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (owned.ConfigureAwait(false))
        {
            await LedgerDb.InsertAsync(owned, null, pending, cancellationToken).ConfigureAwait(false);
            return EfAuditTrailWriter.ToRecord(pending);
        }
    }
}
