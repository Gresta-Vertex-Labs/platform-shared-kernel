using System.Diagnostics.Metrics;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Qdrant.Diagnostics;
using SharedKernel.AI.Qdrant.Tests.Support;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.Qdrant.Tests.Observability;

/// <summary>The instruments of <see cref="QdrantTelemetry"/>: names, units, kinds, tag sets, meter identity and sharing.</summary>
public sealed class QdrantMetricsTests
{
    private static string? Tag(RecordedMeasurement measurement, string key) =>
        measurement.TryGetTag(key, out var value) ? (string?)value : null;

    [Fact]
    public async Task Instruments_HaveTheirFinalNamesUnitsAndKinds_UnderTheSharedMeterName()
    {
        var (client, _) = FaultingQdrantClient.Create(() => new RpcException(new Status(StatusCode.Unavailable, "x")));
        using var harness = new ObservabilityHarness(client);

        await harness.Collection.CountAsync(null, TenantScope.Global);
        await harness.Collection.UpsertAsync(harness.Record("1"), TenantScope.Global);

        harness.Metrics
            .ShouldHaveInstrument(IntelligenceWellKnown.OperationDurationInstrumentName, unit: "s", kind: InstrumentKind.Histogram)
            .ShouldHaveInstrument(IntelligenceWellKnown.VectorRecordsInstrumentName, unit: "{record}", kind: InstrumentKind.Counter);
        harness.Metrics.Instruments.Where(i => i.Name.StartsWith("sharedkernel.ai.", StringComparison.Ordinal))
            .Should().OnlyContain(i => i.MeterName == "SharedKernel.AI" && i.Unit != null);
        harness.Metrics.Instruments.Should().NotContain(i => i.Name == IntelligenceWellKnown.TokensInstrumentName,
            "a vector database bills no tokens");
    }

    [Fact]
    public async Task DurationTags_AreBoundedConfigurationOnly_AndErrorTypeIsAbsentOnSuccess()
    {
        var (client, _) = FaultingQdrantClient.Create(() => new RpcException(new Status(StatusCode.Unavailable, "x")));
        using var harness = new ObservabilityHarness(client, tenanted: true);

        await harness.Collection.CountAsync(null, TenantScope.Global);

        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        duration.Tags.Select(t => t.Key).Should().BeEquivalentTo(
        [
            IntelligenceTagKeys.Provider,
            IntelligenceTagKeys.Operation,
            IntelligenceTagKeys.Collection,
            WellKnownTagKeys.Outcome,
            WellKnownTagKeys.ErrorType,
        ]);
        Tag(duration, WellKnownTagKeys.ErrorType).Should().Be("Unauthorized");
    }

    [Fact]
    public void MeterVersion_IsTheTelemetryVersionOfThePackage_AndTheNameIsTheSharedOne()
    {
        var versions = new System.Collections.Concurrent.ConcurrentBag<(string Name, string? Version)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) => versions.Add((instrument.Meter.Name, instrument.Meter.Version)),
        };
        listener.Start();

        _ = TestMeterFactory.NewTelemetry();

        versions.Should().Contain(v => v.Name == "SharedKernel.AI" && v.Version == TelemetryVersion.Of(typeof(QdrantTelemetry).Assembly));
        QdrantTelemetry.Source.Name.Should().Be("SharedKernel.AI");
        QdrantTelemetry.Source.Version.Should().Be(TelemetryVersion.Of(typeof(QdrantTelemetry).Assembly));
    }

    [Fact]
    public void ASecondFactoryCreate_ForTheSameName_ReturnsTheSharedMeter_AndTheSameInstrumentNameTwiceIsLegal()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        var options = new MeterOptions("SharedKernel.AI") { Version = TelemetryVersion.Of(typeof(QdrantTelemetry).Assembly) };

        var first = factory.Create(options);
        var second = factory.Create(options);

        second.Should().BeSameAs(first);

        using var recorder = MetricRecorder.StartRecording("SharedKernel.AI");
        var a = new QdrantTelemetry(factory);
        var b = new QdrantTelemetry(factory);
        using (var scope = a.Begin(IntelligenceOperations.Count, "shared-meter-check"))
        {
            scope.Succeed();
        }

        using (var scope = b.Begin(IntelligenceOperations.Count, "shared-meter-check"))
        {
            scope.Succeed();
        }

        recorder.Measurements
            .Where(m => Tag(m, IntelligenceTagKeys.Collection) == "shared-meter-check")
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task APartialBulkFailure_CountsEachRecordByItsOwnOutcome()
    {
        var (client, controller) = FaultingQdrantClient.Create(() => new InvalidOperationException("not expected"));
        controller.Respond = (method, _) => method == "UpdateBatchAsync"
            ? (IReadOnlyList<UpdateResult>)new List<UpdateResult>
            {
                new() { Status = UpdateStatus.Completed, OperationId = 1 },
                new() { Status = UpdateStatus.ClockRejected, OperationId = 2 },
                new() { Status = UpdateStatus.Acknowledged, OperationId = 3 },
            }
            : null;
        using var harness = new ObservabilityHarness(client);

        var result = await harness.Collection.UpsertManyAsync([harness.Record("1"), harness.Record("2"), harness.Record("3")], TenantScope.Global);

        result.IsSuccess.Should().BeTrue();
        result.Value.Failures.Should().ContainSingle();
        result.Value.Failures[0].Error.Message.Should().NotContain("sentinel");

        long Sum(string outcome) => harness.Measurements(IntelligenceWellKnown.VectorRecordsInstrumentName)
            .Where(m => Tag(m, WellKnownTagKeys.Outcome) == outcome).Sum(m => (long)m.Value);
        Sum(WellKnownOutcomes.Success).Should().Be(2);
        Sum(WellKnownOutcomes.Failure).Should().Be(1);
    }

    [Fact]
    public void Telemetry_RejectsANullMeterFactory()
    {
        var act = () => new QdrantTelemetry(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
