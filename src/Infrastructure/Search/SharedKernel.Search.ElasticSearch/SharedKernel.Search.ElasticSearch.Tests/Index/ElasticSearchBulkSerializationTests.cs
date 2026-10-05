using System.Reflection;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Serialization;
using Elastic.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Search.ElasticSearch.Tests.Index;

/// <summary>
/// T-27 (P-353): proves the C-49 bulk-write fix — each document passed to a single
/// <see cref="ElasticSearchIndex{TDocument}.IndexManyAsync(IReadOnlyCollection{TDocument}, SearchWriteConsistency, System.Threading.CancellationToken)"/>
/// call is serialized exactly once, never twice. Container-free: exercises the private
/// <c>SerializeAndBatch</c> batching step directly via reflection, with a counting
/// <see cref="Elastic.Transport.Serializer"/> wrapper standing in for the client's real source
/// serializer — no network call is made, so this proof has zero dependency on a running engine.
/// </summary>
/// <remarks>
/// Before the C-49 fix, each document's <c>_bulk</c> wire bytes were produced once for a
/// chunk-size estimate (a standalone <c>JsonSerializer.SerializeToUtf8Bytes</c> call) and again
/// inside the SDK's own typed <c>BulkAsync</c> — two serializations per document, silently
/// divergent whenever a source-serializer context was configured (since the estimate pass never
/// saw it). <see cref="ElasticSearchIndex{TDocument}.SerializeIndexOperation"/> (private, invoked
/// here through <see cref="ElasticSearchIndex{TDocument}.SerializeAndBatch"/>) now serializes each
/// document's <c>_bulk</c> operation exactly once via the SDK's own
/// <c>BulkOperationsCollection.Serialize</c>, and that single buffer is reused verbatim by
/// <c>ConcatenateBulkBody</c> — this test locks that invariant against silent regression.
/// </remarks>
public sealed class ElasticSearchBulkSerializationTests
{
    private sealed class BulkTestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }

        public required string Name { get; init; }
    }

    /// <summary>
    /// Wraps a real <see cref="Elastic.Transport.Serializer"/>, counting every call that serializes a
    /// <see cref="BulkTestDocument"/> instance — through either the generic <c>Serialize&lt;T&gt;</c>
    /// overload or the runtime-<see cref="Type"/>-dispatched overload, since the SDK's internal bulk
    /// operation wrapper may reach either path.
    /// </summary>
    private sealed class CountingSourceSerializer(Elastic.Transport.Serializer inner) : Elastic.Transport.Serializer
    {
        public int DocumentSerializeCallCount { get; private set; }

        public override object? Deserialize(Type type, Stream stream) => inner.Deserialize(type, stream);

        public override T Deserialize<T>(Stream stream) where T : default => inner.Deserialize<T>(stream);

        public override ValueTask<object?> DeserializeAsync(
            Type type, Stream stream, CancellationToken cancellationToken = default) =>
            inner.DeserializeAsync(type, stream, cancellationToken);

        public override ValueTask<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
            where T : default =>
            inner.DeserializeAsync<T>(stream, cancellationToken);

        public override void Serialize(
            object data,
            Type type,
            Stream stream,
            SerializationFormatting formatting = SerializationFormatting.None,
            CancellationToken cancellationToken = default)
        {
            if (type == typeof(BulkTestDocument))
            {
                DocumentSerializeCallCount++;
            }

            inner.Serialize(data, type, stream, formatting, cancellationToken);
        }

        public override void Serialize<T>(T data, Stream stream, SerializationFormatting formatting = SerializationFormatting.None)
        {
            if (typeof(T) == typeof(BulkTestDocument))
            {
                DocumentSerializeCallCount++;
            }

            inner.Serialize(data, stream, formatting);
        }

        public override Task SerializeAsync(
            object data,
            Type type,
            Stream stream,
            SerializationFormatting formatting = SerializationFormatting.None,
            CancellationToken cancellationToken = default)
        {
            if (type == typeof(BulkTestDocument))
            {
                DocumentSerializeCallCount++;
            }

            return inner.SerializeAsync(data, type, stream, formatting, cancellationToken);
        }

        public override Task SerializeAsync<T>(
            T data,
            Stream stream,
            SerializationFormatting formatting = SerializationFormatting.None,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(BulkTestDocument))
            {
                DocumentSerializeCallCount++;
            }

            return inner.SerializeAsync(data, stream, formatting, cancellationToken);
        }
    }

    private static (ElasticSearchIndex<BulkTestDocument> Index, CountingSourceSerializer Serializer) CreateIndex()
    {
        CountingSourceSerializer? serializer = null;
        var settings = new ElasticsearchClientSettings(
            new SingleNodePool(new Uri("http://127.0.0.1:9200")),
            (_, clientSettings) =>
            {
                serializer = new CountingSourceSerializer(new DefaultSourceSerializer(clientSettings, _ => { }));
                return serializer;
            });
        var client = new ElasticsearchClient(settings);

        var definition = new SearchIndexDefinitionBuilder("bulk-serialization-products")
            .Field("name", SearchFieldKind.Text, searchable: true)
            .Build()
            .Value;

        var index = new ElasticSearchIndex<BulkTestDocument>(
            client,
            definition,
            writeAlias: definition.Name,
            new ElasticSearchOptions(),
            new FakeClock(),
            NullLogger<ElasticSearchIndex<BulkTestDocument>>.Instance);

        // The factory delegate above always runs synchronously during ElasticsearchClient construction
        // — serializer is never null by the time control returns here.
        return (index, serializer!);
    }

    private static List<List<byte[]>> InvokeSerializeAndBatch(
        ElasticSearchIndex<BulkTestDocument> index, IReadOnlyCollection<BulkTestDocument> documents, int maxDocuments, int maxBytes)
    {
        var method = typeof(ElasticSearchIndex<BulkTestDocument>).GetMethod(
            "SerializeAndBatch", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("SerializeAndBatch is the batching step this test locks against silent regression");

        var result = (IEnumerable<List<byte[]>>)method!.Invoke(index, [documents, maxDocuments, maxBytes])!;

        // SerializeAndBatch is a lazy iterator (yield return) — ToList() forces full enumeration, which
        // is what drives every document's SerializeIndexOperation call in production too (the real
        // caller, IndexManyAsync's own foreach loop, pulls the same way).
        return result.ToList();
    }

    [Fact]
    public void SerializeAndBatch_MultiDocumentBatch_SerializesEachDocumentExactlyOnce()
    {
        var (index, serializer) = CreateIndex();
        var documents = Enumerable.Range(1, 7)
            .Select(i => new BulkTestDocument { DocumentId = $"doc-{i}", Name = $"Product {i}" })
            .ToList();

        var batches = InvokeSerializeAndBatch(index, documents, maxDocuments: 2000, maxBytes: 10_485_760);

        batches.Should().HaveCount(1, "all seven documents fit comfortably within the default batch limits");
        batches[0].Should().HaveCount(7, "one serialized byte buffer per document");
        serializer.DocumentSerializeCallCount.Should().Be(
            documents.Count, "each document must be serialized exactly once — never twice — per IndexManyAsync call");
    }

    [Fact]
    public void SerializeAndBatch_SingleDocument_SerializesExactlyOnce()
    {
        var (index, serializer) = CreateIndex();
        var documents = new List<BulkTestDocument> { new() { DocumentId = "solo", Name = "Solo Product" } };

        InvokeSerializeAndBatch(index, documents, maxDocuments: 2000, maxBytes: 10_485_760);

        serializer.DocumentSerializeCallCount.Should().Be(1);
    }

    [Fact]
    public void SerializeAndBatch_ExceedingMaxDocumentsPerBatch_StillSerializesEachDocumentExactlyOnce()
    {
        var (index, serializer) = CreateIndex();
        var documents = Enumerable.Range(1, 9)
            .Select(i => new BulkTestDocument { DocumentId = $"chunked-{i}", Name = $"Product {i}" })
            .ToList();

        // maxDocuments: 4 forces three batches (4 + 4 + 1) — the multi-batch dispatch path C-49's fix
        // must also hold for, not merely the common single-batch case above.
        var batches = InvokeSerializeAndBatch(index, documents, maxDocuments: 4, maxBytes: 10_485_760);

        batches.Should().HaveCount(3);
        batches.SelectMany(b => b).Should().HaveCount(9);
        serializer.DocumentSerializeCallCount.Should().Be(documents.Count);
    }
}
