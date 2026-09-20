using AppAuditEntry = SharedKernel.Application.Behaviors.Auditing.AuditEntry;
using AppIAuditTrailWriter = SharedKernel.Application.Behaviors.Auditing.IAuditTrailWriter;
using PersistenceAuditEntry = SharedKernel.Persistence.Abstractions.Auditing.AuditEntry;
using PersistenceAuditOutcome = SharedKernel.Persistence.Abstractions.Auditing.AuditOutcome;
using PersistenceIAuditTrailWriter = SharedKernel.Persistence.Abstractions.Auditing.IAuditTrailWriter;

namespace SharedKernel.ServiceDefaults.Persistence.Auditing;

/// <summary>
/// Bridges <c>05.Application.Behaviors</c>'s local, deliberately-smaller
/// <see cref="AppIAuditTrailWriter"/> seam (consumed by <c>AuditingBehavior</c>) to the real,
/// richer <c>06.Persistence.Abstractions</c> <see cref="PersistenceIAuditTrailWriter"/> — the same
/// same-name-different-namespace bridge shape as <see cref="SharedKernel.ServiceDefaults.Persistence.UnitOfWork.PersistenceUnitOfWorkAdapter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Lives here — not in either <c>05.Application.Behaviors</c> or <c>06.Persistence</c> —
/// because <c>05.Application</c> can never reference <c>06.Persistence</c> (layering runs the other
/// direction), and <c>06.Persistence</c> must never reference <c>05.Application</c> either (see
/// <c>06.Persistence/CLAUDE.md</c>'s package-graph rules). <c>13.ServiceDefaults</c> — specifically
/// this <c>SharedKernel.ServiceDefaults.Persistence</c> integration package — is the layer legally
/// positioned to reference both.
/// </para>
/// <para>
/// Maps every field <c>05.Application</c>'s smaller <see cref="AppAuditEntry"/> carries onto the real
/// contract: <see cref="AppAuditEntry.Succeeded"/> → <see cref="PersistenceAuditOutcome"/>
/// (<see langword="true"/> → <see cref="PersistenceAuditOutcome.Succeeded"/>,
/// <see langword="false"/> → <see cref="PersistenceAuditOutcome.Failed"/>),
/// <see cref="AppAuditEntry.ErrorCode"/> carried through verbatim. Correlation id is NOT mapped here —
/// the real writer resolves <c>AuditRecord.CorrelationId</c> itself, from ambient
/// <see cref="System.Diagnostics.Activity"/> baggage (see <c>EfAuditTrailWriter</c>'s remarks) — so this
/// bridge has nothing to forward for it. The real writer's returned, fully-resolved
/// <c>AuditRecord</c> is discarded — <see cref="AppIAuditTrailWriter.RecordAsync"/> returns
/// <see cref="Task"/>, not a value, matching <c>05.Application</c>'s own contract exactly (it never
/// needs the persisted record back).
/// </para>
/// </remarks>
public sealed class AuditTrailWriterBridge : AppIAuditTrailWriter
{
    private readonly PersistenceIAuditTrailWriter _inner;

    /// <summary>Initialises a new <see cref="AuditTrailWriterBridge"/> wrapping <paramref name="inner"/>.</summary>
    public AuditTrailWriterBridge(PersistenceIAuditTrailWriter inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public async Task RecordAsync(AppAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var mapped = new PersistenceAuditEntry
        {
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            BeforeSnapshot = entry.BeforeSnapshot,
            AfterSnapshot = entry.AfterSnapshot,
            Outcome = entry.Succeeded ? PersistenceAuditOutcome.Succeeded : PersistenceAuditOutcome.Failed,
            ErrorCode = entry.ErrorCode,
        };

        await _inner.RecordAsync(mapped, cancellationToken).ConfigureAwait(false);
    }
}
