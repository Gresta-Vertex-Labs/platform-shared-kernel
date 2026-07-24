using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.AI.Qdrant.Tests.TestSupport;

/// <summary>A minimal <see cref="IVectorRecord"/> implementation used across this project's tests.</summary>
public sealed record TestVectorRecord : IVectorRecord
{
    public required string Id { get; init; }

    public required ReadOnlyMemory<float> Vector { get; init; }

    public required string ModelId { get; init; }

    public IReadOnlyDictionary<string, VectorValue> Metadata { get; init; } =
        new Dictionary<string, VectorValue>(StringComparer.Ordinal);
}
