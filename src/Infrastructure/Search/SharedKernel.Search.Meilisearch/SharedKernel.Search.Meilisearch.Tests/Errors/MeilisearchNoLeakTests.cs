using System.Net;
using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Errors;
using SharedKernel.Search.Meilisearch.Tests.Support;

namespace SharedKernel.Search.Meilisearch.Tests.Errors;

/// <summary>
/// No engine text in any <see cref="Error"/>: a stubbed HTTP layer beneath the real SDK (and the SDK's own
/// message handler, exactly as production composes them) answers every operation family with an error whose
/// message and link carry a sentinel, and no returned <c>Message</c>, <c>Details</c> or code may contain it,
/// nor the host. Every rejection is paired with a passing case that proves the stub is reached.
/// </summary>
public sealed class MeilisearchNoLeakTests
{
    private static readonly int[] FailureStatuses = [400, 401, 403, 404, 408, 409, 429, 500, 502, 503, 504];

    public static TheoryData<string> OperationNames => MeiliOperations.Names;

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task HealthyReply_ReachesTheStub(string operationName)
    {
        var handler = new ScriptedHttpHandler(HttpReply.Ok());
        var harness = new MeiliFaultHarness(handler);

        try
        {
            await MeiliOperations.All[operationName](harness, CancellationToken.None);
        }
        catch (Exception)
        {
            // An empty-object body is not a valid payload for every call; reaching the stub is the point.
        }

        handler.Requests.Should().NotBeEmpty($"'{operationName}' must reach the transport on a healthy reply");
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task AnEngineErrorBody_NeverReachesTheError_ForAnyStatusOrCode(string operationName)
    {
        var codes = new[] { "some_unmapped_code", "invalid_search_filter", "invalid_search_offset", "invalid_api_key", "index_not_found", "Has Spaces SENTINEL" };

        foreach (var status in FailureStatuses)
        {
            foreach (var code in codes)
            {
                var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.ApiError(status, code, MeiliFaultHarness.Sentinel)));

                var error = await MeiliOperations.All[operationName](harness, CancellationToken.None);

                if (error is null)
                {
                    continue;
                }

                AssertSafe(error, $"{operationName} status {status} code '{code}'");
                harness.AllLogs.Should().OnlyContain(
                    record => !record.Message.Contains(MeiliFaultHarness.Sentinel, StringComparison.Ordinal)
                              && !record.Message.Contains(MeiliFaultHarness.HostSentinel, StringComparison.Ordinal),
                    $"{operationName} status {status}: a log message must not carry engine text");
            }
        }
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task TheSameFaultClass_YieldsTheSameCodeAndType_OnEveryOperation(string operationName)
    {
        var cases = new (HttpReply Reply, string Code, ErrorType Type)[]
        {
            (HttpReply.Throws(new HttpRequestException("connect to " + MeiliFaultHarness.HostSentinel + " refused")), SearchErrorCodes.Unavailable, ErrorType.Unavailable),
            (HttpReply.Throws(new TaskCanceledException("timed out " + MeiliFaultHarness.HostSentinel)), SearchErrorCodes.Timeout, ErrorType.Timeout),
            (HttpReply.ApiError(401, "invalid_api_key", MeiliFaultHarness.Sentinel), SearchErrorCodes.EngineUnauthorized, ErrorType.Unexpected),
            (HttpReply.ApiError(403, "missing_authorization_header", MeiliFaultHarness.Sentinel), SearchErrorCodes.EngineUnauthorized, ErrorType.Unexpected),
        };

        foreach (var (reply, code, type) in cases)
        {
            var harness = new MeiliFaultHarness(new ScriptedHttpHandler(reply));

            var error = await MeiliOperations.All[operationName](harness, CancellationToken.None);

            error.Should().NotBeNull($"{operationName}");
            error!.Code.Should().Be(code, operationName);
            error.Type.Should().Be(type, operationName);
            AssertSafe(error, operationName);
        }
    }

