using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.SemanticKernel.Diagnostics;
using SharedKernel.AI.SemanticKernel.Tests.Support;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.SemanticKernel.Tests.Observability;

/// <summary>Spans, the duration histogram and the token counter on the success path, through the real connector.</summary>
public sealed class SemanticKernelTelemetryTests
{
    private static string? Tag(RecordedMeasurement measurement, string key) =>
        measurement.TryGetTag(key, out var value) ? (string?)value : null;

    private static long Tokens(SemanticKernelHarness harness, string operation, string tokenType) =>
        harness.Measurements(IntelligenceWellKnown.TokensInstrumentName)
            .Where(m => Tag(m, IntelligenceTagKeys.Operation) == operation && Tag(m, IntelligenceTagKeys.TokenType) == tokenType)
            .Sum(m => (long)m.Value);

    [Fact]
    public async Task Complete_IsOneClientSpan_WithTheExactTags_OneSuccessDuration_AndTheReturnedUsageCountedOnce()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Chat("completion-text-sentinel", promptTokens: 11, completionTokens: 7));

        var result = await harness.Kernel.CompleteAsync(harness.Request());

        result.IsSuccess.Should().BeTrue();
        result.Value.TokenUsage.PromptTokens.Should().Be(11);
        harness.Handler.Count.Should().Be(1);

        harness.Spans.Should().ContainSingle().Which
            .ShouldHaveKind(ActivityKind.Client)
            .ShouldHaveSource(IntelligenceWellKnown.ActivitySourceName)
            .ShouldHaveTagKeys(IntelligenceTagKeys.Provider, IntelligenceTagKeys.Operation, IntelligenceTagKeys.Model)
            .ShouldHaveTag(IntelligenceTagKeys.Provider, IntelligenceWellKnown.SemanticKernelProviderName)
            .ShouldHaveTag(IntelligenceTagKeys.Operation, IntelligenceOperations.Complete)
            .ShouldHaveTag(IntelligenceTagKeys.Model, harness.ChatModel)
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention()
            .ShouldNotLeak("prompt-text-sentinel", "completion-text-sentinel", SemanticKernelHarness.KeySentinel, SemanticKernelHarness.HostSentinel);

        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Success);
        Tag(duration, WellKnownTagKeys.ErrorType).Should().BeNull();
        Tag(duration, IntelligenceTagKeys.Model).Should().Be(harness.ChatModel);

        Tokens(harness, IntelligenceOperations.Complete, IntelligenceWellKnown.TokenTypeInput).Should().Be(11);
        Tokens(harness, IntelligenceOperations.Complete, IntelligenceWellKnown.TokenTypeOutput).Should().Be(7);
        harness.Metrics.ShouldNotLeak("prompt-text-sentinel", "completion-text-sentinel", SemanticKernelHarness.KeySentinel);
    }

    [Fact]
    public async Task EmbedAsync_IsOneSpan_NotTwo_AndCountsOnlyInputTokens()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Embedding(promptTokens: 9, [0.1f, 0.2f, 0.3f]));

        var result = await harness.Embeddings.EmbedAsync("embed-text-sentinel");

        result.IsSuccess.Should().BeTrue();
        harness.Spans.Should().ContainSingle().Which.OperationName.Should().Be("ai embed");
        harness.Spans[0]
            .ShouldHaveTag(IntelligenceTagKeys.Model, harness.EmbeddingModel)
            .ShouldBeRecordedSuccess()
            .ShouldNotLeak("embed-text-sentinel");
        Tokens(harness, IntelligenceOperations.Embed, IntelligenceWellKnown.TokenTypeInput).Should().Be(9);
        harness.Measurements(IntelligenceWellKnown.TokensInstrumentName)
            .Should().NotContain(m => Tag(m, IntelligenceTagKeys.TokenType) == IntelligenceWellKnown.TokenTypeOutput,
                "an embedding call has no completion half and zero is never recorded");
    }

    [Fact]
    public async Task EmbedManyAsync_UsesItsOwnOperationName_AndCountsItsUsage()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Embedding(promptTokens: 12, [1f, 0f, 0f], [0f, 1f, 0f]));

        var result = await harness.Embeddings.EmbedManyAsync(["a", "b"]);

        result.IsSuccess.Should().BeTrue();
        harness.Spans.Should().ContainSingle().Which.OperationName.Should().Be("ai embed_many");
        Tokens(harness, IntelligenceOperations.EmbedMany, IntelligenceWellKnown.TokenTypeInput).Should().Be(12);
    }

    [Fact]
    public async Task ARequestForAnUnlistedModel_IsTaggedOther_OnTheSpanAndTheMetric()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Chat("x", 4, 2));

        await harness.Kernel.CompleteAsync(harness.Request(modelId: "caller-minted-model-sentinel"));

        harness.OtherModelSpans.Should().ContainSingle().Which
            .ShouldHaveTag(IntelligenceTagKeys.Model, IntelligenceWellKnown.OtherModel)
            .ShouldNotLeak("caller-minted-model-sentinel");
        harness.OtherModelMeasurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle();
        harness.OtherModelMeasurements(IntelligenceWellKnown.TokensInstrumentName).Should().NotBeEmpty();
        harness.Metrics.ShouldNotLeak("caller-minted-model-sentinel");
    }

    [Fact]
    public async Task ARequestForTheConfiguredModel_KeepsItsName()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Chat("x", 4, 2));

        await harness.Kernel.CompleteAsync(harness.Request(modelId: harness.ChatModel));

        harness.Spans.Should().ContainSingle().Which.ShouldHaveTag(IntelligenceTagKeys.Model, harness.ChatModel);
    }

    [Fact]
    public async Task Instruments_HaveTheirFinalNamesUnitsAndKinds()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Chat("x", 1, 1));

        await harness.Kernel.CompleteAsync(harness.Request());

        harness.Metrics
            .ShouldHaveInstrument(IntelligenceWellKnown.OperationDurationInstrumentName, unit: "s", kind: InstrumentKind.Histogram)
            .ShouldHaveInstrument(IntelligenceWellKnown.TokensInstrumentName, unit: "{token}", kind: InstrumentKind.Counter);
        harness.Metrics.Instruments.Where(i => i.Name.StartsWith("sharedkernel.ai.", StringComparison.Ordinal))
            .Should().OnlyContain(i => i.MeterName == "SharedKernel.AI" && i.Unit != null);
        harness.Metrics.Instruments.Should().NotContain(i => i.Name == IntelligenceWellKnown.VectorRecordsInstrumentName,
            "records are a vector concept");
        harness.Metrics.ShouldFollowMetricConvention();
    }

    [Fact]
    public void MeterAndSourceVersion_AreTheTelemetryVersionOfThePackage()
    {
        var versions = new System.Collections.Concurrent.ConcurrentBag<(string Name, string? Version)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) => versions.Add((instrument.Meter.Name, instrument.Meter.Version)),
        };
        listener.Start();

        _ = TestMeterFactory.NewTelemetry();

        var expected = TelemetryVersion.Of(typeof(SemanticKernelTelemetry).Assembly);
        versions.Should().Contain(v => v.Name == "SharedKernel.AI" && v.Version == expected);
        SemanticKernelTelemetry.Source.Name.Should().Be("SharedKernel.AI");
        SemanticKernelTelemetry.Source.Version.Should().Be(expected);
    }

    [Fact]
    public void ASecondFactoryCreate_ForTheSameName_ReturnsTheSharedMeter_SoTheTwoProvidersShareOne()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        var options = new MeterOptions("SharedKernel.AI") { Version = TelemetryVersion.Of(typeof(SemanticKernelTelemetry).Assembly) };

        factory.Create(options).Should().BeSameAs(factory.Create(options));

        var first = new SemanticKernelTelemetry(factory);
        var second = new SemanticKernelTelemetry(factory);
        first.Should().NotBeSameAs(second, "the same-named instrument created twice on the shared meter is legal");
    }

    [Fact]
    public async Task ModelHeaderAndRequestBody_DoNotReachAnyTelemetry()
    {
        using var harness = SemanticKernelHarness.Create((_, _, _) => SemanticKernelHarness.Chat("x", 1, 1));

        await harness.Kernel.CompleteAsync(harness.Request());

        var everyTag = harness.Spans.SelectMany(s => s.TagObjects.Select(t => t.Value?.ToString()))
            .Concat(harness.Metrics.Measurements.SelectMany(m => m.Tags.Select(t => t.Value?.ToString())))
            .Where(v => v is not null)
            .ToList();
        everyTag.Should().NotContain(v => v!.Contains("sentinel", StringComparison.OrdinalIgnoreCase));
    }
}
