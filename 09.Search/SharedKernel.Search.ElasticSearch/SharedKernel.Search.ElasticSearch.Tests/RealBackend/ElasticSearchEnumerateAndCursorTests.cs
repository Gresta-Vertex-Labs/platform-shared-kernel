using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Search.ElasticSearch.Tests.Containers;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using Xunit.Abstractions;
// Elastic.Clients.Elasticsearch declares its own non-generic SearchRequest/SearchRequest<T> types;
// alias ours explicitly, mirroring the production translator's own convention.
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.RealBackend;

/// <summary>
/// T-23: real-backend <c>EnumerateAsync</c> (<see cref="ISearchIndex{TDocument}"/>) and
/// <see cref="ICursorSearch{TDocument}"/> coverage against a live ElasticSearch 9.4.2 container —
/// corpus-walk past the <c>max_result_window</c> ceiling, cursor page-size rejection, point-in-time
/// closure (including on abandoned enumeration), the resumable Open/Read/Close triple, and the two
/// cursor-token failure paths.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
public sealed class ElasticSearchEnumerateAndCursorTests : IAsyncLifetime
{
    private const string IndexName = "products-enumerate-cursor-tests";
    private const int TinyMaxTotalHits = 5;

    private readonly ITestOutputHelper _output;
    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly ElasticSearchIndexProvisioner _provisioner;
    private readonly ElasticSearchIndex<TestProduct> _index;

