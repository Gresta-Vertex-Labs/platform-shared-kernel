using System.Diagnostics;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake implementation of the shared <see cref="IAuditTrailWriter"/> that produces full
/// <see cref="AuditRecord"/>s (actor, tenant, sequence, hash chain) for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Differs from <see cref="SharedKernel.Testing.Application.FakeAuditTrailWriter"/> (same interface):
/// that fake records the caller's <see cref="AuditEntry"/> verbatim; this one resolves identity from an
/// <see cref="IRequestContext"/> and builds the ledger records <see cref="FakeAuditQueryService"/> reads.
/// </para>
/// <para>
/// <b>Structural immutability:</b> this type — and <see cref="IAuditTrailWriter"/> itself — exposes
/// no member anywhere that updates or deletes an existing <see cref="AuditRecord"/>. Every accessor
/// returning recorded records (<see cref="Records"/>, and <see cref="FakeAuditQueryService"/>'s query
/// members) returns a defensive snapshot copy. <see cref="Seed"/> is the one test-setup escape hatch,
/// deliberately shaped as an additive/replace-the-whole-store operation — never a per-record update —
/// used only to construct a deliberately-broken hash chain for
/// <see cref="FakeAuditQueryService.VerifyFullChainAsync"/> tests.
/// </para>
/// <para>
/// Computes a deterministic, NON-cryptographic hash chain (<see cref="System.HashCode"/>-derived hex
/// string) scoped per <c>(TenantId, ResourceType)</c> chain, matching the real implementation's chain
/// scoping and <see cref="AuditRecord.Sequence"/> semantics — sufficient to prove
/// <see cref="FakeAuditQueryService.VerifyFullChainAsync"/>'s tamper-detection contract in tests, but
/// explicitly NOT real HMAC-SHA256. This fake takes NO dependency on
/// <c>Cryptography/FakeContentHasher</c> — even though the latter ships exactly an algorithm this type
/// could otherwise reuse — honoring this domain's sibling-capability-folder isolation rule.
/// </para>
/// <para>
/// <b>Idempotency-key retry-safety:</b> like the real <c>EfAuditTrailWriter</c>, calling
/// <see cref="RecordAsync"/> twice with the same <see cref="AuditEntry.IdempotencyKey"/> for the same
/// chain returns the FIRST recorded record both times, never appending a duplicate.
/// </para>
/// </remarks>
public sealed class FakeAuditTrailWriter : IAuditTrailWriter
{
    private readonly Lock _gate = new();
    private readonly List<AuditRecord> _records = [];
    private readonly Dictionary<string, (long Sequence, string Hash)> _headByChain = [];
    private readonly IRequestContext _requestContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new <see cref="FakeAuditTrailWriter"/>.</summary>
    /// <param name="requestContext">
    /// Resolves the actor, actor kind, tenant, client, session and impersonator. Defaults to a fresh
    /// <see cref="FakeAuditActorContext"/> for zero-config convenience when omitted.
    /// </param>
    /// <param name="clock">
    /// Resolves <see cref="AuditRecord.OccurredOn"/>. Defaults to a fresh <see cref="FakeClock"/> when omitted.
    /// </param>
    public FakeAuditTrailWriter(IRequestContext? requestContext = null, IClock? clock = null)
    {
        _requestContext = requestContext ?? new FakeAuditActorContext();
        _clock = clock ?? new FakeClock();
    }

    /// <summary>Gets or sets the value recorded as <see cref="AuditRecord.SourceService"/>. Defaults to <see langword="null"/>.</summary>
    public string? SourceService { get; set; }

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
    Task IAuditTrailWriter.RecordAsync(AuditEntry entry, CancellationToken cancellationToken) =>
        RecordAsync(entry, cancellationToken);

    /// <summary>Records <paramref name="entry"/> and returns the resulting <see cref="AuditRecord"/>.</summary>
    /// <param name="entry">The entry to record.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The recorded (or, for a reused idempotency key, the previously recorded) record.</returns>
    public Task<AuditRecord> RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (SimulateFailure)
        {
            throw new InvalidOperationException("FakeAuditTrailWriter was configured to simulate a failure.");
        }

