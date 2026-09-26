using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Testing.Intelligence;

/// <summary>
/// In-memory test double for <see cref="IVectorCollection{TRecord}"/>. Simulates behavioral
/// correctness (what was upserted/deleted/queried, faithfully-scored similarity ranking) -- not
/// provider timing or transport faults.
/// </summary>
/// <typeparam name="TRecord">The vector record type.</typeparam>
/// <remarks>
/// <para>
/// Backing store is a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by
/// <see cref="IVectorRecord.Id"/>. Every write is upsert-only, matching the real contract's own
/// no-create-vs-update-split rule.
/// </para>
/// <para>
/// <b>The shared in-memory <see cref="VectorFilter"/> evaluator is a direct dictionary lookup -- a
/// deliberate simplification relative to <c>Search/</c>'s reflection-based evaluator:</b>
/// <see cref="VectorFilter"/>'s field resolves via a direct lookup into
/// <see cref="IVectorRecord.Metadata"/> (already keyed by field name) -- no reflection needed at
/// all. A field absent from <see cref="IVectorRecord.Metadata"/> is a non-match for
/// Equal/NotEqual/In/Range and <see langword="false"/> for Exists. The switch expression over the 8
/// <see cref="VectorFilter"/> subtypes carries no discard arm, mirroring the real contract's own
/// "neither translation switch may carry a discard arm" hard rule -- anticipate the resulting
/// cosmetic CS8509 "not exhaustive" warning; it is expected, not a defect.
/// </para>
/// <para>
/// <b><see cref="VectorHit{TRecord}.Score"/>/<c>.Rank</c> are computed faithfully, unlike
/// <c>Search/</c>'s relevance-ranking simplification:</b> vector similarity is closed-form
/// arithmetic this fake computes exactly per <see cref="VectorCollectionDefinition.DistanceMetric"/>
/// -- Cosine and DotProduct rank descending (higher is better); Euclidean ranks ascending (lower is
/// better, the opposite direction). <see cref="VectorQuery.MinScore"/> filtering is applied
/// post-scoring respecting that same directionality. <see cref="VectorQuery.ReturnMetadata"/>/
/// <see cref="VectorQuery.ReturnVector"/> are accepted but are documented no-ops -- the fake always
/// returns the full stored <typeparamref name="TRecord"/> instance regardless of either flag.
/// </para>
/// <para>
/// <c>Intelligence/</c> (this namespace, <c>SharedKernel.Testing.Intelligence</c>) references only
/// <c>SharedKernel.AI.Abstractions</c> -- never the concrete provider packages nor any sibling
/// capability folder in this package. This type is also deliberately independent of its five sibling
/// <c>Intelligence/</c> fakes -- see <see cref="InMemoryVectorCollectionProvisioner"/>'s remarks for
/// the full non-coupling rationale.
/// </para>
/// </remarks>
public sealed class InMemoryVectorCollection<TRecord> : IVectorCollection<TRecord>
    where TRecord : class, IVectorRecord
{
    private static readonly DateTimeOffset FixedAcceptedAt = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly VectorCollectionDefinition _definition;
    private readonly ConcurrentDictionary<string, TRecord> _store = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _upsertedIds = new();
    private readonly ConcurrentQueue<string> _deletedIds = new();
    private readonly ConcurrentQueue<VectorQuery> _queriedVectors = new();
    private long _tokenSequence;

    /// <summary>Initializes a new <see cref="InMemoryVectorCollection{TRecord}"/> for <paramref name="definition"/>.</summary>
    /// <param name="definition">
    /// The registered collection definition. Required -- the fail-loud model-identity/dimension/
    /// tenant-scope checks all need a real declaration to validate against, exactly like a real
    /// adapter validates against the same registered definition before any I/O.
    /// </param>
    public InMemoryVectorCollection(VectorCollectionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
    }

    /// <inheritdoc />
    public string CollectionName => _definition.Name;

    /// <summary>
    /// Gets or sets a value indicating whether write-path operations should simulate a provider
    /// rejection. When <see langword="true"/>, every write-path member's outer call
    /// (<see cref="UpsertAsync"/>/<see cref="UpsertManyAsync"/>/<see cref="DeleteAsync"/>/
    /// <see cref="DeleteManyAsync"/>/<see cref="DeleteByFilterAsync"/>) returns
    /// <c>IntelligenceErrors.WriteRejected</c> instead of performing the operation. Read-path members
    /// and <see cref="WaitUntilQueryableAsync"/> are unaffected.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Gets every <see cref="IVectorRecord.Id"/> ever successfully upserted, append-only, never pruned on delete.</summary>
    public IReadOnlyList<string> UpsertedIds => _upsertedIds.ToArray();

    /// <summary>Gets every <see cref="IVectorRecord.Id"/> ever successfully deleted.</summary>
    public IReadOnlyList<string> DeletedIds => _deletedIds.ToArray();

    /// <summary>
    /// Gets every <see cref="VectorQuery"/> that passed the fail-loud pre-flight pipeline and
    /// executed against the store -- the assertion helper satisfying "was this vector queried". A
    /// rejected query is never recorded here.
    /// </summary>
    public IReadOnlyList<VectorQuery> QueriedVectors => _queriedVectors.ToArray();

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<VectorWriteReceipt>> UpsertAsync(
        TRecord record, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorWriteReceipt>.Failure(
                IntelligenceErrors.WriteRejected(CollectionName, "SimulateFailure enabled")));
        }

        var validationError = ValidateRecord(record);
        if (validationError is not null)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorWriteReceipt>.Failure(validationError));
        }

        _store[record.Id] = record;
        _upsertedIds.Enqueue(record.Id);

        var receipt = BuildReceipt(1);
        return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorWriteReceipt>.Success(receipt));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<VectorBulkReceipt>> UpsertManyAsync(
        IReadOnlyCollection<TRecord> records, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorBulkReceipt>.Failure(
                IntelligenceErrors.WriteRejected(CollectionName, "SimulateFailure enabled")));
        }

        var failures = new List<VectorItemFailure>();
        var succeeded = 0;

        foreach (var record in records)
        {
            var validationError = ValidateRecord(record);
            if (validationError is not null)
            {
                failures.Add(new VectorItemFailure { RecordId = record.Id, Error = validationError });
                continue;
            }

            _store[record.Id] = record;
            _upsertedIds.Enqueue(record.Id);
            succeeded++;
        }

        var receipt = BuildReceipt(succeeded);

        // Partial failure is never collapsed into an outer Result.Failure -- matches
        // VectorBulkReceipt.Failures' own "the request itself did execute" contract.
        return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorBulkReceipt>.Success(new VectorBulkReceipt
        {
            Receipt = receipt,
            SucceededCount = succeeded,
            Failures = failures,
        }));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<VectorWriteReceipt>> DeleteAsync(
        string id, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorWriteReceipt>.Failure(
                IntelligenceErrors.WriteRejected(CollectionName, "SimulateFailure enabled")));
        }

        var affected = 0;
        if (_store.TryRemove(id, out _))
        {
            _deletedIds.Enqueue(id);
            affected = 1;
        }

        // Idempotent -- deleting an absent id still succeeds with AffectedCount=0.
        var receipt = BuildReceipt(affected);
        return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorWriteReceipt>.Success(receipt));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<VectorBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> ids, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorBulkReceipt>.Failure(
                IntelligenceErrors.WriteRejected(CollectionName, "SimulateFailure enabled")));
        }

        var affected = 0;
        foreach (var id in ids)
        {
            if (_store.TryRemove(id, out _))
            {
                _deletedIds.Enqueue(id);
                affected++;
            }
        }

        var receipt = BuildReceipt(affected);

        // Idempotent per-id -- every requested id counts as succeeded whether or not it was present.
        return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorBulkReceipt>.Success(new VectorBulkReceipt
        {
            Receipt = receipt,
            SucceededCount = ids.Count,
            Failures = [],
        }));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<VectorWriteReceipt>> DeleteByFilterAsync(
        VectorFilter filter, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorWriteReceipt>.Failure(
                IntelligenceErrors.WriteRejected(CollectionName, "SimulateFailure enabled")));
        }

        if (_definition.TenantField is not null && tenantScope.IsGlobal)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorWriteReceipt>.Failure(
                IntelligenceErrors.TenantScopeMissing(CollectionName)));
        }

        var effectiveFilter = ApplyTenantScope(filter, tenantScope)!;

        var affected = 0;
        foreach (var id in _store.Keys.ToArray())
        {
            if (_store.TryGetValue(id, out var record) && Evaluate(effectiveFilter, record) && _store.TryRemove(id, out _))
            {
                _deletedIds.Enqueue(id);
                affected++;
            }
        }

        var receipt = BuildReceipt(affected);
        return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorWriteReceipt>.Success(receipt));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result> WaitUntilQueryableAsync(
        VectorWriteReceipt receipt, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        // Every fake write is synchronously and immediately queryable -- there is no provider queue
        // to wait on, so this deterministically succeeds with zero real delay.
        return Task.FromResult(SharedKernel.Primitives.Results.Result.Success());
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<VectorQueryResults<TRecord>>> QueryAsync(
        VectorQuery query, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!string.Equals(query.ModelId, _definition.EmbeddingModelId, StringComparison.Ordinal))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorQueryResults<TRecord>>.Failure(
                IntelligenceErrors.EmbeddingModelMismatch(CollectionName, _definition.EmbeddingModelId, query.ModelId)));
        }

        if (query.Vector.Length != _definition.Dimension)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorQueryResults<TRecord>>.Failure(
                IntelligenceErrors.DimensionMismatch(CollectionName, _definition.Dimension, query.Vector.Length)));
        }

        if (_definition.TenantField is not null && tenantScope.IsGlobal)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorQueryResults<TRecord>>.Failure(
                IntelligenceErrors.TenantScopeMissing(CollectionName)));
        }

        var effectiveFilter = ApplyTenantScope(query.Filter, tenantScope);
        var candidates = _store.Values.Where(r => effectiveFilter is null || Evaluate(effectiveFilter, r));

        var descending = _definition.DistanceMetric != VectorDistanceMetric.Euclidean;
        var scored = candidates.Select(r => (Record: r, Score: ComputeScore(query.Vector, r.Vector)));
        var ordered = descending
            ? scored.OrderByDescending(x => x.Score)
            : scored.OrderBy(x => x.Score);

        IEnumerable<(TRecord Record, float Score)> filtered = ordered;
        if (query.MinScore is { } minScore)
        {
            filtered = descending
                ? ordered.Where(x => x.Score >= minScore)
                : ordered.Where(x => x.Score <= minScore);
        }

        var hits = filtered
            .Take(query.Limit)
            .Select((x, index) => new VectorHit<TRecord> { Record = x.Record, Score = x.Score, Rank = index })
            .ToList();

        _queriedVectors.Enqueue(query);

        return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorQueryResults<TRecord>>.Success(new VectorQueryResults<TRecord>
        {
            Hits = hits,
            Duration = TimeSpan.Zero,
        }));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<TRecord>> GetAsync(
        string id, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        // A direct dictionary lookup followed by a tenant-field comparison, rather than routing
        // through the shared filter evaluator -- a tenant mismatch (including TenantScope.Global on a
        // tenant-declaring collection) folds into RecordNotFound identically to a genuinely missing
        // id, never a cross-tenant leak, never a thrown exception.
        if (!_store.TryGetValue(id, out var record))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<TRecord>.Failure(
                IntelligenceErrors.RecordNotFound(CollectionName, id)));
        }

        if (_definition.TenantField is { } tenantField)
        {
            if (!record.Metadata.TryGetValue(tenantField, out var actualTenant)
                || actualTenant.Kind != VectorValueKind.String
                || !string.Equals(actualTenant.AsString, tenantScope.Tenant?.ToString(), StringComparison.Ordinal))
            {
                return Task.FromResult(SharedKernel.Primitives.Results.Result<TRecord>.Failure(
                    IntelligenceErrors.RecordNotFound(CollectionName, id)));
            }
        }

        return Task.FromResult(SharedKernel.Primitives.Results.Result<TRecord>.Success(record));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<long>> CountAsync(
        VectorFilter? filter, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        if (_definition.TenantField is not null && tenantScope.IsGlobal)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<long>.Failure(
                IntelligenceErrors.TenantScopeMissing(CollectionName)));
        }

        var effectiveFilter = ApplyTenantScope(filter, tenantScope);
        var count = _store.Values.LongCount(r => effectiveFilter is null || Evaluate(effectiveFilter, r));
        return Task.FromResult(SharedKernel.Primitives.Results.Result<long>.Success(count));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TRecord> ScrollAsync(
        VectorFilter? filter, TenantScope tenantScope, int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_definition.TenantField is not null && tenantScope.IsGlobal)
        {
            throw new IntelligenceStreamException(IntelligenceErrors.TenantScopeMissing(CollectionName));
        }

        var effectiveFilter = ApplyTenantScope(filter, tenantScope);

        // batchSize is accepted for signature parity only -- a ConcurrentDictionary walk needs no
        // explicit chunking. Ordering is unspecified and must not be relied upon.
        foreach (var record in _store.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (effectiveFilter is not null && !Evaluate(effectiveFilter, record))
            {
                continue;
            }

            yield return record;
            await Task.Yield();
        }
    }

    /// <summary>Returns whether <paramref name="id"/> was ever successfully upserted.</summary>
    public bool WasUpserted(string id) => _upsertedIds.Contains(id, StringComparer.Ordinal);

    /// <summary>Returns whether <paramref name="id"/> was ever successfully deleted.</summary>
    public bool WasDeleted(string id) => _deletedIds.Contains(id, StringComparer.Ordinal);

    /// <summary>Returns whether <paramref name="id"/> is currently present in the backing store.</summary>
    public bool IsQueryable(string id) => _store.ContainsKey(id);

    /// <summary>
    /// Pre-populates the backing store with <paramref name="record"/> without going through
    /// <see cref="UpsertAsync"/> -- a test-setup helper. Still enforces the <see cref="IVectorRecord.Id"/>
    /// non-null/non-whitespace rule.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="record"/>'s <see cref="IVectorRecord.Id"/> is null/whitespace.</exception>
    public void Seed(TRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.Id))
        {
            throw new ArgumentException(IntelligenceErrors.InvalidRecordId(record.Id).Message, nameof(record));
        }

        _store[record.Id] = record;
    }

    /// <summary>Clears the backing store and every recorded-history list.</summary>
    public void Reset()
    {
        _store.Clear();
        _upsertedIds.Clear();
        _deletedIds.Clear();
        _queriedVectors.Clear();
    }

    private SharedKernel.Primitives.Errors.Error? ValidateRecord(TRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Id))
        {
            return IntelligenceErrors.InvalidRecordId(record.Id);
        }

        if (!string.Equals(record.ModelId, _definition.EmbeddingModelId, StringComparison.Ordinal))
        {
            return IntelligenceErrors.EmbeddingModelMismatch(CollectionName, _definition.EmbeddingModelId, record.ModelId);
        }

        if (record.Vector.Length != _definition.Dimension)
        {
            return IntelligenceErrors.DimensionMismatch(CollectionName, _definition.Dimension, record.Vector.Length);
        }

        return null;
    }

    private VectorWriteReceipt BuildReceipt(int affectedCount) => new()
    {
        CollectionName = CollectionName,
        ProviderToken = NextToken(),
        AffectedCount = affectedCount,
        AcceptedAt = FixedAcceptedAt,
    };

    private string NextToken() =>
        string.Create(CultureInfo.InvariantCulture, $"in-memory-vector-token-{Interlocked.Increment(ref _tokenSequence)}");

    private VectorFilter? ApplyTenantScope(VectorFilter? filter, TenantScope tenantScope)
    {
        if (_definition.TenantField is not { } tenantField)
        {
            return filter;
        }

        VectorFilter tenantClause = VectorFilter.Eq(tenantField, tenantScope.Tenant!.Value.ToString());
        return filter is null ? tenantClause : VectorFilter.All(tenantClause, filter);
    }

    private float ComputeScore(ReadOnlyMemory<float> query, ReadOnlyMemory<float> candidate) =>
        _definition.DistanceMetric switch
        {
            VectorDistanceMetric.Cosine => CosineSimilarity(query.Span, candidate.Span),
            VectorDistanceMetric.DotProduct => DotProduct(query.Span, candidate.Span),
            VectorDistanceMetric.Euclidean => EuclideanDistance(query.Span, candidate.Span),
            _ => throw new NotSupportedException($"Unsupported distance metric '{_definition.DistanceMetric}'."),
        };

    private static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var dot = DotProduct(a, b);
        var normA = MathF.Sqrt(DotProduct(a, a));
        var normB = MathF.Sqrt(DotProduct(b, b));
        return normA == 0f || normB == 0f ? 0f : dot / (normA * normB);
    }

    private static float EuclideanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }

        return MathF.Sqrt(sum);
    }

    private static bool Evaluate(VectorFilter filter, TRecord record) => filter switch
    {
        EqualFilter f => record.Metadata.TryGetValue(f.Field, out var actual) && actual.Equals(f.Value),
        NotEqualFilter f => record.Metadata.TryGetValue(f.Field, out var actual) && !actual.Equals(f.Value),
        InFilter f => record.Metadata.TryGetValue(f.Field, out var actual) && f.Values.Any(v => v.Equals(actual)),
        RangeFilter f => record.Metadata.TryGetValue(f.Field, out var actual) && SatisfiesRange(actual, f),
        ExistsFilter f => record.Metadata.ContainsKey(f.Field),
        AndFilter f => f.Operands.All(op => Evaluate(op, record)),
        OrFilter f => f.Operands.Any(op => Evaluate(op, record)),
        NotFilter f => !Evaluate(f.Operand, record),
    };

    private static bool SatisfiesRange(VectorValue actual, RangeFilter filter)
    {
        if (filter.From is { } from)
        {
            var comparison = CompareSameKind(actual, from);
            if (comparison is not { } cmp || (filter.FromInclusive ? cmp < 0 : cmp <= 0))
            {
                return false;
            }
        }

        if (filter.To is { } to)
        {
            var comparison = CompareSameKind(actual, to);
            if (comparison is not { } cmp || (filter.ToInclusive ? cmp > 0 : cmp >= 0))
            {
                return false;
            }
        }

        return true;
    }

    private static int? CompareSameKind(VectorValue a, VectorValue b)
    {
        if (a.Kind != b.Kind)
        {
            return null;
        }

        return a.Kind switch
        {
            VectorValueKind.Int64 => a.AsInt64.CompareTo(b.AsInt64),
            VectorValueKind.Double => a.AsDouble.CompareTo(b.AsDouble),
            VectorValueKind.DateTimeOffset => a.AsDateTimeOffset.CompareTo(b.AsDateTimeOffset),
            _ => null,
        };
    }
}
