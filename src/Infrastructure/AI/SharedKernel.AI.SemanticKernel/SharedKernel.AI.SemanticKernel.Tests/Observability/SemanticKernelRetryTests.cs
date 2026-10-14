using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.SemanticKernel.Tests.Support;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.SemanticKernel.Tests.Observability;

/// <summary>
/// There is no hidden second dispatch: the SDK's own retry policy is disabled, so <c>CompleteAsync</c> and
/// <c>EmbedManyAsync</c> reach the endpoint exactly once unless <c>WithBoundedRetry</c> was configured, and then only for
/// a real HTTP 429 or 5xx status (verified against System.ClientModel 1.10.0, whose default policy dispatches four
/// requests for a 429, 5xx, 408, transport fault or timeout).
/// </summary>
public sealed class SemanticKernelRetryTests
{
    private static string? Tag(RecordedMeasurement measurement, string key) =>
        measurement.TryGetTag(key, out var value) ? (string?)value : null;

    [Theory]
    [InlineData(429, "intelligence.rate_limited")]
    [InlineData(503, "intelligence.unavailable")]
    [InlineData(500, "intelligence.engine_fault")]
    public async Task WithoutBoundedRetry_CompleteAsync_IsRequestedExactlyOnce_AndFailsWithTheMappedError(int status, string code)
    {
        using var harness = SemanticKernelHarness.Create((attempt, _, _) => attempt == 1
            ? SemanticKernelHarness.Status(status)
            : SemanticKernelHarness.Chat("ok", 1, 1));

        var result = await harness.Kernel.CompleteAsync(harness.Request());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(code);
        harness.Handler.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(500)]
    public async Task WithoutBoundedRetry_EmbedManyAsync_IsRequestedExactlyOnce(int status)
    {
        using var harness = SemanticKernelHarness.Create((attempt, _, _) => attempt == 1
            ? SemanticKernelHarness.Status(status)
            : SemanticKernelHarness.Embedding(1, [1f, 0f, 0f]));

        var result = await harness.Embeddings.EmbedManyAsync(["a"]);

        result.IsFailure.Should().BeTrue();
        harness.Handler.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(500)]
    public async Task WithBoundedRetry_ATransientStatus_IsRepeatedOnce_AndTheCounterCountsOnlyTheFinalUsage(int status)
    {
        using var harness = SemanticKernelHarness.Create(
            (attempt, _, _) => attempt == 1 ? SemanticKernelHarness.Status(status) : SemanticKernelHarness.Chat("ok", promptTokens: 11, completionTokens: 7),
            configure: builder => builder.WithBoundedRetry(3, TimeSpan.FromMilliseconds(1)));

        var result = await harness.Kernel.CompleteAsync(harness.Request());

        result.IsSuccess.Should().BeTrue();
        harness.Handler.Count.Should().Be(2);
        harness.Spans.Should().ContainSingle("a retry stays inside its call's span");
        harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle();
        harness.Measurements(IntelligenceWellKnown.TokensInstrumentName).Sum(m => (long)m.Value).Should().Be(18);

        var retry = harness.PackageLogs.Should().ContainSingle(r => r.EventId.Id == 10309).Which;
        retry.LogLevel.Should().Be(LogLevel.Warning);
        retry.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task WithBoundedRetry_TheBudgetIsBounded_AndTheLastFailureIsReturned()
    {
        using var harness = SemanticKernelHarness.Create(
            (_, _, _) => SemanticKernelHarness.Status(429),
            configure: builder => builder.WithBoundedRetry(3, TimeSpan.FromMilliseconds(1)));

        var result = await harness.Kernel.CompleteAsync(harness.Request());

        result.Error.Code.Should().Be("intelligence.rate_limited");
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        harness.Handler.Count.Should().Be(3);
        harness.PackageLogs.Count(r => r.EventId.Id == 10309).Should().Be(2);
        harness.Measurements(IntelligenceWellKnown.TokensInstrumentName).Should().BeEmpty();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(408)]
    public async Task WithBoundedRetry_AClientShapedFailureIsNeverRetried(int status)
    {
        using var harness = SemanticKernelHarness.Create(
            (_, _, _) => SemanticKernelHarness.Status(status),
            configure: builder => builder.WithBoundedRetry(5, TimeSpan.FromMilliseconds(1)));

        var result = await harness.Kernel.CompleteAsync(harness.Request());

        result.IsFailure.Should().BeTrue();
        harness.Handler.Count.Should().Be(1);
    }

    [Fact]
    public async Task WithBoundedRetry_ATransportFault_IsNotRetried_BecauseItHasNoHttpStatus()
    {
        using var harness = SemanticKernelHarness.Create(
            (_, _, _) => throw new HttpRequestException("refused"),
            configure: builder => builder.WithBoundedRetry(5, TimeSpan.FromMilliseconds(1)));

        var result = await harness.Kernel.CompleteAsync(harness.Request());

        result.Error.Code.Should().Be("intelligence.unavailable");
        harness.Handler.Count.Should().Be(1);
    }

    [Fact]
    public async Task WithBoundedRetry_CancellationDuringTheBackoff_IsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        using var harness = SemanticKernelHarness.Create(
            (_, _, _) =>
            {
                cts.CancelAfter(TimeSpan.FromMilliseconds(50));
                return SemanticKernelHarness.Status(429);
            },
            configure: builder => builder.WithBoundedRetry(3, TimeSpan.FromSeconds(30)));

        var act = () => harness.Kernel.CompleteAsync(harness.Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Handler.Count.Should().Be(1);
    }

    [Fact]
    public async Task Streaming_IsNeverRetried_EvenWithBoundedRetryConfigured()
    {
        using var harness = SemanticKernelHarness.Create(
            (_, _, _) => SemanticKernelHarness.Status(429),
            configure: builder => builder.WithBoundedRetry(5, TimeSpan.FromMilliseconds(1)));

        var act = async () =>
        {
            await foreach (var _ in harness.Kernel.CompleteStreamingAsync(harness.Request()))
            {
            }
        };

        await act.Should().ThrowAsync<SharedKernel.AI.Abstractions.Exceptions.IntelligenceStreamException>();
        harness.Handler.Count.Should().Be(1);
    }
}
