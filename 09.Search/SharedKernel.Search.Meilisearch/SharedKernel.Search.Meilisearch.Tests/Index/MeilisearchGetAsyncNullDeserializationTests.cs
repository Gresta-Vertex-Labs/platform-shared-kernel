using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Index;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Search.Meilisearch.Tests.Index;

/// <summary>
/// T-28 (P-353): exercises <see cref="MeilisearchIndex{TDocument}.GetAsync"/>'s null-deserialization
/// path — the engine responds 200 OK with a document body that deserializes to <see langword="null"/>
/// — and asserts a <see cref="Result{T}.Failure"/> carrying <c>SearchErrors.EngineFault</c> is
/// returned, with no exception escaping <c>GetAsync</c>.
/// </summary>
/// <remarks>
/// Container-free: <c>global::Meilisearch.MeilisearchClient</c> ships no interface (see
/// <c>MeilisearchPreflightValidationTests</c>'s own remarks), so it cannot be substituted via
/// NSubstitute directly. Instead, a real <c>MeilisearchClient</c> is constructed over a fake
/// <see cref="HttpMessageHandler"/> that returns a literal JSON <c>null</c> body for every request —
/// the SDK-level seam the client itself is built from (<c>MeilisearchClient(HttpClient, string)</c>),
/// never a mocked SDK type. This reproduces the exact scenario
/// <c>MeilisearchIndex{TDocument}.GetAsync</c>'s <c>raw.Deserialize&lt;TDocument&gt;(...)</c> call
/// guards against: <see cref="System.Text.Json.JsonElement.Deserialize{T}(System.Text.Json.JsonSerializerOptions?)"/>
/// returns <see langword="null"/> for a reference type when the underlying token is JSON
/// <c>null</c> — a case a real, healthy Meilisearch engine will never itself produce for an existing
/// document, but one the adapter must still fail loud against rather than propagate a
/// <see cref="NullReferenceException"/> to the caller.
/// </remarks>
public sealed class MeilisearchGetAsyncNullDeserializationTests
{
    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }

        public required string Name { get; init; }
    }

    /// <summary>Returns 200 OK with a literal JSON <c>null</c> body for every request it handles.</summary>
    private sealed class NullBodyHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private static SearchIndexDefinition Definition() => new SearchIndexDefinitionBuilder("null-body-products")
        .Field("name", SearchFieldKind.Text, searchable: true)
        .Build()
        .Value;

    [Fact]
    public async Task GetAsync_EngineReturnsNullDeserializedDocument_ReturnsFailure_WithEngineFault_AndNoException()
    {
        var handler = new NullBodyHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:7700") };
        var client = new global::Meilisearch.MeilisearchClient(httpClient, "test-api-key");
        var index = new MeilisearchIndex<TestDocument>(
            client, Definition(), new MeilisearchOptions(), new FakeClock(), NullLogger<MeilisearchIndex<TestDocument>>.Instance);

        var act = async () => await index.GetAsync("some-document-id", TenantScope.None);

        var result = await act.Should().NotThrowAsync(
            "a null-deserialized document must surface as a Result failure, never an unhandled exception");
        result.Subject.IsFailure.Should().BeTrue();
        result.Subject.Error.Code.Should().Be("search.engine_fault");
        // The operation name in the error is the telemetry operation ("get"), not the C# member name —
        // the same string the search.operation span tag and the duration histogram carry, so an error in
        // a log can be joined to its metric series.
        result.Subject.Error.Message.Should().Contain("'get'").And.Contain("null document");
        handler.RequestCount.Should().Be(1, "GetAsync issues exactly one HTTP call for a get-by-id");
    }
}