        lock (_gate)
        {
            var tenantId = _requestContext.TenantId;
            var chainKey = BuildChainKey(tenantId, entry.ResourceType);

            if (entry.IdempotencyKey is { } idempotencyKey)
            {
                var existing = _records.FirstOrDefault(r =>
                    BuildChainKey(r.TenantId, r.ResourceType) == chainKey && r.IdempotencyKey == idempotencyKey);
                if (existing is not null)
                    return Task.FromResult(existing);
            }

            _headByChain.TryGetValue(chainKey, out var head);
            var sequence = head.Sequence + 1;

            var record = new AuditRecord
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                ActorId = _requestContext.UserId ?? "system",
                ActorKind = _requestContext.ActorKind,
                Action = entry.Action,
                ResourceType = entry.ResourceType,
                ResourceId = entry.ResourceId,
                Sequence = sequence,
                OccurredOn = _clock.UtcNow,
                BeforeSnapshot = entry.BeforeSnapshot,
                AfterSnapshot = entry.AfterSnapshot,
                CorrelationId = Activity.Current?.GetBaggageItem(WellKnownBaggageKeys.CorrelationId),
                ApprovalId = entry.ApprovalId,
                Outcome = entry.Outcome,
                ErrorCode = entry.ErrorCode,
                ClientId = _requestContext.ClientId,
                SessionId = _requestContext.SessionId,
                ImpersonatorId = _requestContext.ImpersonatorId,
                SourceService = SourceService,
                IdempotencyKey = entry.IdempotencyKey,
                HashAlgorithm = "FAKE-NONCRYPTOGRAPHIC",
                SchemaVersion = 1,
                KeyId = "fake",
                RecordHash = string.Empty,
                PreviousRecordHash = sequence == 1 ? null : head.Hash,
            };

            record = record with { RecordHash = ComputeHash(record) };

            _records.Add(record);
            _headByChain[chainKey] = (sequence, record.RecordHash);

            return Task.FromResult(record);
        }
    }

    /// <summary>
    /// Bulk-replaces the backing store with <paramref name="records"/> verbatim — including a
    /// deliberately-inconsistent <see cref="AuditRecord.RecordHash"/>/<see cref="AuditRecord.PreviousRecordHash"/>/
    /// <see cref="AuditRecord.Sequence"/> chain, if the caller constructs one. Test SETUP only, never
    /// code under test — the one way to exercise
    /// <see cref="FakeAuditQueryService.VerifyFullChainAsync"/>'s tamper-detection path without a
    /// public update member on this type (there is none — see the class remarks).
    /// </summary>
    /// <param name="records">The records to seed, replacing the current backing store entirely.</param>
    public void Seed(IEnumerable<AuditRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        lock (_gate)
        {
            _records.Clear();
            _headByChain.Clear();
            _records.AddRange(records);

            foreach (var record in _records)
            {
                var chainKey = BuildChainKey(record.TenantId, record.ResourceType);
                if (!_headByChain.TryGetValue(chainKey, out var head) || record.Sequence > head.Sequence)
                    _headByChain[chainKey] = (record.Sequence, record.RecordHash);
            }
        }
    }

    /// <summary>Clears the backing store only. Does NOT reset <see cref="SimulateFailure"/>.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _records.Clear();
            _headByChain.Clear();
        }
    }

    /// <summary>Builds the internal, non-nullable chain key a <c>(TenantId, ResourceType)</c> pair maps to — mirrors <c>AuditChainKeyFormat</c>'s production shape.</summary>
    internal static string BuildChainKey(Guid? tenantId, string resourceType) =>
        $"{(tenantId is { } id ? id.ToString("D") : "system")}|{resourceType}";

    /// <summary>
    /// Computes the deterministic, non-cryptographic digest used both to stamp a newly-written
    /// record's <see cref="AuditRecord.RecordHash"/> and to re-verify an existing record's integrity
    /// in <see cref="FakeAuditQueryService.VerifyFullChainAsync"/> — the SAME function drives both
    /// directions, so a tampered field is always detected by re-running it.
    /// </summary>
    /// <remarks>
    /// Public (not <see langword="internal"/>) specifically so a test can construct a
    /// self-consistent-but-wrongly-linked FORGED record via <see cref="Seed"/> — one whose
    /// <see cref="AuditRecord.RecordHash"/> correctly matches its own fields (including a
    /// deliberately wrong <see cref="AuditRecord.PreviousRecordHash"/>) — proving
    /// <see cref="FakeAuditQueryService.VerifyFullChainAsync"/> catches a broken CHAIN LINK, not merely
    /// a corrupted field.
    /// </remarks>
    public static string ComputeHash(AuditRecord record)
    {
        var hash = new HashCode();
        hash.Add(record.TenantId);
        hash.Add(record.ActorId);
        hash.Add(record.ActorKind);
        hash.Add(record.Action);
        hash.Add(record.ResourceType);
        hash.Add(record.ResourceId);
        hash.Add(record.Sequence);
        hash.Add(record.OccurredOn);
        hash.Add(record.BeforeSnapshot);
        hash.Add(record.AfterSnapshot);
        hash.Add(record.CorrelationId);
        hash.Add(record.ApprovalId);
        hash.Add(record.Outcome);
        hash.Add(record.ErrorCode);
        hash.Add(record.ClientId);
        hash.Add(record.SessionId);
        hash.Add(record.ImpersonatorId);
        hash.Add(record.SourceService);
        hash.Add(record.IdempotencyKey);
        hash.Add(record.PreviousRecordHash);

        return hash.ToHashCode().ToString("x8");
    }
}
