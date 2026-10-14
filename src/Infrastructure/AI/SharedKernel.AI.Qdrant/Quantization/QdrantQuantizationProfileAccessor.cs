using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Qdrant.Diagnostics;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Quantization;

/// <summary>The sole implementation of <see cref="IQdrantQuantizationProfileAccessor"/>.</summary>
internal sealed class QdrantQuantizationProfileAccessor : IQdrantQuantizationProfileAccessor
{
    private readonly IQdrantClient _client;
    private readonly QdrantTelemetry _telemetry;
    private readonly ILogger<QdrantQuantizationProfileAccessor> _logger;

    public QdrantQuantizationProfileAccessor(
        IQdrantClient client,
        QdrantTelemetry telemetry,
        ILogger<QdrantQuantizationProfileAccessor> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _telemetry = telemetry;
        _logger = logger;
    }

    public Task<Result<QdrantQuantizationProfile>> GetQuantizationProfileAsync(
        string collectionName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        return QdrantOperationRunner.RunAsync(
            _telemetry,
            _logger,
            IntelligenceOperations.QuantizationProfile,
            collectionName,
            cancellationToken,
            () => ReadProfileAsync(collectionName, cancellationToken));
    }

    private async Task<Result<QdrantQuantizationProfile>> ReadProfileAsync(string collectionName, CancellationToken cancellationToken)
    {
        var info = await _client.GetCollectionInfoAsync(collectionName, cancellationToken).ConfigureAwait(false);
        var profile = info.Config.QuantizationConfig?.QuantizationCase switch
        {
            QuantizationConfig.QuantizationOneofCase.Scalar => QdrantQuantizationProfile.Scalar,
            QuantizationConfig.QuantizationOneofCase.Binary => QdrantQuantizationProfile.Binary,
            QuantizationConfig.QuantizationOneofCase.Product => QdrantQuantizationProfile.Product,
            _ => QdrantQuantizationProfile.None,
        };

        return Result<QdrantQuantizationProfile>.Success(profile);
    }
}
