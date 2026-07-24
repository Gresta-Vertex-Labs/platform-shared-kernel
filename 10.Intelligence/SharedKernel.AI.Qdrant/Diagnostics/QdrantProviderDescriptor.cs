using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Diagnostics;

/// <summary>The Qdrant implementation of <see cref="IVectorProviderDescriptor"/> — singleton, zero I/O.</summary>
internal sealed class QdrantProviderDescriptor : IVectorProviderDescriptor
{
    private readonly IReadOnlyDictionary<string, VectorCollectionDefinition> _collections;

    public QdrantProviderDescriptor(
        IReadOnlyDictionary<string, VectorCollectionDefinition> collections,
        int maxBatchSize,
        int maxVectorDimension,
        int maxFilterDepth)
    {
        ArgumentNullException.ThrowIfNull(collections);

        _collections = collections;
        MaxBatchSize = maxBatchSize;
        MaxVectorDimension = maxVectorDimension;
        MaxFilterDepth = maxFilterDepth;
    }

    public string ProviderName => IntelligenceWellKnown.QdrantProviderName;

    public int MaxBatchSize { get; }

    public int MaxVectorDimension { get; }

    public int MaxFilterDepth { get; }

    public IReadOnlyList<string> RegisteredCollections => _collections.Keys.ToList();

    public Result Validate(string collectionName, VectorQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ArgumentNullException.ThrowIfNull(query);

        if (!_collections.ContainsKey(collectionName))
        {
            return Result.Failure(IntelligenceErrors.CollectionNotFound(collectionName));
        }

        if (query.Vector.Length > MaxVectorDimension)
        {
            return Result.Failure(IntelligenceErrors.InvalidQuery(
                $"Query vector dimension {query.Vector.Length} exceeds the provider's MaxVectorDimension ceiling of {MaxVectorDimension}."));
        }

        if (query.Filter is { } filter)
        {
            var depth = ComputeDepth(filter);
            if (depth > MaxFilterDepth)
            {
                return Result.Failure(IntelligenceErrors.FilterDepthExceeded(depth, MaxFilterDepth));
            }
        }

        return Result.Success();
    }

    private static int ComputeDepth(VectorFilter filter) => filter switch
    {
        AndFilter and => 1 + (and.Operands.Count > 0 ? and.Operands.Max(ComputeDepth) : 0),
        OrFilter or => 1 + (or.Operands.Count > 0 ? or.Operands.Max(ComputeDepth) : 0),
        NotFilter not => 1 + ComputeDepth(not.Operand),
        EqualFilter or NotEqualFilter or InFilter or RangeFilter or ExistsFilter => 1,
        _ => 1,
    };
}
