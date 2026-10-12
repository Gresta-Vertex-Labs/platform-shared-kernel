using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Qdrant.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Quantization;

/// <summary>The sole implementation of <see cref="IQdrantQuantizationProfileAccessor"/>.</summary>
internal sealed class QdrantQuantizationProfileAccessor : IQdrantQuantizationProfileAccessor
{
    private const string ProviderName = IntelligenceWellKnown.QdrantProviderName;
    private const string Operation = "quantization_profile";

    private readonly IQdrantClient _client;
    private readonly ILogger<QdrantQuantizationProfileAccessor> _logger;

    public QdrantQuantizationProfileAccessor(IQdrantClient client, ILogger<QdrantQuantizationProfileAccessor> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _logger = logger;
    }

    public async Task<Result<QdrantQuantizationProfile>> GetQuantizationProfileAsync(
        string collectionName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        try
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
        catch (Exception ex) when (QdrantFaults.TryClassify(ex, cancellationToken, ProviderName, Operation, collectionName, out var error))
        {
            return Result<QdrantQuantizationProfile>.Failure(
                QdrantFaults.Surface(ex, error, cancellationToken, _logger, Operation, collectionName));
        }
    }
}
