using Qdrant.Client;

namespace SharedKernel.AI.Qdrant.Raw;

/// <summary>The sole implementation of <see cref="IQdrantRawClientAccessor"/>.</summary>
internal sealed class QdrantRawClientAccessor : IQdrantRawClientAccessor
{
    public QdrantRawClientAccessor(IQdrantClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Client = client;
    }

    public IQdrantClient Client { get; }
}
