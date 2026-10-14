using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Collections;
using SharedKernel.AI.Qdrant.Diagnostics;
using SharedKernel.AI.Qdrant.Querying;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Sparse;

/// <summary>The sole implementation of <see cref="IQdrantHybridQueryAccessor{TRecord}"/>.</summary>
internal sealed class QdrantHybridQueryAccessor<TRecord> : IQdrantHybridQueryAccessor<TRecord>
    where TRecord : class, IVectorRecord
{
    private readonly IQdrantClient _client;
    private readonly VectorCollectionDefinition _definition;
    private readonly IClock _clock;
    private readonly QdrantTelemetry _telemetry;
    private readonly ILogger<QdrantHybridQueryAccessor<TRecord>> _logger;

    public QdrantHybridQueryAccessor(
        IQdrantClient client,
        VectorCollectionDefinition definition,
        IClock clock,
        QdrantTelemetry telemetry,
        ILogger<QdrantHybridQueryAccessor<TRecord>> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _definition = definition;
        _clock = clock;
        _telemetry = telemetry;
        _logger = logger;
    }

    public Task<Result<VectorQueryResults<TRecord>>> QueryHybridAsync(
        VectorQuery denseQuery,
        string sparseVectorName,
        IReadOnlyList<QdrantSparseVectorEntry> sparseQueryVector,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(denseQuery);
        ArgumentException.ThrowIfNullOrWhiteSpace(sparseVectorName);
        ArgumentNullException.ThrowIfNull(sparseQueryVector);

        return QdrantOperationRunner.RunAsync(_telemetry, _logger, IntelligenceOperations.QueryHybrid, _definition.Name, cancellationToken, async () =>
        {
            if (_definition.TenantField is not null && tenantScope.IsGlobal)
            {
                return Result<VectorQueryResults<TRecord>>.Failure(IntelligenceErrors.TenantScopeMissing(_definition.Name));
            }

            if (!string.Equals(denseQuery.ModelId, _definition.EmbeddingModelId, StringComparison.Ordinal))
            {
                return Result<VectorQueryResults<TRecord>>.Failure(
                    IntelligenceErrors.EmbeddingModelMismatch(_definition.Name, _definition.EmbeddingModelId, denseQuery.ModelId));
            }

            if (denseQuery.Vector.Length != _definition.Dimension)
            {
                return Result<VectorQueryResults<TRecord>>.Failure(
                    IntelligenceErrors.DimensionMismatch(_definition.Name, _definition.Dimension, denseQuery.Vector.Length));
            }

            var compiledFilter = QdrantFilterCompiler.Compile(denseQuery.Filter, _definition.TenantField, tenantScope);
            var limit = (ulong)Math.Max(denseQuery.Limit, 1);
            var indices = sparseQueryVector.Select(e => e.Index).ToArray();
            var values = sparseQueryVector.Select(e => e.Value).ToArray();

            var prefetch = new List<PrefetchQuery>
            {
                new() { Query = (Query)denseQuery.Vector.ToArray(), Filter = compiledFilter, Limit = limit },
                new() { Query = (Query)(values, indices), Using = sparseVectorName, Filter = compiledFilter, Limit = limit },
            };

            var started = _clock.UtcNow;
            var hits = await _client.QueryAsync(
                    _definition.Name,
                    query: (Query)Fusion.Rrf,
                    prefetch: prefetch,
                    limit: limit,
                    payloadSelector: denseQuery.ReturnMetadata,
                    vectorsSelector: denseQuery.ReturnVector,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var duration = _clock.UtcNow - started;
            var vectorHits = new List<VectorHit<TRecord>>(hits.Count);
            for (var i = 0; i < hits.Count; i++)
            {
                var scored = hits[i];
                var record = QdrantRecordMapper.ToRecord<TRecord>(
                    PointIdToString(scored.Id),
                    ExtractDenseVector(scored.Vectors),
                    scored.Payload,
                    _definition);
                vectorHits.Add(new VectorHit<TRecord> { Record = record, Score = scored.Score, Rank = i });
            }

            return Result<VectorQueryResults<TRecord>>.Success(new VectorQueryResults<TRecord> { Hits = vectorHits, Duration = duration });
        });
    }

    private static string PointIdToString(PointId pointId) => pointId.PointIdOptionsCase switch
    {
        PointId.PointIdOptionsOneofCase.Num => pointId.Num.ToString(System.Globalization.CultureInfo.InvariantCulture),
        PointId.PointIdOptionsOneofCase.Uuid => pointId.Uuid,
        _ => string.Empty,
    };

    private static ReadOnlyMemory<float> ExtractDenseVector(VectorsOutput? vectors) =>
        vectors is not null && vectors.VectorsOptionsCase == VectorsOutput.VectorsOptionsOneofCase.Vector
            ? vectors.Vector!.GetDenseVector()!.Data.ToArray()
            : ReadOnlyMemory<float>.Empty;
}