    [Theory]
    [InlineData("invalid_search_filter", "search.invalid_filter")]
    [InlineData("invalid_document_filter", "search.invalid_filter")]
    [InlineData("invalid_search_offset", "search.invalid_request")]
    [InlineData("invalid_search_limit", "search.invalid_request")]
    [InlineData("some_unmapped_code", "search.engine_fault")]
    public async Task EngineRejection_KeepsItsMappedCode_WithKernelWordingAndTheBoundedCode(string engineCode, string expected)
    {
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.ApiError(400, engineCode, MeiliFaultHarness.Sentinel)));

        var result = await harness.Index.SearchAsync(SearchRequest.Default, MeiliOperations.Tenant);

        result.Error.Code.Should().Be(expected);
        result.Error.Message.Should().Contain(engineCode, "the bounded engine code is the one piece of engine text that survives")
            .And.NotContain(MeiliFaultHarness.Sentinel);
    }

    [Theory]
    [InlineData("a-hyphenated-code")]
    [InlineData("Has Spaces SENTINEL")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task AnUnboundedEngineCode_IsOmitted(string engineCode)
    {
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.ApiError(400, engineCode, MeiliFaultHarness.Sentinel)));

        var result = await harness.Index.SearchAsync(SearchRequest.Default, MeiliOperations.Tenant);

        result.Error.Code.Should().Be("search.engine_fault");
        result.Error.Message.Should().NotContain(engineCode).And.NotContain(MeiliFaultHarness.Sentinel);
    }

    [Theory]
    [InlineData("internal", true)]
    [InlineData("a.b_c1", true)]
    [InlineData("", false)]
    [InlineData("UPPER", false)]
    [InlineData("has space", false)]
    public void BoundedEngineCode_AcceptsOnlyShortSnakeCaseTokens(string code, bool accepted)
    {
        (MeilisearchFaultMapper.BoundedEngineCode(code) is not null).Should().Be(accepted);
        MeilisearchFaultMapper.BoundedEngineCode(null).Should().BeNull();
    }

    [Fact]
    public async Task EmptyAndHtmlErrorBodies_AreClassified_NotLeaked()
    {
        foreach (var body in new[] { string.Empty, "<html>" + MeiliFaultHarness.Sentinel + " bad gateway</html>" })
        {
            var harness = new MeiliFaultHarness(new ScriptedHttpHandler(new HttpReply(HttpStatusCode.BadGateway, body)));

            var result = await harness.Index.SearchAsync(SearchRequest.Default, MeiliOperations.Tenant);

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("search.engine_fault", "the SDK drops the HTTP status, so a body-less gateway fault is an engine fault");
            AssertSafe(result.Error, "gateway body");
        }
    }

    [Fact]
    public async Task FailedIndexingTask_CarriesOnlyTheBoundedEngineCode()
    {
        var handler = new ScriptedHttpHandler(request => request.Method == HttpMethod.Get
            ? HttpReply.Ok("{\"uid\":1,\"indexUid\":\"products\",\"status\":\"failed\",\"type\":\"documentAdditionOrUpdate\","
                + "\"error\":{\"message\":\"" + MeiliFaultHarness.Sentinel + "\",\"code\":\"Has Spaces SENTINEL\",\"type\":\"x\",\"link\":\"l\"}}")
            : HttpReply.Ok("{\"taskUid\":1,\"indexUid\":\"products\",\"status\":\"enqueued\",\"type\":\"documentAdditionOrUpdate\",\"enqueuedAt\":\"2026-01-01T00:00:00Z\"}"));
        var harness = new MeiliFaultHarness(handler);

        var result = await harness.Index.IndexAsync(MeiliFaultHarness.Doc(), SearchWriteConsistency.Searchable);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.meilisearch.indexing_task_failed");
        result.Error.Message.Should().NotContain(MeiliFaultHarness.Sentinel).And.Contain("unknown");
    }

    private static void AssertSafe(Error error, string context)
    {
        var text = error.Message + "|" + error.Code + "|" + string.Join("|", error.Details?.Select(d => d.ToString()) ?? []);
        foreach (var forbidden in new[] { MeiliFaultHarness.Sentinel, MeiliFaultHarness.HostSentinel, "http://", "https://", "link" })
        {
            text.Should().NotContain(forbidden, $"{context}: an Error must not carry engine or transport text");
        }
    }
}
