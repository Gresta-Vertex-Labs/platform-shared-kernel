using System.Diagnostics;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Constants;
using SharedKernel.AI.Qdrant.Diagnostics;
using SharedKernel.AI.Qdrant.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Provisioning;

/// <summary>
/// The Qdrant implementation of <see cref="IVectorCollectionProvisioner"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="VectorCollectionDefinition.Fingerprint"/> is persisted via Qdrant's genuine
/// collection-level <c>metadata</c> map</b> (<c>Qdrant.Client</c> 1.18.1's
/// <c>CreateCollectionAsync</c>/<c>UpdateCollectionAsync</c> <c>metadata</c> parameter, confirmed via
/// direct assembly reflection at Core-phase implementation time) — not a reserved sentinel point, which
/// the Design phase assumed would be necessary because "Qdrant has no generic collection-level metadata
/// slot." That assumption is corrected here: the real, current client API makes a sentinel-point
/// workaround unnecessary.
/// </para>
/// <para>
/// <b>Takes the concrete <see cref="QdrantClient"/>, not <see cref="IQdrantClient"/>:</b> verified
/// directly against the compiled assembly — the metadata-accepting
/// <c>CreateCollectionAsync</c>/<c>UpdateCollectionAsync</c> overloads this type depends on are
/// convenience overloads declared only on the concrete class, not on <see cref="IQdrantClient"/>. Every
/// other member this package uses (<c>UpsertAsync</c>, <c>QueryAsync</c>, <c>ScrollAsync</c>, etc.) is
/// on the interface, so <c>QdrantVectorCollection{TRecord}</c> and the other Qdrant-exclusive
/// accessors continue to depend on <see cref="IQdrantClient"/> — the sanctioned <c>NSubstitute</c>
/// mocking seam for status-code-mapping tests — and only this provisioner needs the concrete type.
/// </para>
/// </remarks>
internal sealed class QdrantCollectionProvisioner : IVectorCollectionProvisioner
{
    private readonly QdrantClient _client;
    private readonly QdrantTelemetry _telemetry;
    private readonly ILogger<QdrantCollectionProvisioner> _logger;

    public QdrantCollectionProvisioner(QdrantClient client, QdrantTelemetry telemetry, ILogger<QdrantCollectionProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _telemetry = telemetry;
        _logger = logger;
    }

    public Task<Result> EnsureCollectionAsync(VectorCollectionDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return QdrantOperationRunner.RunAsync(
            _telemetry,
            _logger,
            IntelligenceOperations.EnsureCollection,
            definition.Name,
            cancellationToken,
            () => EnsureCollectionCoreAsync(definition, cancellationToken));
    }