    public ElasticSearchEnumerateAndCursorTests(ElasticsearchContainerFixture fixture, ITestOutputHelper output)
    {
        _output = output;
        _client = ElasticsearchProviderFactory.CreateClient(fixture);
        // Deliberately tiny MaxTotalHits — tenant A alone (10 docs) already exceeds it, so any test
        // here that yields the full tenant corpus is direct proof of bypassing index.max_result_window.
        _definition = TestProductIndexDefinitions.WithMaxTotalHits(IndexName, TinyMaxTotalHits);
        _provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
        _index = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _definition);
    }

    public async Task InitializeAsync()
    {
        (await _provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();
        (await _index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync() => await _provisioner.DeleteIndexAsync(IndexName);

    [Fact]
    public async Task EnumerateAsync_YieldsFullTenantCorpus_PastMaxTotalHitsCeiling()
    {
        var tenantADocs = new List<TestProduct>();
        await foreach (var document in _index.EnumerateAsync(filter: null, TenantScope.Of(TestProductCorpus.TenantA), batchSize: 3))
        {
            tenantADocs.Add(document);
        }

        var tenantBDocs = new List<TestProduct>();
        await foreach (var document in _index.EnumerateAsync(filter: null, TenantScope.Of(TestProductCorpus.TenantB), batchSize: 3))
        {
            tenantBDocs.Add(document);
        }

        _output.WriteLine(
            "EnumerateAsync observed order (tenant-a): " + string.Join(", ", tenantADocs.Select(d => d.DocumentId)));
        _output.WriteLine(
            "EnumerateAsync observed order (tenant-b): " + string.Join(", ", tenantBDocs.Select(d => d.DocumentId)));

        (tenantADocs.Count + tenantBDocs.Count).Should().Be(TestProductCorpus.All.Count);
        tenantADocs.Select(d => d.DocumentId).Should()
            .BeEquivalentTo(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Select(p => p.DocumentId));
        tenantBDocs.Select(d => d.DocumentId).Should()
            .BeEquivalentTo(TestProductCorpus.ForTenant(TestProductCorpus.TenantB).Select(p => p.DocumentId));
    }

    [Fact]
    public async Task StreamAsync_YieldsFullTenantCorpus_PastMaxTotalHitsCeiling()
    {
        var cursorSearch = ElasticsearchProviderFactory.CreateCursorSearch<TestProduct>(_client, _definition);
        var request = new SearchRequest { PageSize = 3 };

        var hits = new List<SearchHit<TestProduct>>();
        await foreach (var hit in cursorSearch.StreamAsync(request, TenantScope.Of(TestProductCorpus.TenantA), TimeSpan.FromSeconds(30)))
        {
            hits.Add(hit);
        }

        _output.WriteLine("StreamAsync observed order (tenant-a): " + string.Join(", ", hits.Select(h => h.Document.DocumentId)));

        hits.Should().HaveCount(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);
        hits.Select(h => h.Document.DocumentId).Should()
            .BeEquivalentTo(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Select(p => p.DocumentId));
    }

    [Fact]
    public async Task StreamAsync_WithPageGreaterThanOne_ThrowsSearchStreamException_WrappingInvalidSearchRequest()
    {
        var cursorSearch = ElasticsearchProviderFactory.CreateCursorSearch<TestProduct>(_client, _definition);
        var request = new SearchRequest { Page = 2 };

        var act = async () =>
        {
            await foreach (var _ in cursorSearch.StreamAsync(request, TenantScope.Of(TestProductCorpus.TenantA), TimeSpan.FromSeconds(30)))
            {
            }
        };

        var thrown = await act.Should().ThrowAsync<SearchStreamException>();
        thrown.Which.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public async Task OpenCursorAsync_WithPageGreaterThanOne_ReturnsInvalidSearchRequestFailure()
    {
        var cursorSearch = ElasticsearchProviderFactory.CreateCursorSearch<TestProduct>(_client, _definition);
        var request = new SearchRequest { Page = 2 };

        var result = await cursorSearch.OpenCursorAsync(request, TenantScope.Of(TestProductCorpus.TenantA), TimeSpan.FromSeconds(30));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public async Task StreamAsync_AbandonedEnumeration_StillClosesThePointInTime()
    {
        var logger = new InMemoryLogger<ElasticSearchCursorSearch<TestProduct>>();
        var cursorSearch = ElasticsearchProviderFactory.CreateCursorSearch<TestProduct>(_client, _definition, logger: logger);
        // A page size small enough to force multiple round trips across the 10-document tenant corpus.
        var request = new SearchRequest { PageSize = 3 };

        var received = 0;
        await foreach (var _ in cursorSearch.StreamAsync(request, TenantScope.Of(TestProductCorpus.TenantA), TimeSpan.FromSeconds(30)))
        {
            received++;
            if (received == 2)
            {
                break;
            }
        }

        // A C# async-iterator's `finally` block runs when the enumerator is disposed, which
        // `await foreach`'s implicit `await using` triggers on loop exit — including `break` — so this
        // is direct, robust proof the finally block (and its PIT close) ran even on early abandonment.
        logger.Records.ShouldHaveLogged(new EventId(9214), LogLevel.Debug);
    }

    [Fact]
    public async Task OpenReadCloseCursor_ResumableAcrossCalls_YieldsFullTenantCorpus_WithNoDuplicates()
    {
        var cursorSearch = ElasticsearchProviderFactory.CreateCursorSearch<TestProduct>(_client, _definition);
        var request = new SearchRequest { PageSize = 3 };

        var openResult = await cursorSearch.OpenCursorAsync(request, TenantScope.Of(TestProductCorpus.TenantA), TimeSpan.FromMinutes(2));
        openResult.IsSuccess.Should().BeTrue();

        var allHits = new List<SearchHit<TestProduct>>();
        // Only the opaque SearchCursor value is carried between iterations — no Query/PIT id is cached
        // separately — proving the token alone is sufficient to resume, simulating a resumption across a
        // process boundary.
        var currentCursor = openResult.Value;
        var lastCursorUsed = currentCursor;

        while (true)
        {
            var pageResult = await cursorSearch.ReadCursorAsync(currentCursor);
            pageResult.IsSuccess.Should().BeTrue();
            allHits.AddRange(pageResult.Value.Hits);
            lastCursorUsed = currentCursor;

            if (pageResult.Value.IsExhausted || pageResult.Value.NextCursor is null)
            {
                break;
            }

            currentCursor = pageResult.Value.NextCursor.Value;
        }

        allHits.Should().HaveCount(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);
        allHits.Select(h => h.Document.DocumentId).Should().OnlyHaveUniqueItems();
        allHits.Select(h => h.Document.DocumentId).Should()
            .BeEquivalentTo(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Select(p => p.DocumentId));

        var closeResult = await cursorSearch.CloseCursorAsync(lastCursorUsed);
        closeResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ReadCursorAsync_AfterKeepAliveExpires_ReturnsCursorExpired()
    {
        var cursorSearch = ElasticsearchProviderFactory.CreateCursorSearch<TestProduct>(_client, _definition);
        var request = new SearchRequest { PageSize = 3 };
        var shortKeepAlive = TimeSpan.FromSeconds(2);

        var openResult = await cursorSearch.OpenCursorAsync(request, TenantScope.Of(TestProductCorpus.TenantA), shortKeepAlive);
        openResult.IsSuccess.Should().BeTrue();

        // A genuine, deliberate wait for the real ElasticSearch-side point-in-time to lapse — the one
        // sanctioned Task.Delay use in this suite, never a substitute for a readiness check.
        //
        // CONFIRMED EMPIRICALLY against the real container, in this order: (1) a single 8-second wait
        // after a 2-second keep_alive was NOT sufficient; (2) POLLING with ReadCursorAsync every 5
        // seconds for 90 seconds was ALSO not sufficient — and polling is actually
        // self-defeating here, not just slow: every poll re-references the PIT with the SAME
        // KeepAlive=2s, which (per ES's PIT semantics) extends the deadline again from that poll's own
        // timestamp, so a repeated-read strategy can never observe expiry unless the gap between polls
        // safely exceeds however long the server takes to actually act on an elapsed deadline. ES does
        // not appear to validate a PIT reference's remaining keep_alive synchronously at query time —
        // the context stays servable until a periodic background sweep actually reaps it, and that
        // sweep's own cadence (not the requested keep_alive value) is what determines how soon
        // CursorExpired becomes observable. This test therefore performs exactly ONE read, after exactly
        // ONE long period of total inactivity — no intermediate reads to keep re-extending the deadline.
        await Task.Delay(TimeSpan.FromSeconds(75));
        var readResult = await cursorSearch.ReadCursorAsync(openResult.Value);

        readResult.IsFailure.Should().BeTrue();
        readResult.Error.Code.Should().Be("search.elasticsearch.cursor_expired");
    }

    [Fact]
    public async Task ReadCursorAsync_MalformedToken_ReturnsInvalidCursor()
    {
        var cursorSearch = ElasticsearchProviderFactory.CreateCursorSearch<TestProduct>(_client, _definition);
        var malformedCursor = new SearchCursor("not-valid-base64!!!", DateTimeOffset.UtcNow);

        var result = await cursorSearch.ReadCursorAsync(malformedCursor);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.elasticsearch.invalid_cursor");
    }

    [Fact]
    public async Task CloseCursorAsync_MalformedToken_ReturnsInvalidCursor()
    {
        var cursorSearch = ElasticsearchProviderFactory.CreateCursorSearch<TestProduct>(_client, _definition);
        var malformedCursor = new SearchCursor("not-valid-base64!!!", DateTimeOffset.UtcNow);

        var result = await cursorSearch.CloseCursorAsync(malformedCursor);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.elasticsearch.invalid_cursor");
    }
}
