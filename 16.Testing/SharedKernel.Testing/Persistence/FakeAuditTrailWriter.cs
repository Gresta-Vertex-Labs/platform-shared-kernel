using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake implementation of <see cref="IAuditTrailWriter"/> (<c>06.Persistence.Abstractions</c>)
/// for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>NOT</b> <see cref="SharedKernel.Testing.Application.FakeAuditTrailWriter"/> — same class name,
/// different namespace; this type fakes the RICH <c>06.Persistence.Abstractions</c> contract (actor/
/// tenant/timestamp/hash-chain all resolved internally), never <c>05.Application.Behaviors</c>'s
/// deliberately smaller local seam. See <c>Application/FakeAuditTrailWriter</c> for that fake.
/// </para>
/// <para>
/// <b>Structural immutability:</b> this type — and <see cref="IAuditTrailWriter"/> itself — exposes
/// no member anywhere that updates or deletes an existing <see cref="AuditRecord"/>. There is nothing
/// to "reject" at a call site because no such call site exists; every accessor returning recorded
/// records (<see cref="Records"/>, and <see cref="FakeAuditQueryService"/>'s query members) returns a
/// defensive snapshot copy, so mutating a returned collection can never corrupt the backing store
/// either. <see cref="Seed"/> is the one test-setup escape hatch, deliberately shaped as an
/// additive/replace-the-whole-store operation (mirroring <c>Persistence/FakeRepository{TAggregate,TId}.Seed</c>'s
/// precedent) — never a per-record update — used only to construct a deliberately-broken hash chain
/// for <see cref="FakeAuditQueryService.VerifyChainIntegrityAsync"/> tests.
/// </para>
/// <para>
/// Computes a deterministic, NON-cryptographic hash chain (<see cref="System.HashCode"/>-derived hex
/// string) scoped per <c>(TenantId, ResourceType)</c> partition, matching the real implementation's
/// partition scoping — sufficient to prove <c>VerifyChainIntegrityAsync</c>'s tamper-detection
/// contract in tests, but explicitly NOT real SHA-256. This fake takes NO dependency on
/// <c>Cryptography/FakeContentHasher</c> — even though the latter ships exactly the algorithm this
/// type could otherwise reuse — honoring this domain's sibling-capability-folder isolation rule.
/// </para>
/// </remarks>
public sealed class FakeAuditTrailWriter : IAuditTrailWriter
{
    private readonly Lock _gate = new();
    private readonly List<AuditRecord> _records = [];
    private readonly Dictionary<(Guid TenantId, string ResourceType), string> _lastHashByPartition = [];
    private readonly IAuditActorContext _actorContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new <see cref="FakeAuditTrailWriter"/>.</summary>
    /// <param name="actorContext">
    /// Resolves the current actor/tenant identity. Defaults to a fresh <see cref="FakeAuditActorContext"/>
    /// for zero-config convenience when omitted.
    /// </param>
    /// <param name="clock">
    /// Resolves <see cref="AuditRecord.OccurredOn"/>. Defaults to a fresh <see cref="FakeClock"/> when omitted.
    /// </param>
    public FakeAuditTrailWriter(IAuditActorContext? actorContext = null, IClock? clock = null)
    {
        _actorContext = actorContext ?? new FakeAuditActorContext();
        _clock = clock ?? new FakeClock();
    }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="RecordAsync"/> should unconditionally
    /// throw <see cref="InvalidOperationException"/> instead of recording. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>A defensive snapshot of every <see cref="AuditRecord"/> written so far, in write order.</summary>
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
    public Task<AuditRecord> RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (SimulateFailure)
        {
            throw new InvalidOperationException("FakeAuditTrailWriter was configured to simulate a failure.");
        }

        lock (_gate)
        {
            var partitionKey = (_actorContext.TenantId, entry.ResourceType);
            _lastHashByPartition.TryGetValue(partitionKey, out var previousHash);

            var record = new AuditRecord
            {
                Id = Guid.CreateVersion7(),
                TenantId = _actorContext.TenantId,
                ActorId = _actorContext.ActorId,
                Action = entry.Action,
                ResourceType = entry.ResourceType,
                ResourceId = entry.ResourceId,
                OccurredOn = _clock.UtcNow,
                BeforeSnapshot = entry.BeforeSnapshot,
                AfterSnapshot = entry.AfterSnapshot,
                CorrelationId = entry.CorrelationId,
                ApprovalId = entry.ApprovalId,
                RecordHash = string.Empty,
                PreviousRecordHash = previousHash,
            };

            record = record with { RecordHash = ComputeHash(record) };

            _records.Add(record);
            _lastHashByPartition[partitionKey] = record.RecordHash;

            return Task.FromResult(record);
        }
    }

    /// <summary>
    /// Bulk-replaces the backing store with <paramref name="records"/> verbatim — including a
    /// deliberately-inconsistent <see cref="AuditRecord.RecordHash"/>/<see cref="AuditRecord.PreviousRecordHash"/>
    /// chain, if the caller constructs one. Test SETUP only, never code under test — the one way to
    /// exercise <see cref="FakeAuditQueryService.VerifyChainIntegrityAsync"/>'s tamper-detection path
    /// without a public update member on this type (there is none — see the class remarks).
    /// </summary>
    /// <param name="records">The records to seed, replacing the current backing store entirely.</param>
    public void Seed(IEnumerable<AuditRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        lock (_gate)
        {
            _records.Clear();
            _lastHashByPartition.Clear();
            _records.AddRange(records);

            foreach (var record in _records)
            {
                _lastHashByPartition[(record.TenantId, record.ResourceType)] = record.RecordHash;
            }
        }
    }

    /// <summary>Clears the backing store only. Does NOT reset <see cref="SimulateFailure"/>.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _records.Clear();
            _lastHashByPartition.Clear();
        }
    }

    /// <summary>
    /// Computes the deterministic, non-cryptographic digest used both to stamp a newly-written
    /// record's <see cref="AuditRecord.RecordHash"/> and to re-verify an existing record's integrity
    /// in <see cref="FakeAuditQueryService.VerifyChainIntegrityAsync"/> — the SAME function drives
    /// both directions, so a tampered field is always detected by re-running it.
    /// </summary>
    /// <remarks>
    /// Public (not <see langword="internal"/>) specifically so a test can construct a
    /// self-consistent-but-wrongly-linked FORGED record via <see cref="Seed"/> — one whose
    /// <see cref="AuditRecord.RecordHash"/> correctly matches its own fields (including a
    /// deliberately wrong <see cref="AuditRecord.PreviousRecordHash"/>) — proving
    /// <see cref="FakeAuditQueryService.VerifyChainIntegrityAsync"/> catches a broken CHAIN LINK,
    /// not merely a corrupted field, a materially different attack shape a real hash chain must
    /// also guard against.
    /// </remarks>
    public static string ComputeHash(AuditRecord record)
    {
        var hash = new HashCode();
        hash.Add(record.TenantId);
        hash.Add(record.ActorId);
        hash.Add(record.Action);
        hash.Add(record.ResourceType);
        hash.Add(record.ResourceId);
        hash.Add(record.OccurredOn);
        hash.Add(record.BeforeSnapshot);
        hash.Add(record.AfterSnapshot);
        hash.Add(record.CorrelationId);
        hash.Add(record.ApprovalId);
        hash.Add(record.PreviousRecordHash);

        return hash.ToHashCode().ToString("x8");
    }
}
