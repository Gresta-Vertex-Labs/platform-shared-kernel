using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Elastic.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Telemetry;
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.Errors;

/// <summary>
/// Classify or rethrow, and cancellation. Only a recognised engine or transport fault becomes a
/// <c>Result</c> failure; anything else (a defect) rethrows with its stack and still completes the span and the
/// duration histogram. The caller's cancellation always propagates and is never reported as a search failure.
/// </summary>
public sealed class ElasticSearchRethrowAndCancellationTests
{
    public static TheoryData<string> OperationNames => EsOperations.Names;

    [Fact]
    public async Task UnclassifiedClientException_Rethrows_TheSameInstance_NotASoftEngineFault()
    {
        var original = new InvalidOperationException("defect in the stack");
        var invoker = new ScriptedRequestInvoker(CannedReply.TransportFailure(original));
        var harness = new EsFaultHarness(invoker, "products-rethrow-a");

        var act = () => harness.Index.CountAsync(null, EsOperations.Tenant);

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        invoker.Requests.Should().NotBeEmpty("the failure came from the transport, not from a pre-flight rejection");
        Chain(thrown).Should().Contain(original, "the original instance travels with its stack, wrapped only by the client");
    }

    [Fact]
    public async Task UntranslatableFilterNode_ThrowsSwitchExpressionException_BeforeAnyIo()
    {
        // SearchValue has a private constructor; a kind outside the enum is the only way to build the
        // "future node an adapter was not taught" that the exhaustive translation switch must not hide.
        var constructor = typeof(SearchValue).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(SearchValueKind), typeof(string), typeof(long), typeof(double), typeof(bool), typeof(DateTimeOffset)])!;
        var rogue = (SearchValue)constructor.Invoke([(SearchValueKind)99, null, 0L, 0d, false, default(DateTimeOffset)]);
        var invoker = new ScriptedRequestInvoker(CannedReply.Ok());
        var harness = new EsFaultHarness(invoker, "products-rethrow-b");
        var request = SearchRequest.Default with { Filter = SearchFilter.Eq("status", rogue) };

        var act = () => harness.Index.SearchAsync(request, EsOperations.Tenant);

        var thrown = await act.Should().ThrowAsync<SwitchExpressionException>();
        thrown.Which.StackTrace.Should().Contain("ElasticSearchFilterCompiler", "the original stack is preserved");
        invoker.Requests.Should().BeEmpty("a defect in translation must not reach the engine");
    }

    [Fact]
    public async Task RethrownException_StillCompletesTheSpan_WithTheTypeNameAsDescription_AndTheHistogram()
    {
        const string indexName = "products-rethrow-telemetry";
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);
        var original = new InvalidOperationException("message that must never become a span description");
        var harness = new EsFaultHarness(new ScriptedRequestInvoker(CannedReply.TransportFailure(original)), indexName);

        var act = () => harness.Index.CountAsync(null, EsOperations.Tenant);
        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;

        var span = spans.RecordedActivities.Single(a => (string?)a.GetTagItem(SearchWellKnown.IndexTagName) == indexName);
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be(thrown.GetType().Name).And.NotContain("message that must never");
        span.GetTagItem("error.type").Should().Be(thrown.GetType().FullName);

        var measurement = metrics.MeasurementsOf(SearchWellKnown.OperationDurationMetricName)
            .Single(m => m.TryGetTag(SearchWellKnown.IndexTagName, out var value) && (string?)value == indexName);
        measurement.TryGetTag("error.type", out var errorType).Should().BeTrue();
        errorType.Should().Be(thrown.GetType().FullName);
    }

    [Fact]
    public async Task RecognisedFault_StillReturnsTheMappedResult()
    {
        var harness = EsFaultHarness.Failing(CannedReply.Error(503, EsFaultHarness.Sentinel));

        var result = await harness.Index.CountAsync(null, EsOperations.Tenant);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.unreachable");
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task CancellingTheCallersToken_InFlight_PropagatesOperationCanceled_AndLogsNoFailure(string operationName)
    {
        var (run, pageRead) = EsOperations.All[operationName];
        var invoker = new ScriptedRequestInvoker(request =>
            !pageRead || !request.RequestUri!.PathAndQuery.Contains("_pit", StringComparison.Ordinal)
                ? CannedReply.Hang()
                : CannedReply.Ok("{\"id\":\"pit-1\",\"succeeded\":true,\"num_freed\":1}"));
        var harness = new EsFaultHarness(invoker);
        using var cts = new CancellationTokenSource();

        var pending = run(harness, cts.Token);
        await invoker.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        var act = () => pending;
        await act.Should().ThrowAsync<OperationCanceledException>(
            $"'{operationName}': the caller's cancellation is an instruction, not an engine failure");
        harness.AllLogs.Should().NotContain(
            record => record.LogLevel >= LogLevel.Warning,
            "a cancelled request must not inflate the failure signal");
    }

    [Fact]
    public async Task HttpClientTimeout_WithTheTokenNotCancelled_IsSearchTimeout()
    {
        var invoker = new ScriptedRequestInvoker(CannedReply.Hang());
        var client = StubbedClient.Create(invoker, settings => settings.RequestTimeout(TimeSpan.FromMilliseconds(200)));
        var harness = new EsFaultHarness(invoker, client: client);

        var result = await harness.Index.CountAsync(null, EsOperations.Tenant, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.timeout");
        result.Error.Type.Should().Be(SharedKernel.Primitives.Errors.ErrorType.Timeout);
    }

    [Fact]
    public async Task ReadCursor_WithAClientTimeout_IsTimeout_NotCursorExpired()
    {
        var token = await EsFaultHarness.OpenHealthyCursorAsync();
        var invoker = new ScriptedRequestInvoker(CannedReply.Hang());
        var client = StubbedClient.Create(invoker, settings => settings.RequestTimeout(TimeSpan.FromMilliseconds(200)));
        var harness = new EsFaultHarness(invoker, client: client);

        var result = await harness.Cursor.ReadCursorAsync(token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.timeout");
    }

    private static IEnumerable<Exception> Chain(Exception? exception)
    {
        while (exception is not null)
        {
            yield return exception;
            exception = exception.InnerException;
        }
    }
}