    private async Task<Result> EnsureCollectionCoreAsync(VectorCollectionDefinition definition, CancellationToken cancellationToken)
    {
        var exists = await _client.CollectionExistsAsync(definition.Name, cancellationToken).ConfigureAwait(false);
        if (!exists && await TryCreateCollectionAsync(definition, cancellationToken).ConfigureAwait(false))
        {
            foreach (var field in definition.Fields.Where(f => f.Filterable))
            {
                await _client
                    .CreatePayloadIndexAsync(definition.Name, field.Name, MapPayloadSchemaType(field.Kind), wait: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            QdrantLog.QdrantCollectionEnsured(_logger, definition.Name, definition.Fields.Count(f => f.Filterable));
            return Result.Success();
        }

        var info = await _client.GetCollectionInfoAsync(definition.Name, cancellationToken).ConfigureAwait(false);
        var existingFingerprint = info.Config.Metadata.TryGetValue(QdrantWellKnown.FingerprintMetadataKey, out var fingerprintValue)
            ? fingerprintValue.StringValue
            : null;

        if (existingFingerprint is not null && !string.Equals(existingFingerprint, definition.Fingerprint, StringComparison.Ordinal))
        {
            QdrantLog.QdrantSchemaFingerprintMismatch(_logger, definition.Name, definition.Fingerprint, existingFingerprint);
            return Result.Failure(IntelligenceErrors.CollectionDefinitionConflict(definition.Name, "schema fingerprint"));
        }

        // Additive-only: create an index for any Filterable field the live collection is missing —
        // never drop or rewrite an existing field's index.
        foreach (var field in definition.Fields.Where(f => f.Filterable && !info.PayloadSchema.ContainsKey(f.Name)))
        {
            await _client
                .CreatePayloadIndexAsync(definition.Name, field.Name, MapPayloadSchemaType(field.Kind), wait: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        if (existingFingerprint is null)
        {
            // A legacy collection created before fingerprint tracking existed — stamp it now.
            await _client
                .UpdateCollectionAsync(definition.Name, new Dictionary<string, Value> { [QdrantWellKnown.FingerprintMetadataKey] = definition.Fingerprint }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <summary>
    /// Creates the collection with its fingerprint. <see langword="false"/> when it already exists: replicas starting
    /// together all see "no collection" and all create it, and every one but the first is told AlreadyExists — the
    /// collection is there, so the caller continues as on an existing one.
    /// </summary>
    private async Task<bool> TryCreateCollectionAsync(VectorCollectionDefinition definition, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, Value> { [QdrantWellKnown.FingerprintMetadataKey] = definition.Fingerprint };
        var vectorParams = new VectorParams
        {
            Size = (ulong)definition.Dimension,
            Distance = MapMetric(definition.DistanceMetric),
        };

        try
        {
            await _client
                .CreateCollectionAsync(definition.Name, vectorParams, metadata, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            return false;
        }
    }

    public Task<Result<bool>> CollectionExistsAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        return QdrantOperationRunner.RunAsync(
            _telemetry,
            _logger,
            IntelligenceOperations.CollectionExists,
            collectionName,
            cancellationToken,
            () => CollectionExistsCoreAsync(collectionName, cancellationToken));
    }

    private async Task<Result<bool>> CollectionExistsCoreAsync(string collectionName, CancellationToken cancellationToken)
    {
        var exists = await _client.CollectionExistsAsync(collectionName, cancellationToken).ConfigureAwait(false);
        return Result<bool>.Success(exists);
    }

    public Task<Result> DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        return QdrantOperationRunner.RunAsync(
            _telemetry,
            _logger,
            IntelligenceOperations.DeleteCollection,
            collectionName,
            cancellationToken,
            () => DeleteCollectionCoreAsync(collectionName, cancellationToken));
    }

    private async Task<Result> DeleteCollectionCoreAsync(string collectionName, CancellationToken cancellationToken)
    {
        await _client.DeleteCollectionAsync(collectionName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<Result> CutoverAsync(VectorCollectionCutoverRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return QdrantOperationRunner.RunAsync(
            _telemetry,
            _logger,
            IntelligenceOperations.Cutover,
            request.LiveCollectionName,
            cancellationToken,
            () => CutoverCoreAsync(request, cancellationToken));
    }

    private async Task<Result> CutoverCoreAsync(VectorCollectionCutoverRequest request, CancellationToken cancellationToken)
    {
        var stagingExists = await _client.CollectionExistsAsync(request.StagingCollectionName, cancellationToken).ConfigureAwait(false);
        if (!stagingExists)
        {
            return Result.Failure(IntelligenceErrors.CutoverFailed(
                request.StagingCollectionName, request.LiveCollectionName, "The staging collection does not exist."));
        }

        var existingAliases = await _client.ListAliasesAsync(cancellationToken).ConfigureAwait(false);
        var previousCollectionName = existingAliases
            .FirstOrDefault(alias => string.Equals(alias.AliasName, request.LiveCollectionName, StringComparison.Ordinal))
            ?.CollectionName;

        var operations = new List<AliasOperations>();
        if (previousCollectionName is not null)
        {
            operations.Add(new AliasOperations { DeleteAlias = new DeleteAlias { AliasName = request.LiveCollectionName } });
        }

        operations.Add(new AliasOperations
        {
            CreateAlias = new CreateAlias { AliasName = request.LiveCollectionName, CollectionName = request.StagingCollectionName },
        });

        await _client.UpdateAliasesAsync(operations, cancellationToken: cancellationToken).ConfigureAwait(false);
        QdrantLog.QdrantCollectionsCutOver(_logger, request.StagingCollectionName, request.LiveCollectionName);

        // "Staging" here names the neutral-contract's Meilisearch-oriented field — on Qdrant's alias
        // model, the collection left over after cutover is whatever the LIVE ALIAS previously
        // pointed at (now orphaned), never request.StagingCollectionName itself (which is the
        // collection now actively serving traffic through the alias).
        if (previousCollectionName is not null &&
            !string.Equals(previousCollectionName, request.StagingCollectionName, StringComparison.Ordinal))
        {
            if (request.DeleteStagingAfterCutover)
            {
                await _client.DeleteCollectionAsync(previousCollectionName, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                QdrantLog.QdrantStagingCollectionRetained(_logger, previousCollectionName);
            }
        }

        return Result.Success();
    }

    public async Task<Result<VectorCollectionHealth>> ProbeAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        var stopwatch = Stopwatch.StartNew();
        var engineVersion = string.Empty;

        try
        {
            var health = await _client.HealthAsync(cancellationToken).ConfigureAwait(false);
            engineVersion = health.Version;
        }
        catch (Exception ex) when (QdrantFaults.IsCancellationOf(ex, cancellationToken))
        {
            throw QdrantFaults.CanceledBy(ex, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A failed measurement degrades the report instead of throwing; the first exception reaches the log.
            stopwatch.Stop();
            QdrantLog.QdrantProbeDegraded(_logger, collectionName, reachable: false, collectionAddressable: false, queryable: false, ex);
            return Result<VectorCollectionHealth>.Success(new VectorCollectionHealth
            {
                Reachable = false,
                CollectionAddressable = false,
                Queryable = false,
                VectorCount = 0,
                EngineVersion = engineVersion,
                Latency = stopwatch.Elapsed,
            });
        }

        var addressable = false;
        long vectorCount = 0;
        string? fingerprint = null;
        long? pendingWriteCount = null;
        Exception? firstFault = null;

        try
        {
            var info = await _client.GetCollectionInfoAsync(collectionName, cancellationToken).ConfigureAwait(false);
            addressable = true;
            vectorCount = checked((long)info.PointsCount);
            fingerprint = info.Config.Metadata.TryGetValue(QdrantWellKnown.FingerprintMetadataKey, out var fingerprintValue)
                ? fingerprintValue.StringValue
                : null;
            pendingWriteCount = info.UpdateQueue is { } queue ? checked((long)queue.Length) : null;
        }
        catch (Exception ex) when (QdrantFaults.IsCancellationOf(ex, cancellationToken))
        {
            throw QdrantFaults.CanceledBy(ex, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            addressable = false;
            firstFault ??= ex;
        }

        var queryable = false;
        if (addressable)
        {
            try
            {
                await _client.CountAsync(collectionName, filter: null, exact: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                queryable = true;
            }
            catch (Exception ex) when (QdrantFaults.IsCancellationOf(ex, cancellationToken))
            {
                throw QdrantFaults.CanceledBy(ex, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                queryable = false;
                firstFault ??= ex;
            }
        }

        stopwatch.Stop();

        if (!addressable || !queryable)
        {
            QdrantLog.QdrantProbeDegraded(_logger, collectionName, reachable: true, addressable, queryable, firstFault);
        }

        return Result<VectorCollectionHealth>.Success(new VectorCollectionHealth
        {
            Reachable = true,
            CollectionAddressable = addressable,
            Queryable = queryable,
            VectorCount = vectorCount,
            PendingWriteCount = pendingWriteCount,
            EngineVersion = engineVersion,
            SchemaFingerprint = fingerprint,
            Latency = stopwatch.Elapsed,
        });
    }

    private static Distance MapMetric(VectorDistanceMetric metric) => metric switch
    {
        VectorDistanceMetric.Cosine => Distance.Cosine,
        VectorDistanceMetric.DotProduct => Distance.Dot,
        VectorDistanceMetric.Euclidean => Distance.Euclid,
        _ => throw new UnreachableException($"Unknown VectorDistanceMetric '{metric}'."),
    };

    private static PayloadSchemaType MapPayloadSchemaType(VectorFieldKind kind) => kind switch
    {
        VectorFieldKind.String => PayloadSchemaType.Keyword,
        VectorFieldKind.Int64 => PayloadSchemaType.Integer,
        VectorFieldKind.Double => PayloadSchemaType.Float,
        VectorFieldKind.Boolean => PayloadSchemaType.Bool,
        VectorFieldKind.DateTimeOffset => PayloadSchemaType.Datetime,
        _ => throw new UnreachableException($"Unknown VectorFieldKind '{kind}'."),
    };
}
