using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using Result = SharedKernel.Primitives.Results.Result;
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.Errors;

/// <summary>
/// No engine text in any <see cref="Error"/>: a stubbed transport answers every operation family with an
/// error whose body, reason and root cause all carry a sentinel, and no returned <c>Message</c>, <c>Details</c>
/// or code may contain it, nor the node host, nor a debug-information marker. Every rejection is paired with
/// a passing case that proves the same stub is reached by a healthy request.
/// </summary>
public sealed class ElasticSearchNoLeakTests
{
    private static readonly TenantScope Tenant = TenantScope.For(TestTenants.TenantA);

    private static readonly int[] FailureStatuses = [400, 401, 403, 404, 408, 409, 429, 500, 502, 503, 504];

    private static Dictionary<string, (EsOperations.Operation Run, bool PageRead)> Operations => EsOperations.All;

    public static TheoryData<string> OperationNames => EsOperations.Names;

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task HealthyReply_ReachesTheStub(string operationName)
    {
        // The companion to every rejection below: the same harness, with a reply that is not an error, does
        // reach the transport. An empty ok body is not a valid payload for every call, so a payload fault is
        // tolerated; what is asserted is that the stub was reached.
        var (run, _) = Operations[operationName];
        var invoker = new ScriptedRequestInvoker(CannedReply.Ok());
        var harness = new EsFaultHarness(invoker);

        try
        {
            await run(harness, CancellationToken.None);
        }
        catch (Exception)
        {
            // A malformed-but-200 body may surface as a payload exception; reaching the stub is the point.
        }

        invoker.Requests.Should().NotBeEmpty($"'{operationName}' must reach the transport on a healthy reply");
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task AnEngineErrorBody_NeverReachesTheError_ForAnyStatus(string operationName)
    {
        var (run, pageRead) = Operations[operationName];

        foreach (var status in FailureStatuses)
        {
            var reply = CannedReply.Error(status, EsFaultHarness.Sentinel);
            var harness = pageRead ? EsFaultHarness.WithOpenPointInTime(reply) : EsFaultHarness.Failing(reply);

            var error = await run(harness, CancellationToken.None);

            if (error is null)
            {
                continue;
            }

            AssertSafe(error, $"{operationName} status {status}");
            harness.AllLogs.Should().OnlyContain(
                record => !record.Message.Contains(EsFaultHarness.Sentinel, StringComparison.Ordinal)
                          && !record.Message.Contains(EsFaultHarness.HostSentinel, StringComparison.Ordinal),
                $"{operationName} status {status}: a log message must not carry engine text");
        }
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task OutageAuthAndTimeoutStatuses_AreClassifiedIdentically_OnEveryOperation(string operationName)
    {
        var (run, pageRead) = Operations[operationName];
        var expectations = new (int Status, string Code, ErrorType Type)[]
        {
            (503, "search.unreachable", ErrorType.Unavailable),
            (502, "search.unreachable", ErrorType.Unavailable),
            (504, "search.unreachable", ErrorType.Unavailable),
            (429, "search.unreachable", ErrorType.Unavailable),
            (401, "search.unauthorized", ErrorType.Unauthorized),
            (403, "search.unauthorized", ErrorType.Unauthorized),
            (408, "search.timeout", ErrorType.Timeout),
        };

        foreach (var (status, code, type) in expectations)
        {
            var reply = CannedReply.Error(status, EsFaultHarness.Sentinel);
            var harness = pageRead ? EsFaultHarness.WithOpenPointInTime(reply) : EsFaultHarness.Failing(reply);

            var error = await run(harness, CancellationToken.None);

            error.Should().NotBeNull($"{operationName} must fail on {status}");
            error!.Code.Should().Be(code, $"{operationName} on {status}");
            error.Type.Should().Be(type, $"{operationName} on {status}");
        }
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task TransportFailures_AreUnreachableOrTimeout_AndLeakNoHost(string operationName)
    {
        var (run, pageRead) = Operations[operationName];
        var cases = new (CannedReply Reply, string Code)[]
        {
            (CannedReply.TransportFailure(new HttpRequestException("connect to " + EsFaultHarness.HostSentinel + " refused")), "search.unreachable"),
            (CannedReply.TransportFailure(new TaskCanceledException("request to " + EsFaultHarness.HostSentinel + " timed out")), "search.timeout"),
            (CannedReply.TransportFailure(new TaskCanceledException("timed out", new TimeoutException(EsFaultHarness.HostSentinel))), "search.timeout"),
        };

        foreach (var (reply, code) in cases)
        {
            var harness = pageRead ? EsFaultHarness.WithOpenPointInTime(reply) : EsFaultHarness.Failing(reply);

            var error = await run(harness, CancellationToken.None);

            error.Should().NotBeNull(operationName);
            error!.Code.Should().Be(code, operationName);
            AssertSafe(error, operationName);
        }
    }

    [Theory]
    [InlineData("mapper_parsing_exception", true)]
    [InlineData("Has Spaces And Capitals", false)]
    [InlineData("a-hyphenated-type", false)]
    public async Task OnlyABoundedEngineErrorCode_SurvivesInTheMessage(string errorType, bool survives)
    {
        var harness = EsFaultHarness.Failing(CannedReply.ErrorOfType(500, errorType, EsFaultHarness.Sentinel));

        var result = await harness.Index.CountAsync(null, Tenant);

        result.Error.Code.Should().Be("search.engine_fault");
        result.Error.Message.Contains(errorType, StringComparison.Ordinal).Should().Be(survives);
        result.Error.Message.Should().NotContain(EsFaultHarness.Sentinel);
    }

    [Fact]
    public async Task BulkItemRejections_CarryOnlyTheBoundedErrorType_NotTheServerReason()
    {
        const string body = "{\"took\":1,\"errors\":true,\"items\":[{\"index\":{\"_index\":\"products\",\"_id\":\"doc-1\",\"status\":400,"
            + "\"error\":{\"type\":\"mapper_parsing_exception\",\"reason\":\"failed to parse field [price] value " + EsFaultHarness.Sentinel + "\"}}}]}";
        var harness = EsFaultHarness.Failing(CannedReply.Ok(body));

        var result = await harness.Index.IndexManyAsync([EsFaultHarness.Doc()], SearchWriteConsistency.Accepted);

        result.IsSuccess.Should().BeTrue("per-item failures are reported on the receipt, not as a request failure");
        var failure = result.Value.Failures.Should().ContainSingle().Subject;
        failure.Error.Message.Should().Contain("mapper_parsing_exception").And.NotContain(EsFaultHarness.Sentinel);
    }

    [Fact]
    public async Task DeleteMany_WhenTheWholeRequestFails_IsAFailure_NotASilentSuccess()
    {
        var harness = EsFaultHarness.Failing(CannedReply.Error(503, EsFaultHarness.Sentinel));

        var result = await harness.Index.DeleteManyAsync(["doc-1", "doc-2"], SearchWriteConsistency.Accepted);

        result.IsFailure.Should().BeTrue("a bulk request that did not execute must not report success with zero failures");
        result.Error.Code.Should().Be("search.unreachable");
    }

    private static void AssertSafe(Error error, string context)
    {
        var text = error.Message + "|" + error.Code + "|" + string.Join("|", error.Details?.Select(d => d.ToString()) ?? []);
        foreach (var forbidden in new[]
                 {
                     EsFaultHarness.Sentinel, EsFaultHarness.HostSentinel, "DebugInformation", "root_cause",
                     "Invalid Elasticsearch response", "http://", "https://",
                 })
        {
            text.Should().NotContain(forbidden, $"{context}: an Error must not carry engine or transport text");
        }
    }

}
