using Grpc.Core;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Qdrant.Errors;

/// <summary>
/// Maps a raised <see cref="Exception"/> from <c>Qdrant.Client</c> onto the canonical
/// <see cref="IntelligenceErrors"/> factory — the only construction path for an <see cref="Error"/>
/// value in this package.
/// </summary>
internal static class QdrantErrors
{
    /// <summary>Maps <paramref name="exception"/> to the corresponding <see cref="Error"/>.</summary>
    public static Error FromException(Exception exception, string providerName, string operation, string collectionName) =>
        exception switch
        {
            RpcException rpc => FromRpcException(rpc, providerName, operation, collectionName),
            QdrantException qdrant => IntelligenceErrors.EngineFault(providerName, operation, qdrant.Message),
            _ => IntelligenceErrors.EngineFault(providerName, operation, exception.Message),
        };

    private static Error FromRpcException(RpcException rpc, string providerName, string operation, string collectionName) =>
        rpc.StatusCode switch
        {
            StatusCode.NotFound => IntelligenceErrors.CollectionNotFound(collectionName),
            StatusCode.AlreadyExists => IntelligenceErrors.CollectionAlreadyExists(collectionName),
            StatusCode.InvalidArgument => IntelligenceErrors.InvalidQuery(rpc.Status.Detail),
            StatusCode.FailedPrecondition => IntelligenceErrors.InvalidQuery(rpc.Status.Detail),
            StatusCode.PermissionDenied => IntelligenceErrors.Unauthorized(collectionName, operation),
            StatusCode.Unauthenticated => IntelligenceErrors.Unauthorized(collectionName, operation),
            StatusCode.ResourceExhausted => IntelligenceErrors.RateLimited(providerName, retryAfter: null),
            StatusCode.Unavailable => IntelligenceErrors.Unreachable(providerName, collectionName),
            StatusCode.DeadlineExceeded => IntelligenceErrors.Timeout(operation, TimeSpan.Zero),
            _ => IntelligenceErrors.EngineFault(providerName, operation, rpc.Status.Detail),
        };
}
