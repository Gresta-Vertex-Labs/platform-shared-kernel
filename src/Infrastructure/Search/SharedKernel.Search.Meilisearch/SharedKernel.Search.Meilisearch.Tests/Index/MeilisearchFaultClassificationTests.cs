using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Index;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Search.Meilisearch.Tests.Index;

/// <summary>
/// Container-free tests for the fault classification the pre-publish pass introduced. The Meilisearch
/// SDK signals every failure by throwing, so before the pass an unreachable instance escaped these
/// <c>Result</c>-returning members as an exception — while the ElasticSearch sibling, whose client does
/// not throw, returned a failed <c>Result</c> for the identical condition. Two providers behaving
/// differently on "the search engine is down" is not something a consumer can write correct code
/// against, and it is exactly what this domain's intersection-only rule exists to prevent.
/// </summary>
/// <remarks>
/// Every test here points a real <c>MeilisearchClient</c> at <c>http://127.0.0.1:1/</c> — a
/// syntactically valid, guaranteed-unreachable URL, since nothing listens on port 1. That exercises the
/// genuine SDK throw path rather than a simulated one, with no container and no network dependency.
/// </remarks>
public sealed class MeilisearchFaultClassificationTests
{
    private const string UnreachableUrl = "http://127.0.0.1:1/";

    /// <summary>
    /// The two codes that both mean "the engine did not answer; retry is meaningful". Which one a given
    /// host produces depends on whether the network refuses the connection outright (unreachable) or
    /// drops it and lets the HttpClient timeout elapse (timeout) — a firewall policy difference, not a
    /// behavioural one. Asserting the class rather than one code keeps these tests honest on every host
    /// while still proving the point: an operational failure is never reported as a malformed request,
    /// a missing document, or an unclassified engine fault.
    /// </summary>
    private static readonly string[] OperationalFailureCodes = ["search.unreachable", "search.timeout"];

    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    private static MeilisearchIndex<TestDocument> CreateIndexAgainstUnreachableInstance()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri(UnreachableUrl), Timeout = TimeSpan.FromSeconds(2) };
        return new MeilisearchIndex<TestDocument>(
            new global::Meilisearch.MeilisearchClient(httpClient, "test-key"),
            Definition(),
            new MeilisearchOptions { Url = UnreachableUrl, TaskWaitTimeoutSeconds = 1 },
            new FakeClock(),
            NullLogger<MeilisearchIndex<TestDocument>>.Instance);
    }

    private static SearchIndexDefinition Definition() => new SearchIndexDefinitionBuilder("products")
        .Field("name", SearchFieldKind.Text, searchable: true)
        .Field("status", SearchFieldKind.Keyword, filterable: true)
        .Build()
        .Value;

    [Fact]
    public async Task SearchAsync_AgainstAnUnreachableInstance_ReturnsUnreachable_NotAThrownException()
    {
        var index = CreateIndexAgainstUnreachableInstance();

        var act = async () => await index.SearchAsync(SearchRequest.Default, TenantScope.Global);

        var result = await act.Should().NotThrowAsync(
            "an unreachable search engine is an operational condition, not an exception — the same rule " +
            "07.Messaging and 08.Storage adopted for an unreachable broker and an unreachable bucket");
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().BeOneOf(
            OperationalFailureCodes,
            "a caller must be able to tell 'retry in a moment' from 'this request will never succeed'");
    }

    [Fact]
    public async Task SearchAsync_AgainstAnUnreachableInstance_IsAnUnavailableOrTimeoutError_NeverUnexpected()
    {
        // P-562: the code has always said "retry in a moment"; the error type now says so too, so the
        // HTTP boundary answers 503 or 504 for a down engine instead of 500.
        var index = CreateIndexAgainstUnreachableInstance();

        var result = await index.SearchAsync(SearchRequest.Default, TenantScope.Global);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(
            result.Error.Code == "search.timeout" ? ErrorType.Timeout : ErrorType.Unavailable,
            "search.unreachable is an Unavailable error and search.timeout a Timeout error");
    }

    [Fact]
    public async Task IndexAsync_AgainstAnUnreachableInstance_ReturnsUnreachable_NotAThrownException()
    {
        var index = CreateIndexAgainstUnreachableInstance();

        var act = async () => await index.IndexAsync(
            new TestDocument { DocumentId = "doc-1" }, SearchWriteConsistency.Accepted);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().BeOneOf(OperationalFailureCodes);
    }

    [Fact]
    public async Task CountAsync_AgainstAnUnreachableInstance_ReturnsUnreachable_NotAThrownException()
    {
        var index = CreateIndexAgainstUnreachableInstance();

        var act = async () => await index.CountAsync(filter: null, TenantScope.Global);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().BeOneOf(OperationalFailureCodes);
    }

    [Fact]
    public async Task DeleteAsync_AgainstAnUnreachableInstance_ReturnsUnreachable_NotAThrownException()
    {
        var index = CreateIndexAgainstUnreachableInstance();

        var act = async () => await index.DeleteAsync("doc-1", SearchWriteConsistency.Accepted);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().BeOneOf(OperationalFailureCodes);
    }

    [Fact]
    public async Task GetAsync_AgainstAnUnreachableInstance_ReturnsUnreachable_NeverDocumentNotFound()
    {
        // The sharpest case in this file. GetAsync absorbs a genuine 404 into DocumentNotFound, and
        // before the pass its catch was broad enough to swallow any API error that way — so an
        // unreachable instance or a rejected API key would have told the caller its document was gone.
        // "Your data does not exist" and "I cannot reach the engine" must never be the same answer.
        var index = CreateIndexAgainstUnreachableInstance();

        var act = async () => await index.GetAsync("doc-1", TenantScope.Global);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().BeOneOf(OperationalFailureCodes);
        result.Subject.Error.Code.Should().NotBe("search.document_not_found");
    }

    [Fact]
    public async Task EnumerateAsync_AgainstAnUnreachableInstance_ThrowsSearchStreamExceptionCarryingUnreachable()
    {
        // EnumerateAsync is the domain's one documented exception to the Result-first rule, following
        // the src/Infrastructure/Persistence/08.Storage streaming-read precedent — but the error it carries is now
        // classified identically to every other member's.
        var index = CreateIndexAgainstUnreachableInstance();

        var act = async () =>
        {
            await foreach (var _ in index.EnumerateAsync(filter: null, TenantScope.Global, batchSize: 10))
            {
                // Intentionally empty: the first MoveNextAsync is what faults.
            }
        };

        var exception = await act.Should().ThrowAsync<SharedKernel.Search.Abstractions.Exceptions.SearchStreamException>();
        exception.Which.Error.Code.Should().BeOneOf(OperationalFailureCodes);
    }

    [Fact]
    public async Task CancellationIsNeverReportedAsAnEngineFailure()
    {
        // Cancellation is the caller's own instruction. Reporting it as a search error would make a
        // cancelled request indistinguishable from a broken cluster on every dashboard, and would
        // inflate the very error rate that is supposed to signal a real outage.
        var index = CreateIndexAgainstUnreachableInstance();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await index.SearchAsync(SearchRequest.Default, TenantScope.Global, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
