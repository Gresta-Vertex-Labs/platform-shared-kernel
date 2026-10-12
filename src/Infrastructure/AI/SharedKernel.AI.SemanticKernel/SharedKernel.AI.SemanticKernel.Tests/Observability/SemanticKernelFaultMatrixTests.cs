using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.SemanticKernel.Tests.Support;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.SemanticKernel.Tests.Observability;

/// <summary>
/// Drives every operation through the real registration (the real Semantic Kernel connector and the real OpenAI SDK
/// pipeline) over a scripted <see cref="HttpMessageHandler"/>: what each HTTP status, transport fault, timeout and
/// cancellation does to the result, the span, the metrics and the log, and that no request is ever dispatched twice.
/// </summary>
public sealed class SemanticKernelFaultMatrixTests
{
    private static readonly string[] AllSentinels =
    [
        SemanticKernelHarness.HostSentinel, SemanticKernelHarness.BodySentinel, SemanticKernelHarness.KeySentinel, "prompt-text-sentinel",
    ];

    public static TheoryData<string> OperationNames =>
    [
        IntelligenceOperations.Embed, IntelligenceOperations.EmbedMany, IntelligenceOperations.Complete, IntelligenceOperations.CompleteStream,
    ];

    private static async Task<Error?> RunAsync(SemanticKernelHarness harness, string operation, CancellationToken cancellationToken = default)
    {
        switch (operation)
        {
            case IntelligenceOperations.Embed:
                var embed = await harness.Embeddings.EmbedAsync("embed-text-sentinel", cancellationToken);
                return embed.IsFailure ? embed.Error : null;
            case IntelligenceOperations.EmbedMany:
                var many = await harness.Embeddings.EmbedManyAsync(["embed-text-sentinel", "second"], cancellationToken);
                return many.IsFailure ? many.Error : null;
            case IntelligenceOperations.Complete:
                var completion = await harness.Kernel.CompleteAsync(harness.Request(), cancellationToken);
                return completion.IsFailure ? completion.Error : null;
            default:
                try
                {
                    await foreach (var _ in harness.Kernel.CompleteStreamingAsync(harness.Request(), cancellationToken))
                    {
                    }

                    return null;
                }
                catch (IntelligenceStreamException ex)
                {
                    return ex.Error;
                }
        }
    }

    public static TheoryData<string, int, string, ErrorType> StatusMatrix
    {
        get
        {
            var rows = new (int Status, string Code, ErrorType Type)[]
            {
                (400, "intelligence.completion_failed", ErrorType.Unexpected),
                (401, "intelligence.unauthorized", ErrorType.Unauthorized),
                (403, "intelligence.unauthorized", ErrorType.Unauthorized),
                (404, "intelligence.model_not_found", ErrorType.NotFound),
                (408, "intelligence.timeout", ErrorType.Timeout),
                (429, "intelligence.rate_limited", ErrorType.Unavailable),
                (500, "intelligence.engine_fault", ErrorType.Unexpected),
                (502, "intelligence.unreachable", ErrorType.Unavailable),
                (503, "intelligence.unreachable", ErrorType.Unavailable),
                (504, "intelligence.unreachable", ErrorType.Unavailable),
            };

            var data = new TheoryData<string, int, string, ErrorType>();
            foreach (var operation in new[] { IntelligenceOperations.Embed, IntelligenceOperations.EmbedMany, IntelligenceOperations.Complete, IntelligenceOperations.CompleteStream })
            {
                foreach (var (status, code, type) in rows)
                {
                    data.Add(operation, status, code, type);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StatusMatrix))]
    public async Task EveryHttpStatus_MapsToTheCodeAndTypeOfItsCondition_WithNoProviderText_AndExactlyOneRequest(
        string operation, int status, string code, ErrorType type)
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Status(status));

        var error = await RunAsync(harness, operation);

        error.Should().NotBeNull();
        error!.Code.Should().Be(code);
        error.Type.Should().Be(type);
        error.Message.Should().NotContainAny(AllSentinels);
        error.Details.Should().BeNullOrEmpty();
        harness.Handler.Count.Should().Be(1, "the SDK's implicit retry is disabled and this package never retries unless asked");

        var span = harness.Spans.Should().ContainSingle().Which;
        span.OperationName.Should().Be($"ai {operation}");
        span.ShouldHaveKind(ActivityKind.Client)
            .ShouldBeRecordedFailure(error)
            .ShouldFollowSpanConvention()
            .ShouldNotLeak(AllSentinels);

        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        duration.TryGetTag(WellKnownTagKeys.Outcome, out var outcome).Should().BeTrue();
        outcome.Should().Be(WellKnownOutcomes.Failure);
        duration.TryGetTag(WellKnownTagKeys.ErrorType, out var errorType).Should().BeTrue();
        errorType.Should().Be(type.ToString());

        harness.Measurements(IntelligenceWellKnown.TokensInstrumentName).Should().BeEmpty("a failed call counts nothing");

