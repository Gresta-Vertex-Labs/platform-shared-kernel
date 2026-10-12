using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Diagnostics;
using SharedKernel.AI.Qdrant.Errors;
using SharedKernel.AI.Qdrant.Logging;
using SharedKernel.AI.Qdrant.Querying;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Collections;

/// <summary>
/// The Qdrant implementation of <see cref="IVectorCollection{TRecord}"/> for one collection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every write dispatches with <c>wait: true</c></b> — Qdrant's own <c>wait</c> parameter blocks the
/// call until the operation is durably applied and searchable, which is the strongest available
/// consistency Qdrant offers. Consequently <see cref="WaitUntilQueryableAsync"/> is a documented no-op
/// success for this provider: by construction, every prior write from this instance has already
/// returned only once queryable. This is an honest simplification, not a faked barrier — Qdrant's
/// high-level client exposes no "wait for a specific past operation id" primitive to defer against.
/// </para>
/// <para>
/// <b>Model-identity/dimension validation runs before any I/O</b> on every write and query, per this
/// domain's sharpest invariant.
/// </para>
/// <para>
/// <b>Tenant scope is fail-closed on every member</b> when the collection declares a
/// <see cref="VectorCollectionDefinition.TenantField"/>: a caller-supplied <see cref="TenantScope.Global"/>
/// returns <c>IntelligenceErrors.TenantScopeMissing</c> before any I/O, uniformly across
/// reads/writes/deletes/scans. Writes stamp the tenant value from the caller-supplied
/// <c>tenantScope</c> parameter itself — never trusting the caller's own <see cref="IVectorRecord.Metadata"/> — and
/// point-level reads/deletes (<see cref="GetAsync"/>, single-id <see cref="DeleteAsync"/>) become
/// filtered operations (<c>has_id AND tenant == value</c>) rather than raw id lookups whenever a
/// tenant field is declared.
/// </para>
/// <para>
/// <b>Observability.</b> Every public call except <see cref="WaitUntilQueryableAsync"/> runs inside one Client span
/// and one duration observation, pre-I/O rejections included. Only a recognised provider fault becomes a failed
/// <see cref="Result{T}"/>; the caller's cancellation is always an <see cref="OperationCanceledException"/>; anything
/// else propagates untouched.
/// </para>
/// </remarks>
internal sealed class QdrantVectorCollection<TRecord> : IVectorCollection<TRecord>
    where TRecord : class, IVectorRecord
{
    private const string ProviderName = IntelligenceWellKnown.QdrantProviderName;

    private readonly IQdrantClient _client;
    private readonly VectorCollectionDefinition _definition;
    private readonly IClock _clock;
    private readonly QdrantTelemetry _telemetry;
    private readonly ILogger<QdrantVectorCollection<TRecord>> _logger;

    public QdrantVectorCollection(
        IQdrantClient client,
        VectorCollectionDefinition definition,
        IClock clock,
        QdrantTelemetry telemetry,
        ILogger<QdrantVectorCollection<TRecord>> logger)
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

    public string CollectionName => _definition.Name;

    // ---------------------------------------------------------------------------------------------
    // write
    // ---------------------------------------------------------------------------------------------

    public async Task<Result<VectorWriteReceipt>> UpsertAsync(
        TRecord record,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var result = await RunAsync(IntelligenceOperations.Upsert, cancellationToken, async () =>
        {
            if (ValidateTenantScope(tenantScope) is { } tenantError)
            {
                return Reject<VectorWriteReceipt>(tenantError);
            }

            if (ValidateRecord(record) is { } recordError)
            {
                return Reject<VectorWriteReceipt>(recordError);
            }

            var pointResult = QdrantRecordMapper.ToPointStruct(record, _definition, tenantScope);
            if (pointResult.IsFailure)
            {
                return Reject<VectorWriteReceipt>(pointResult.Error);
            }

            var updateResult = await _client
                .UpsertAsync(CollectionName, [pointResult.Value], wait: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var receipt = BuildReceipt(updateResult.OperationId, affectedCount: 1);
            _logger.QdrantRecordsUpserted(CollectionName, 1, receipt.ProviderToken);
            return Result<VectorWriteReceipt>.Success(receipt);
        }).ConfigureAwait(false);

        CountSingleWrite(IntelligenceOperations.Upsert, result.IsSuccess);
        return result;
    }

    public async Task<Result<VectorBulkReceipt>> UpsertManyAsync(
        IReadOnlyCollection<TRecord> records,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var result = await RunAsync(IntelligenceOperations.UpsertMany, cancellationToken, async () =>
        {
            if (ValidateTenantScope(tenantScope) is { } tenantError)
            {
                return Reject<VectorBulkReceipt>(tenantError);
            }

            var points = new List<PointStruct>(records.Count);
            foreach (var record in records)
            {
                if (ValidateRecord(record) is { } recordError)
                {
                    return Reject<VectorBulkReceipt>(recordError);
                }

                var pointResult = QdrantRecordMapper.ToPointStruct(record, _definition, tenantScope);
                if (pointResult.IsFailure)
                {
                    return Reject<VectorBulkReceipt>(pointResult.Error);
                }

                points.Add(pointResult.Value);
            }

            if (points.Count == 0)
            {
                return Result<VectorBulkReceipt>.Success(new VectorBulkReceipt
                {
                    Receipt = BuildReceipt(operationId: 0, affectedCount: 0),
                    SucceededCount = 0,
                    Failures = [],
                });
            }

            var operations = points
                .Select(point => new PointsUpdateOperation
                {
                    Upsert = new PointsUpdateOperation.Types.PointStructList { Points = { point } },
                })
                .ToList();

            var results = await _client
                .UpdateBatchAsync(CollectionName, operations, wait: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var recordList = records.ToList();
            var failures = new List<VectorItemFailure>();
            var succeeded = 0;
            for (var i = 0; i < results.Count && i < recordList.Count; i++)
            {
                if (IsSuccessStatus(results[i].Status))
                {
                    succeeded++;
                }
                else
                {
                    failures.Add(new VectorItemFailure
                    {
                        RecordId = recordList[i].Id,
                        Error = IntelligenceErrors.WriteRejected(CollectionName, $"The engine reported update status {results[i].Status}."),
                    });
                }
            }

            var lastOperationId = results.Count > 0 ? results[^1].OperationId : 0;
            var receipt = new VectorBulkReceipt
            {
                Receipt = BuildReceipt(lastOperationId, succeeded),
                SucceededCount = succeeded,
                Failures = failures,
            };

            _logger.QdrantRecordsUpserted(CollectionName, succeeded, receipt.Receipt.ProviderToken);
            if (receipt.HasFailures)
            {
                _logger.QdrantBulkPartialFailure(CollectionName, failures.Count, points.Count);
            }

            return Result<VectorBulkReceipt>.Success(receipt);
        }).ConfigureAwait(false);

        CountBulkWrite(IntelligenceOperations.UpsertMany, result, records.Count);
        return result;
    }

    public async Task<Result<VectorWriteReceipt>> DeleteAsync(
        string id,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var result = await RunAsync(IntelligenceOperations.Delete, cancellationToken, async () =>
        {
            if (ValidateTenantScope(tenantScope) is { } tenantError)
            {
                return Reject<VectorWriteReceipt>(tenantError);
            }

            var idResult = QdrantRecordMapper.ToPointId(id);
            if (idResult.IsFailure)
            {
                return Reject<VectorWriteReceipt>(idResult.Error);
            }

            UpdateResult updateResult;
            if (_definition.TenantField is not null)
            {
                var filter = CompileFilter(filter: null, tenantScope);
                filter.Must.Add(BuildHasIdCondition(idResult.Value));
                updateResult = await _client.DeleteAsync(CollectionName, filter, wait: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                updateResult = await _client
                    .DeleteAsync(CollectionName, idResult.Value, wait: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            // Qdrant's delete-by-id/filter response carries no confirmed-removed count — AffectedCount
            // reflects the requested operation, not a verified removal (documented limitation).
            var receipt = BuildReceipt(updateResult.OperationId, affectedCount: 1);
            _logger.QdrantRecordsDeleted(CollectionName, 1, receipt.ProviderToken);
            return Result<VectorWriteReceipt>.Success(receipt);
        }).ConfigureAwait(false);

        CountSingleWrite(IntelligenceOperations.Delete, result.IsSuccess);
        return result;
    }

    public async Task<Result<VectorBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> ids,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var result = await RunAsync(IntelligenceOperations.DeleteMany, cancellationToken, async () =>
        {
            if (ValidateTenantScope(tenantScope) is { } tenantError)
            {
                return Reject<VectorBulkReceipt>(tenantError);
            }

            var idList = ids.ToList();
            var pointIds = new List<(string RawId, PointId PointId)>(idList.Count);
            foreach (var id in idList)
            {
                var idResult = QdrantRecordMapper.ToPointId(id);
                if (idResult.IsFailure)
                {
                    return Reject<VectorBulkReceipt>(idResult.Error);
                }

                pointIds.Add((id, idResult.Value));
            }

            if (pointIds.Count == 0)
            {
                return Result<VectorBulkReceipt>.Success(new VectorBulkReceipt
                {
                    Receipt = BuildReceipt(operationId: 0, affectedCount: 0),
                    SucceededCount = 0,
                    Failures = [],
                });
            }

            var operations = pointIds
                .Select(entry =>
                {
                    var selector = new PointsSelector();
                    if (_definition.TenantField is not null)
                    {
                        var filter = CompileFilter(filter: null, tenantScope);
                        filter.Must.Add(BuildHasIdCondition(entry.PointId));
                        selector.Filter = filter;
                    }
                    else
                    {
                        selector.Points = new PointsIdsList { Ids = { entry.PointId } };
                    }

                    return new PointsUpdateOperation
                    {
                        DeletePoints = new PointsUpdateOperation.Types.DeletePoints { Points = selector },
                    };
                })
                .ToList();

            var results = await _client
                .UpdateBatchAsync(CollectionName, operations, wait: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var failures = new List<VectorItemFailure>();
            var succeeded = 0;
            for (var i = 0; i < results.Count && i < pointIds.Count; i++)
            {
                if (IsSuccessStatus(results[i].Status))
                {
                    succeeded++;
                }
                else
                {
                    failures.Add(new VectorItemFailure
                    {
                        RecordId = pointIds[i].RawId,
                        Error = IntelligenceErrors.WriteRejected(CollectionName, $"The engine reported update status {results[i].Status}."),
                    });
                }
            }

            var lastOperationId = results.Count > 0 ? results[^1].OperationId : 0;
            var receipt = new VectorBulkReceipt
            {
                Receipt = BuildReceipt(lastOperationId, succeeded),
                SucceededCount = succeeded,
                Failures = failures,
            };

            _logger.QdrantRecordsDeleted(CollectionName, succeeded, receipt.Receipt.ProviderToken);
            return Result<VectorBulkReceipt>.Success(receipt);
        }).ConfigureAwait(false);

        CountBulkWrite(IntelligenceOperations.DeleteMany, result, ids.Count);
        return result;
    }

    public Task<Result<VectorWriteReceipt>> DeleteByFilterAsync(
        VectorFilter filter,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return RunAsync(IntelligenceOperations.DeleteByFilter, cancellationToken, async () =>
        {
            if (ValidateTenantScope(tenantScope) is { } tenantError)
            {
                return Reject<VectorWriteReceipt>(tenantError);
            }

            var compiled = CompileFilter(filter, tenantScope);

            // Qdrant's filtered delete response carries no removed-count — a pre-delete exact count is
            // used as the best-effort AffectedCount, documented as approximate under concurrent writes.
            var matchedCount = await _client
                .CountAsync(CollectionName, compiled, exact: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var updateResult = await _client
                .DeleteAsync(CollectionName, compiled, wait: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var receipt = BuildReceipt(updateResult.OperationId, affectedCount: checked((int)Math.Min(matchedCount, (ulong)int.MaxValue)));
            _logger.QdrantRecordsDeleted(CollectionName, receipt.AffectedCount, receipt.ProviderToken);
            return Result<VectorWriteReceipt>.Success(receipt);
        });
    }

    public Task<Result> WaitUntilQueryableAsync(
        VectorWriteReceipt receipt,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        // Every write from this instance already dispatches with wait: true (see type-level remarks) —
        // by the time a VectorWriteReceipt exists, the write is already durable and queryable. No I/O, so no span.
        return Task.FromResult(Result.Success());
    }

    // ---------------------------------------------------------------------------------------------
    // read
    // ---------------------------------------------------------------------------------------------

    public Task<Result<VectorQueryResults<TRecord>>> QueryAsync(
        VectorQuery query,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return RunAsync(IntelligenceOperations.Query, cancellationToken, async () =>
        {
            if (ValidateTenantScope(tenantScope) is { } tenantError)
            {
                return Reject<VectorQueryResults<TRecord>>(tenantError);
            }

            if (!string.Equals(query.ModelId, _definition.EmbeddingModelId, StringComparison.Ordinal))
            {
                return Reject<VectorQueryResults<TRecord>>(
                    IntelligenceErrors.EmbeddingModelMismatch(CollectionName, _definition.EmbeddingModelId, query.ModelId));
            }

            if (query.Vector.Length != _definition.Dimension)
            {
                return Reject<VectorQueryResults<TRecord>>(
                    IntelligenceErrors.DimensionMismatch(CollectionName, _definition.Dimension, query.Vector.Length));
            }

            var compiled = CompileFilter(query.Filter, tenantScope);

            var started = _clock.UtcNow;
            var hits = await _client.QueryAsync(
                    CollectionName,
                    query: (Query)query.Vector.ToArray(),
                    filter: compiled,
                    scoreThreshold: query.MinScore,
                    limit: (ulong)Math.Max(query.Limit, 0),
                    payloadSelector: query.ReturnMetadata,
                    vectorsSelector: query.ReturnVector,
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

            var results = new VectorQueryResults<TRecord> { Hits = vectorHits, Duration = duration };
            _logger.QdrantQueryExecuted(CollectionName, vectorHits.Count, (long)duration.TotalMilliseconds);
            return Result<VectorQueryResults<TRecord>>.Success(results);
        });
    }

    public Task<Result<TRecord>> GetAsync(
        string id,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return RunAsync(IntelligenceOperations.Get, cancellationToken, async () =>
        {
            if (ValidateTenantScope(tenantScope) is { } tenantError)
            {
                return Reject<TRecord>(tenantError);
            }

            var idResult = QdrantRecordMapper.ToPointId(id);
            if (idResult.IsFailure)
            {
                return Reject<TRecord>(idResult.Error);
            }

            if (_definition.TenantField is not null)
            {
                var filter = CompileFilter(filter: null, tenantScope);
                filter.Must.Add(BuildHasIdCondition(idResult.Value));

                var scrollResult = await _client.ScrollAsync(
                        CollectionName,
                        filter,
                        limit: 1,
                        payloadSelector: true,
                        vectorsSelector: true,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (scrollResult.Result.Count == 0)
                {
                    return Result<TRecord>.Failure(IntelligenceErrors.RecordNotFound(CollectionName, id));
                }

                var scrolled = scrollResult.Result[0];
                var scrolledRecord = QdrantRecordMapper.ToRecord<TRecord>(id, ExtractDenseVector(scrolled.Vectors), scrolled.Payload, _definition);
                return Result<TRecord>.Success(scrolledRecord);
            }

            var retrieved = await _client
                .RetrieveAsync(CollectionName, idResult.Value, withPayload: true, withVectors: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (retrieved.Count == 0)
            {
                return Result<TRecord>.Failure(IntelligenceErrors.RecordNotFound(CollectionName, id));
            }

            var point = retrieved[0];
            var record = QdrantRecordMapper.ToRecord<TRecord>(id, ExtractDenseVector(point.Vectors), point.Payload, _definition);
            return Result<TRecord>.Success(record);
        });
    }

    public Task<Result<long>> CountAsync(
        VectorFilter? filter,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(IntelligenceOperations.Count, cancellationToken, async () =>
        {
            if (ValidateTenantScope(tenantScope) is { } tenantError)
            {
                return Reject<long>(tenantError);
            }

            var compiled = CompileFilter(filter, tenantScope);
            var count = await _client.CountAsync(CollectionName, compiled, exact: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Result<long>.Success(checked((long)count));
        });
    }

    // ---------------------------------------------------------------------------------------------
    // corpus walk
    // ---------------------------------------------------------------------------------------------

    public async IAsyncEnumerable<TRecord> ScrollAsync(
        VectorFilter? filter,
        TenantScope tenantScope,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The span ends when the enumerator completes, faults or is disposed (an abandoned walk included). The consumer's
        // Activity.Current is captured before the span starts and put back before every yield, so the span never
        // replaces the consumer's ambient activity; it is current only around the provider call it parents.
        var consumerActivity = Activity.Current;
        using var scope = _telemetry.Begin(IntelligenceOperations.Scroll, CollectionName);
        Activity.Current = consumerActivity;

        if (ValidateTenantScope(tenantScope) is { } tenantError)
        {
            scope.Fail(tenantError);
            throw new IntelligenceStreamException(tenantError);
        }

        Filter compiled;
        try
        {
            compiled = CompileFilter(filter, tenantScope);
        }
        catch (Exception ex) when (scope.FailAndDecline(ex))
        {
            throw;
        }

        _logger.QdrantRecordWalkStarted(CollectionName, batchSize);

        PointId? offset = null;
        while (true)
        {
            consumerActivity = Activity.Current;

            IReadOnlyList<TRecord> batch;
            PointId? next;
            try
            {
                if (scope.Activity is not null)
                {
                    Activity.Current = scope.Activity;
                }

                var page = await _client.ScrollAsync(
                        CollectionName,
                        compiled,
                        limit: (uint)Math.Max(batchSize, 1),
                        offset: offset,
                        payloadSelector: true,
                        vectorsSelector: true,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var mapped = new List<TRecord>(page.Result.Count);
                foreach (var point in page.Result)
                {
                    mapped.Add(QdrantRecordMapper.ToRecord<TRecord>(
                        PointIdToString(point.Id),
                        ExtractDenseVector(point.Vectors),
                        point.Payload,
                        _definition));
                }

                batch = mapped;
                next = page.Result.Count == 0 ? null : page.NextPageOffset;
            }
            catch (Exception ex) when (QdrantFaults.TryClassify(
                ex, cancellationToken, ProviderName, IntelligenceOperations.Scroll, CollectionName, out var error))
            {
                if (error is null)
                {
                    var canceled = QdrantFaults.CanceledBy(ex, cancellationToken);
                    scope.Fail(canceled);
                    throw canceled;
                }

                QdrantFaults.Report(ex, error, _logger, IntelligenceOperations.Scroll, CollectionName);
                scope.Fail(error);
                throw new IntelligenceStreamException(error, ex);
            }
            catch (Exception ex) when (scope.FailAndDecline(ex))
            {
                throw;
            }
            finally
            {
                if (scope.Activity is not null)
                {
                    Activity.Current = consumerActivity;
                }
            }

            foreach (var record in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    var canceled = new OperationCanceledException(cancellationToken);
                    scope.Fail(canceled);
                    throw canceled;
                }

                yield return record;
            }

            if (next is null)
            {
                scope.Succeed();
                yield break;
            }

            offset = next;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // shared helpers
    // ---------------------------------------------------------------------------------------------

    private Task<Result<T>> RunAsync<T>(string operation, CancellationToken cancellationToken, Func<Task<Result<T>>> body) =>
        QdrantOperationRunner.RunAsync(_telemetry, _logger, operation, CollectionName, cancellationToken, body);

    private void CountSingleWrite(string operation, bool succeeded) =>
        _telemetry.RecordRecords(operation, CollectionName, succeeded ? 1 : 0, succeeded ? 0 : 1);

    private void CountBulkWrite(string operation, Result<VectorBulkReceipt> result, int requested)
    {
        if (result.IsSuccess)
        {
            _telemetry.RecordRecords(operation, CollectionName, result.Value.SucceededCount, result.Value.Failures.Count);
        }
        else
        {
            _telemetry.RecordRecords(operation, CollectionName, 0, requested);
        }
    }

    private Error? ValidateTenantScope(TenantScope tenantScope)
    {
        if (_definition.TenantField is not null && tenantScope.IsGlobal)
        {
            _logger.QdrantTenantScopeMissing(CollectionName);
            return IntelligenceErrors.TenantScopeMissing(CollectionName);
        }

        return null;
    }

    private Error? ValidateRecord(IVectorRecord record)
    {
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

    private Filter CompileFilter(VectorFilter? filter, TenantScope tenantScope) =>
        QdrantFilterCompiler.Compile(filter, _definition.TenantField, tenantScope);

    private static Condition BuildHasIdCondition(PointId pointId)
    {
        var condition = new Condition { HasId = new HasIdCondition() };
        condition.HasId.HasId.Add(pointId);
        return condition;
    }

    private VectorWriteReceipt BuildReceipt(ulong operationId, int affectedCount) => new()
    {
        CollectionName = CollectionName,
        ProviderToken = operationId.ToString(CultureInfo.InvariantCulture),
        AffectedCount = affectedCount,
        AcceptedAt = _clock.UtcNow,
    };

    private static bool IsSuccessStatus(UpdateStatus status) =>
        status is UpdateStatus.Completed or UpdateStatus.Acknowledged;

    private static string PointIdToString(PointId pointId) => pointId.PointIdOptionsCase switch
    {
        PointId.PointIdOptionsOneofCase.Num => pointId.Num.ToString(CultureInfo.InvariantCulture),
        PointId.PointIdOptionsOneofCase.Uuid => pointId.Uuid,
        _ => string.Empty,
    };

    // Qdrant omits the Vectors submessage entirely (a null reference, not a default instance) whenever
    // the caller's vectorsSelector was false — VectorQuery.ReturnVector defaults false, so QueryAsync's
    // own hits routinely carry a null Vectors field. GetAsync/ScrollAsync always pass vectorsSelector:
    // true, so their point/scrolled.Vectors is never null in practice, but the null-guard is unconditional
    // here since ExtractDenseVector has no way to know which caller's selector produced its input.
    private static ReadOnlyMemory<float> ExtractDenseVector(VectorsOutput? vectors) =>
        vectors is not null && vectors.VectorsOptionsCase == VectorsOutput.VectorsOptionsOneofCase.Vector
            ? vectors.Vector!.GetDenseVector()!.Data.ToArray()
            : ReadOnlyMemory<float>.Empty;

    // Logs the rejection with its bounded type and code only: the message of a rejection can carry the caller's own
    // record id, which is never logged.
    private Result<T> Reject<T>(Error error)
    {
        _logger.QdrantRequestRejected(CollectionName, error.Type.ToString(), error.Code);
        return Result<T>.Failure(error);
    }
}
