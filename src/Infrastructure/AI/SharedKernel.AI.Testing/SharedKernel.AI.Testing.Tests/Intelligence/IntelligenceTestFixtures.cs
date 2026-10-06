using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.Testing.SelfTests.Intelligence;

/// <summary>
/// Minimal <see cref="IVectorRecord"/> implementation shared by the <c>Intelligence/</c> self-tests --
/// proves <c>Intelligence/InMemoryEmbeddingGenerator</c>/<c>InMemoryVectorCollection&lt;TRecord&gt;</c>/
/// <c>InMemoryVectorCollectionProvisioner</c>/<c>InMemoryVectorProviderDescriptor</c>/
/// <c>InMemorySemanticKernel</c>/<c>InMemoryCompletionProviderDescriptor</c> against
/// <c>SharedKernel.AI.Abstractions</c>'s documented contract. No consuming domain has adopted these
/// fakes yet (see <c>src/Testing/state-map.md</c> T-54), so these self-tests are the only behavioral
/// proof today, per the SelfTests routing rule.
/// </summary>
internal sealed record TestVectorRecord : IVectorRecord
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public required ReadOnlyMemory<float> Vector { get; init; }

    /// <inheritdoc />
    public required string ModelId { get; init; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, VectorValue> Metadata { get; init; } =
        new Dictionary<string, VectorValue>();
}