        var logs = harness.PackageLogs;
        var fault = logs.Where(r => r.EventId.Id == 10307).Should().ContainSingle().Which;
        fault.LogLevel.Should().Be(LogLevel.Warning);
        fault.Exception.Should().NotBeNull();
        fault.TryGetProperty("ErrorCode", out var logged).Should().BeTrue();
        logged.Should().Be(code);
        AssertNoLeakIntoMessageOrState(logs);
    }

    private static void AssertNoLeakIntoMessageOrState(IReadOnlyList<SharedKernel.Testing.Logging.LogRecord> records)
    {
        foreach (var record in records)
        {
            var scanned = new List<string?> { record.Message, record.Template };
            if (record.State is not null)
            {
                scanned.AddRange(record.State.Select(p => p.Value?.ToString()));
            }

            foreach (var text in scanned.Where(t => !string.IsNullOrEmpty(t)))
            {
                text.Should().NotContainAny(AllSentinels);
            }
        }
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task ATransportFault_IsUnreachable_Unavailable_AndNamesNoHost(string operation)
    {
        using var harness = SemanticKernelHarness.Create(
            (_, _, _) => throw new HttpRequestException($"{SemanticKernelHarness.HostSentinel} connection refused {SemanticKernelHarness.BodySentinel}"));

        var error = await RunAsync(harness, operation);

        error!.Code.Should().Be("intelligence.unreachable");
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Message.Should().NotContainAny(AllSentinels);
        harness.Handler.Count.Should().Be(1);
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedFailure(error);
        harness.PackageLogs.Single(r => r.EventId.Id == 10307).Exception.Should().NotBeNull();
        AssertNoLeakIntoMessageOrState(harness.PackageLogs);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task AClientTimeout_WithTheCallersTokenLive_IsATimeoutResult_NotARawException(string operation)
    {
        using var harness = SemanticKernelHarness.Create(
            async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return await SemanticKernelHarness.Status(200);
            },
            httpTimeoutSeconds: 1);

        var error = await RunAsync(harness, operation);

        error!.Code.Should().Be("intelligence.timeout");
        error.Type.Should().Be(ErrorType.Timeout);
        harness.Handler.Count.Should().Be(1);
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedFailure(error);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task CallersCancellation_IsOperationCanceled_NotAResult_NotAnError_AndNeverRetried(string operation)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        using var harness = SemanticKernelHarness.Create(
            async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return await SemanticKernelHarness.Status(200);
            });

        var act = () => RunAsync(harness, operation, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Handler.Count.Should().Be(1);
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedCancellation();
        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        duration.TryGetTag(WellKnownTagKeys.Outcome, out var outcome).Should().BeTrue();
        outcome.Should().Be(WellKnownOutcomes.Canceled);
        duration.TryGetTag(WellKnownTagKeys.ErrorType, out _).Should().BeFalse();
        harness.PackageLogs.Where(r => r.LogLevel >= LogLevel.Warning).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task ADefectFromTheTransport_Propagates_AsTheSameInstance_AndIsRecordedAsAnException(string operation)
    {
        var defect = new InvalidOperationException($"defect {SemanticKernelHarness.HostSentinel}");
        using var harness = SemanticKernelHarness.Create((_, _, _) => throw defect);

        var act = () => RunAsync(harness, operation);

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        Unwrap(thrown).Should().BeSameAs(defect, "an unclassified exception must keep its identity and stack");
        harness.Spans.Should().ContainSingle().Which.ShouldHaveStatus(ActivityStatusCode.Error);
        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        duration.TryGetTag(WellKnownTagKeys.Outcome, out var outcome).Should().BeTrue();
        outcome.Should().Be(WellKnownOutcomes.Exception);
        harness.PackageLogs.Where(r => r.LogLevel >= LogLevel.Warning).Should().BeEmpty();
    }

    private static Exception Unwrap(Exception exception)
    {
        // The connector may wrap an unclassified transport defect; the instance must still be reachable unchanged.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException)
            {
                return current;
            }
        }

        return exception;
    }

    [Fact]
    public async Task AnUnknownChatRole_IsADefect_ThatPropagatesBeforeAnyRequest()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Chat("x", 1, 1));
        var request = new CompletionRequest { Messages = [new ChatMessage { Role = (ChatRole)99, Content = "prompt-text-sentinel" }] };

        var act = () => harness.Kernel.CompleteAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Handler.Count.Should().Be(0);
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedException(typeof(InvalidOperationException));
    }

    [Fact]
    public async Task Rejections_AreSpansToo_WithNoRequest()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Embedding(1, [1f, 0f, 0f]));

        var empty = await harness.Embeddings.EmbedManyAsync([]);
        var tooMany = await harness.Embeddings.EmbedManyAsync(Enumerable.Repeat("x", 5000).ToList());

        empty.Error.Code.Should().Be("intelligence.invalid_query");
        tooMany.Error.Code.Should().Be("intelligence.batch_size_exceeded");
        harness.Handler.Count.Should().Be(0);
        harness.Spans.Should().HaveCount(2).And.OnlyContain(s => s.Status == ActivityStatusCode.Error && s.OperationName == "ai embed_many");
        harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().HaveCount(2);
        var rejected = harness.PackageLogs.Single(r => r.EventId.Id == 10305);
        rejected.TryGetProperty("ErrorType", out _).Should().BeTrue();
        rejected.TryGetProperty("ErrorCode", out _).Should().BeTrue();
        rejected.TryGetProperty("Reason", out _).Should().BeFalse();
    }
}
