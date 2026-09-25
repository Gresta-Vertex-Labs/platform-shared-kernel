using System.Diagnostics;
using SharedKernel.Execution.Auditing;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake of the shared <see cref="IAuditTrailWriter"/> that produces ledger
/// <see cref="AuditRecord"/>s (identity from an <see cref="IRequestContext"/>, per-chain sequence) for
/// <see cref="FakeAuditQueryService"/> to read.
/// </summary>
/// <remarks>
/// <para>
/// Differs from <see cref="SharedKernel.Testing.Application.FakeAuditTrailWriter"/> (same interface),
/// which records the caller's <see cref="AuditEntry"/> verbatim.
/// </para>
/// <para>
/// Records are sealed immediately: each gets the next <see cref="AuditRecord.Sequence"/> of its
/// <c>(TenantId, ResourceType)</c> chain, with no MAC (the real ledger seals asynchronously with a keyed MAC
/// — test that against the real package). A reused <see cref="AuditEntry.IdempotencyKey"/> in the same chain
/// returns the existing record. <see cref="Records"/> is a snapshot copy.
/// </para>
/// </remarks>
public sealed class FakeAuditTrailWriter : IAuditTrailWriter
{
    private readonly Lock _gate = new();
    private readonly List<AuditRecord> _records = [];
    private readonly IRequestContext _requestContext;
    private readonly IClock _clock;

    /// <summary>Initialises the fake.</summary>
    /// <param name="requestContext">Where identity and tenant come from; defaults to a <see cref="FakeAuditActorContext"/>.</param>
    /// <param name="clock">The time source; defaults to a <see cref="FakeClock"/>.</param>
    public FakeAuditTrailWriter(IRequestContext? requestContext = null, IClock? clock = null)
    {
        _requestContext = requestContext ?? new FakeAuditActorContext();
        _clock = clock ?? new FakeClock();
    }

    /// <summary>Gets or sets the source service recorded on every record. Defaults to <c>"fake-service"</c>.</summary>
    public string SourceService { get; set; } = "fake-service";

    /// <summary>Gets or sets a value indicating whether <see cref="RecordAsync"/> throws instead of recording.</summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Gets a snapshot of every record, in write order.</summary>
    public IReadOnlyList<AuditRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }
    }

    /// <inheritdoc />
    Task IAuditTrailWriter.RecordAsync(AuditEntry entry, CancellationToken cancellationToken) =>
        RecordAsync(entry, cancellationToken);

    /// <summary>Records <paramref name="entry"/> and returns the stored record.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The stored (or, for a reused idempotency key, the existing) record.</returns>
    public Task<AuditRecord> RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (SimulateFailure)
            throw new InvalidOperationException("FakeAuditTrailWriter was configured to simulate a failure.");

        lock (_gate)
        {
            var tenantId = _requestContext.TenantId;

            if (entry.IdempotencyKey is { } key &&
                _records.FirstOrDefault(r => r.TenantId == tenantId && r.ResourceType == entry.ResourceType && r.IdempotencyKey == key) is { } existing)
            {
                return Task.FromResult(existing);
            }

            var sequence = _records.Count(r => r.TenantId == tenantId && r.ResourceType == entry.ResourceType) + 1;
            var now = _clock.UtcNow;
            var activity = Activity.Current;

            var record = new AuditRecord
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                ResourceType = entry.ResourceType,
                ResourceId = entry.ResourceId,
                Action = entry.Action,
                Outcome = entry.Outcome,
                ErrorCode = entry.ErrorCode,
                ActorId = _requestContext.UserId ?? SourceService,
                ActorKind = _requestContext.ActorKind,
                ClientId = _requestContext.ClientId,
                SessionId = _requestContext.SessionId,
                ImpersonatorId = _requestContext.ImpersonatorId,
                SourceService = SourceService,
                CorrelationId = activity?.GetBaggageItem(WellKnownBaggageKeys.CorrelationId),
                TraceId = activity is { IdFormat: ActivityIdFormat.W3C } ? activity.TraceId.ToHexString() : null,
                ApprovalId = entry.ApprovalId,
                IdempotencyKey = entry.IdempotencyKey,
                OccurredOn = now,
                BeforeSnapshot = entry.BeforeSnapshot,
                AfterSnapshot = entry.AfterSnapshot,
                Sequence = sequence,
                SealedOn = now,
                KeyId = "fake",
            };

            _records.Add(record);
            return Task.FromResult(record);
        }
    }

    /// <summary>Simulates payload erasure: clears the snapshots of <paramref name="recordId"/> and flags it erased.</summary>
    /// <param name="recordId">The record.</param>
    /// <returns><see langword="true"/> when the record exists and was not erased yet.</returns>
    public bool ErasePayload(Guid recordId)
    {
        lock (_gate)
        {
            var index = _records.FindIndex(r => r.Id == recordId);
            if (index < 0 || _records[index].PayloadErased)
                return false;

            _records[index] = _records[index] with { BeforeSnapshot = null, AfterSnapshot = null, PayloadErased = true };
            return true;
        }
    }

    /// <summary>Replaces the whole store — the test-setup escape hatch for building a deliberately broken chain.</summary>
    /// <param name="records">The records.</param>
    public void Seed(IEnumerable<AuditRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        lock (_gate)
        {
            _records.Clear();
            _records.AddRange(records);
        }
    }

    /// <summary>Removes every record. <see cref="SimulateFailure"/> is unchanged.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _records.Clear();
        }
    }
}
