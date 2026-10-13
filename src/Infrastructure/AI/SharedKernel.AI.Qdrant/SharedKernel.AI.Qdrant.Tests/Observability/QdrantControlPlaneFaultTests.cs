using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Errors;
using SharedKernel.AI.Qdrant.Provisioning;
using SharedKernel.AI.Qdrant.Quantization;
using SharedKernel.AI.Qdrant.Sparse;
using SharedKernel.AI.Qdrant.Tests.Support;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.AI.Qdrant.Tests.Observability;

/// <summary>
/// The provisioner, probe, hybrid accessor and quantization accessor against a plainly unreachable address (the real
/// <see cref="QdrantClient"/> is not mockable) and against a faulting <see cref="IQdrantClient"/>: no host or provider
/// text in any error, a classified failure logged once with its exception, the caller's cancellation as
/// <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class QdrantControlPlaneFaultTests
{
    private const string Host = "127.0.0.1";
    private const string TextSentinel = "provider-text-sentinel";

    private static QdrantClient DeadClient() => new(Host, 1, grpcTimeout: TimeSpan.FromSeconds(5));

    private static VectorCollectionDefinition Definition(string name) =>
        new VectorCollectionDefinitionBuilder(name).EmbeddingModel("model-a", 2).DistanceMetric(VectorDistanceMetric.Cosine).Build().Value;

    private static CancellationToken Cancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    public static TheoryData<string> ProvisionerOperations => ["ensure_collection", "collection_exists", "delete_collection", "cutover"];

    private static Task<Error?> RunAsync(string operation, QdrantCollectionProvisioner provisioner, CancellationToken ct) => operation switch
    {
        "ensure_collection" => Errored(provisioner.EnsureCollectionAsync(Definition("c-1"), ct)),
        "collection_exists" => Errored(provisioner.CollectionExistsAsync("c-1", ct)),
        "delete_collection" => Errored(provisioner.DeleteCollectionAsync("c-1", ct)),
        _ => Errored(provisioner.CutoverAsync(new VectorCollectionCutoverRequest { StagingCollectionName = "c-staging", LiveCollectionName = "c-live" }, ct)),
    };

    private static async Task<Error?> Errored(Task<Result> task)
    {
        var result = await task;
        return result.IsFailure ? result.Error : null;
    }

    private static async Task<Error?> Errored(Task<Result<bool>> task)
    {
        var result = await task;
        return result.IsFailure ? result.Error : null;
    }

    [Theory]
    [MemberData(nameof(ProvisionerOperations))]
    public async Task Provisioner_Outage_IsUnavailable_NamesNoHost_AndLogsOnceWithTheException(string operation)
    {
        using var client = DeadClient();
        var logger = new InMemoryLogger<QdrantCollectionProvisioner>();
        var provisioner = new QdrantCollectionProvisioner(client, TestMeterFactory.NewTelemetry(), logger);

        var error = await RunAsync(operation, provisioner, CancellationToken.None);

        error.Should().NotBeNull();
        error!.Code.Should().Be("intelligence.unavailable");
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Message.Should().NotContain(Host);
        var record = logger.Records.Should().ContainSingle(r => r.EventId.Id == 10113).Which;
        record.LogLevel.Should().Be(LogLevel.Warning);
        record.Exception.Should().BeOfType<RpcException>();
        logger.Records.ShouldNotLeakIntoMessageOrState(Host);
    }

    [Theory]
    [MemberData(nameof(ProvisionerOperations))]
    public async Task Provisioner_CallersCancellation_IsOperationCanceled_NotAResult_AndNotLogged(string operation)
    {
        using var client = DeadClient();
        var logger = new InMemoryLogger<QdrantCollectionProvisioner>();
        var provisioner = new QdrantCollectionProvisioner(client, TestMeterFactory.NewTelemetry(), logger);
        var token = Cancelled();

        var act = () => RunAsync(operation, provisioner, token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(token);
        logger.Records.Where(r => r.LogLevel >= LogLevel.Warning).Should().BeEmpty();
    }

    [Fact]
    public async Task Probe_WhenTheEngineIsDown_DegradesTheReport_AndTheDegradedLogCarriesTheFirstException()
    {
        using var client = DeadClient();
        var logger = new InMemoryLogger<QdrantCollectionProvisioner>();
        var provisioner = new QdrantCollectionProvisioner(client, TestMeterFactory.NewTelemetry(), logger);

        var result = await provisioner.ProbeAsync("c-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Reachable.Should().BeFalse();
        var degraded = logger.Records.Should().ContainSingle(r => r.EventId.Id == 10111).Which;
        degraded.LogLevel.Should().Be(LogLevel.Warning);
        degraded.Exception.Should().BeOfType<RpcException>();
        logger.Records.ShouldNotLeakIntoMessageOrState(Host);

        var report = await new VectorCollectionReadinessProbe("qdrant", "c-1", provisioner.ProbeAsync).ProbeAsync();
        (report.Description ?? string.Empty).Should().NotContain(Host);
        report.Data.Values.Select(v => v.ToString()).Should().NotContain(v => v!.Contains(Host));
    }

    [Fact]
    public async Task Probe_CallersCancellation_Propagates_AsOperationCanceled_InsteadOfDegrading()
    {
        using var client = DeadClient();
        var provisioner = new QdrantCollectionProvisioner(client, TestMeterFactory.NewTelemetry(), new InMemoryLogger<QdrantCollectionProvisioner>());
        var token = Cancelled();

        var act = () => provisioner.ProbeAsync("c-1", token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(token);
    }

    [Fact]
    public async Task FailedProbeReport_CarriesTheKernelAuthoredMessage_NeverTheEngineText()
    {
        var rpc = new RpcException(new Status(StatusCode.Unavailable, $"{TextSentinel} {Host}:6334"));
        QdrantFaults.TryClassify(rpc, CancellationToken.None, "qdrant", "probe", "c-1", out var error).Should().BeTrue();
        var probe = new VectorCollectionReadinessProbe("qdrant", "c-1", (_, _) => Task.FromResult(Result<VectorCollectionHealth>.Failure(error!)));

        var report = await probe.ProbeAsync();

        report.IsHealthy.Should().BeFalse();
        report.Description.Should().NotContain(TextSentinel).And.NotContain(Host);
        report.Data.Values.Select(v => v.ToString()).Should().NotContain(v => v!.Contains(TextSentinel) || v.Contains(Host));
    }

    [Fact]
    public async Task HybridQuery_Fault_IsClassifiedLoggedOnce_AndHasItsOwnSpan()
    {
        var (client, controller) = FaultingQdrantClient.Create(() => new RpcException(new Status(StatusCode.Unavailable, TextSentinel)));
        using var harness = new ObservabilityHarness(client);
        var logger = new InMemoryLogger<QdrantHybridQueryAccessor<TestVectorRecord>>();
        var accessor = new QdrantHybridQueryAccessor<TestVectorRecord>(client, harness.Definition, new FakeClock(), harness.Telemetry, logger);
        var query = new VectorQuery { Vector = new float[] { 0.1f, 0.2f }, ModelId = ObservabilityHarness.ModelId, Limit = 3 };

        var result = await accessor.QueryHybridAsync(query, "sparse", [new QdrantSparseVectorEntry { Index = 1, Value = 0.5f }], TenantScope.Global);

        result.Error.Code.Should().Be("intelligence.unavailable");
        result.Error.Message.Should().NotContain(TextSentinel);
        controller.Calls.Should().NotBeEmpty();
        logger.Records.Should().ContainSingle(r => r.EventId.Id == 10113).Which.Exception.Should().BeOfType<RpcException>();
        harness.Spans.Should().ContainSingle().Which.OperationName.Should().Be("ai query_hybrid");
    }

    [Fact]
    public async Task HybridQuery_ModelMismatch_IsRejectedBeforeAnyIo()
    {
        var (client, controller) = FaultingQdrantClient.Create(() => new InvalidOperationException());
        using var harness = new ObservabilityHarness(client);
        var accessor = new QdrantHybridQueryAccessor<TestVectorRecord>(
            client, harness.Definition, new FakeClock(), harness.Telemetry, new InMemoryLogger<QdrantHybridQueryAccessor<TestVectorRecord>>());
        var query = new VectorQuery { Vector = new float[] { 0.1f, 0.2f }, ModelId = "other-model", Limit = 3 };

        var result = await accessor.QueryHybridAsync(query, "sparse", [new QdrantSparseVectorEntry { Index = 1, Value = 0.5f }], TenantScope.Global);

        result.Error.Code.Should().Be("intelligence.embedding_model_mismatch");
        controller.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task QuantizationProfile_Fault_IsClassified_AndCancellationPropagates()
    {
        var (client, _) = FaultingQdrantClient.Create(() => new RpcException(new Status(StatusCode.Unavailable, $"{TextSentinel} {Host}")));
        var logger = new InMemoryLogger<QdrantQuantizationProfileAccessor>();
        var accessor = new QdrantQuantizationProfileAccessor(client, TestMeterFactory.NewTelemetry(), logger);

        var result = await accessor.GetQuantizationProfileAsync("c-1");

        result.Error.Code.Should().Be("intelligence.unavailable");
        result.Error.Message.Should().NotContain(TextSentinel).And.NotContain(Host);
        logger.Records.Should().ContainSingle(r => r.EventId.Id == 10113).Which.Exception.Should().BeOfType<RpcException>();

        var (cancelClient, _) = FaultingQdrantClient.Create(() => new RpcException(new Status(StatusCode.Cancelled, "x", new OperationCanceledException())));
        var token = Cancelled();
        var act = () => new QdrantQuantizationProfileAccessor(cancelClient, TestMeterFactory.NewTelemetry(), logger).GetQuantizationProfileAsync("c-1", token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task QuantizationProfile_Defect_PropagatesUnclassified()
    {
        var defect = new InvalidOperationException("defect");
        var (client, _) = FaultingQdrantClient.Create(() => defect);
        var accessor = new QdrantQuantizationProfileAccessor(client, TestMeterFactory.NewTelemetry(), new InMemoryLogger<QdrantQuantizationProfileAccessor>());

        var act = () => accessor.GetQuantizationProfileAsync("c-1");

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(defect);
    }
}
