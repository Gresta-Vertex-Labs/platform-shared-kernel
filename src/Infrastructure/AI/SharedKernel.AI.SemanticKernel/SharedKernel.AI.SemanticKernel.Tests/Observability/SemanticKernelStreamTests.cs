using System.Diagnostics;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.SemanticKernel.Tests.Support;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.SemanticKernel.Tests.Observability;

/// <summary>
/// <c>CompleteStreamingAsync</c> is one of the two non-<c>Result</c> surfaces: a recognised fault is an
/// <see cref="IntelligenceStreamException"/>, a defect propagates untouched, the caller's cancellation is an
/// <see cref="OperationCanceledException"/>, usage is counted once, and the span ends however the enumeration ends
/// without ever replacing the consumer's ambient activity.
/// </summary>
public sealed class SemanticKernelStreamTests
{
    private static string? Tag(RecordedMeasurement measurement, string key) =>
        measurement.TryGetTag(key, out var value) ? (string?)value : null;

    /// <summary>An event-stream body that serves one chunk and then fails the way a dropped connection does.</summary>
    private sealed class DroppingContent(Exception failure) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            var first = Encoding.UTF8.GetBytes(
                "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"gpt-test\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"one\"},\"finish_reason\":null}]}\n\n");
            await stream.WriteAsync(first);
            await stream.FlushAsync();
            throw failure;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    [Fact]
    public async Task ACompleteStream_YieldsTheChunks_CountsTheFinalUsageOnce_AndIsOneSuccessfulSpan()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.ChatStream("Hel", "lo"));

        var chunks = new List<CompletionChunk>();
        await foreach (var chunk in harness.Kernel.CompleteStreamingAsync(harness.Request()))
        {
            chunks.Add(chunk);
        }

        string.Concat(chunks.Select(c => c.DeltaContent)).Should().Be("Hello");
        harness.Handler.Count.Should().Be(1);

        harness.Spans.Should().ContainSingle().Which
            .ShouldHaveKind(ActivityKind.Client)
            .ShouldHaveTag(IntelligenceTagKeys.Operation, IntelligenceOperations.CompleteStream)
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention()
            .ShouldNotLeak("prompt-text-sentinel", "Hel", SemanticKernelHarness.KeySentinel);

        harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle();
        long Sum(string type) => harness.Measurements(IntelligenceWellKnown.TokensInstrumentName)
            .Where(m => Tag(m, IntelligenceTagKeys.TokenType) == type).Sum(m => (long)m.Value);
        Sum(IntelligenceWellKnown.TokenTypeInput).Should().Be(5, "the final chunk's usage, counted once");
        Sum(IntelligenceWellKnown.TokenTypeOutput).Should().Be(3);
    }

    [Fact]
    public async Task AConnectionDroppedMidStream_IsAnIntelligenceStreamException_WithTheMappedError_AndTheInnerException()
    {
        var dropped = new IOException($"{SemanticKernelHarness.HostSentinel} {SemanticKernelHarness.BodySentinel} connection reset");
        using var harness = SemanticKernelHarness.Create((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new DroppingContent(dropped),
        }));
        var seen = new List<string>();

        var act = async () =>
        {
            await foreach (var chunk in harness.Kernel.CompleteStreamingAsync(harness.Request()))
            {
                seen.Add(chunk.DeltaContent);
            }
        };

        var thrown = (await act.Should().ThrowAsync<IntelligenceStreamException>()).Which;
        thrown.Should().BeAssignableTo<SharedKernelException>().And.BeAssignableTo<IHasError>();
        thrown.Error.Code.Should().Be("intelligence.unavailable");
        thrown.Error.Type.Should().Be(ErrorType.Unavailable);
        thrown.Error.Message.Should().NotContain("sentinel").And.NotContain(SemanticKernelHarness.HostSentinel);
        thrown.Message.Should().Be(thrown.Error.Message);
        thrown.InnerException.Should().NotBeNull();
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedFailure(thrown.Error);
        harness.Measurements(IntelligenceWellKnown.TokensInstrumentName).Should().BeEmpty();
        harness.PackageLogs.Should().ContainSingle(r => r.EventId.Id == 10307).Which.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task ADefectMidStream_PropagatesUntouched()
    {
        var defect = new InvalidOperationException("defect");
        using var harness = SemanticKernelHarness.Create((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new DroppingContent(defect),
        }));

        var act = async () =>
        {
            await foreach (var _ in harness.Kernel.CompleteStreamingAsync(harness.Request()))
            {
            }
        };

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        thrown.Should().NotBeOfType<IntelligenceStreamException>();
        harness.Spans.Should().ContainSingle().Which.ShouldHaveStatus(ActivityStatusCode.Error);
        harness.PackageLogs.Where(r => r.LogLevel >= LogLevel.Warning).Should().BeEmpty();
    }

    [Fact]
    public async Task CallersCancellation_MidStream_IsOperationCanceled_NotWrapped_AndNotAnError()
    {
        using var cts = new CancellationTokenSource();
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.ChatStream("a", "b", "c"));

        var act = async () =>
        {
            await foreach (var _ in harness.Kernel.CompleteStreamingAsync(harness.Request(), cts.Token))
            {
                await cts.CancelAsync();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedCancellation();
        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Canceled);
        harness.PackageLogs.Where(r => r.LogLevel >= LogLevel.Warning).Should().BeEmpty();
    }

    [Fact]
    public async Task AnAbandonedStream_StillEndsItsSpan_AndCountsNoTokens()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.ChatStream("a", "b", "c"));

        await using (var enumerator = harness.Kernel.CompleteStreamingAsync(harness.Request()).GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            harness.Spans.Should().BeEmpty("the span is still open while the enumerator is alive");
        }

        harness.Spans.Should().ContainSingle().Which.IsStopped.Should().BeTrue();
        harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle();
        harness.Measurements(IntelligenceWellKnown.TokensInstrumentName).Should().BeEmpty("usage arrives with the final chunk, which was never read");
    }

    [Fact]
    public async Task TheConsumersActivity_IsNeverReplaced_BetweenChunks_OrAfterTheStream()
    {
        using var consumer = new Activity("consumer").Start();
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.ChatStream("a", "b", "c"));
        var betweenChunks = new List<Activity?>();

        await foreach (var _ in harness.Kernel.CompleteStreamingAsync(harness.Request()))
        {
            betweenChunks.Add(Activity.Current);
        }

        betweenChunks.Should().NotBeEmpty().And.OnlyContain(a => ReferenceEquals(a, consumer));
        Activity.Current.Should().BeSameAs(consumer);
        harness.Spans.Should().ContainSingle().Which.Parent.Should().BeSameAs(consumer);
    }
}
