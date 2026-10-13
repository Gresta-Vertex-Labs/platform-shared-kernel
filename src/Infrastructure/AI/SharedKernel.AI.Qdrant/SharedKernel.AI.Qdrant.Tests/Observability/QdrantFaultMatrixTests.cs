using System.Diagnostics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Qdrant.Tests.Support;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.Qdrant.Tests.Observability;

/// <summary>
/// Drives every data-plane operation family of the Qdrant collection through a faulting client and asserts, in one place,
/// what each fault shape does to the result, the span, the metrics and the log: no provider text anywhere, one span, one
/// duration observation, one log, the caller's cancellation as <see cref="OperationCanceledException"/>, and an
/// unclassified exception propagating with its original stack.
/// </summary>
public sealed class QdrantFaultMatrixTests
{
    private const string HostSentinel = "host-sentinel.internal";
    private const string RecordSentinel = "record-id-sentinel";
    private const string KeySentinel = "api-key-sentinel";
    private const string FilterSentinel = "sentinel-filter-value";
    private const string TextSentinel = "provider-text-sentinel";

    private static readonly string[] AllSentinels = [HostSentinel, RecordSentinel, KeySentinel, FilterSentinel, TextSentinel, "6334"];

    public static TheoryData<string> OperationNames => [.. ObservabilityHarness.Operations.Keys];

    private static RpcException Rpc(StatusCode status) =>
        new(new Status(status, $"{HostSentinel}:6334 {RecordSentinel} {KeySentinel} {FilterSentinel} {TextSentinel}", new InvalidOperationException(TextSentinel)));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception ThrownFromUniqueFrameForStackProof()
    {
        try
        {
            throw new InvalidOperationException($"{TextSentinel} {HostSentinel}");
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    // ---- classified provider faults ---------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task ClassifiedOutage_IsAFailedResult_WithOneFailedSpan_OneDuration_AndOneWarningWithTheException(string operation)
    {
        var (client, controller) = FaultingQdrantClient.Create(() => Rpc(StatusCode.Unavailable));
        using var harness = new ObservabilityHarness(client);

        var error = await ObservabilityHarness.Operations[operation](harness, TenantScope.Global, CancellationToken.None);

        error.Should().NotBeNull();
        error!.Code.Should().Be("intelligence.unavailable");
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Message.Should().NotContainAny(AllSentinels);
        error.Details.Should().BeNullOrEmpty();
        controller.Calls.Should().NotBeEmpty("the guard-satisfied call must reach the client for the fault to be a provider fault");

        var span = harness.Spans.Should().ContainSingle().Which;
        span.OperationName.Should().Be($"ai {operation}");
        span.ShouldHaveKind(ActivityKind.Client)
            .ShouldHaveSource(IntelligenceWellKnown.ActivitySourceName)
            .ShouldBeRecordedFailure(error)
            .ShouldHaveTag(IntelligenceTagKeys.Provider, IntelligenceWellKnown.QdrantProviderName)
            .ShouldHaveTag(IntelligenceTagKeys.Operation, operation)
            .ShouldHaveTag(IntelligenceTagKeys.Collection, harness.Name)
            .ShouldFollowSpanConvention()
            .ShouldNotLeak(AllSentinels);

        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        duration.TryGetTag(WellKnownTagKeys.Outcome, out var outcome).Should().BeTrue();
        outcome.Should().Be(WellKnownOutcomes.Failure);
        duration.TryGetTag(WellKnownTagKeys.ErrorType, out var errorType).Should().BeTrue();
        errorType.Should().Be("Unavailable");
        duration.TryGetTag(IntelligenceTagKeys.Operation, out var op).Should().BeTrue();
        op.Should().Be(operation);

        var fault = harness.Logger.Records.Where(r => r.EventId.Id == 10113).Should().ContainSingle().Which;
        fault.LogLevel.Should().Be(LogLevel.Warning);
        fault.Exception.Should().BeOfType<RpcException>();
        harness.Logger.Records.ShouldNotLeakIntoMessageOrState(RecordSentinel, KeySentinel, FilterSentinel, "6334");
        fault.TryGetProperty("ErrorCode", out var code).Should().BeTrue();
        code.Should().Be("intelligence.unavailable");
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task EveryGrpcStatus_NeverLeaksProviderText_OnAnyOperation(string operation)
    {
        foreach (var status in Enum.GetValues<StatusCode>().Where(s => s != StatusCode.OK))
        {
            var (client, _) = FaultingQdrantClient.Create(() => Rpc(status));
            using var harness = new ObservabilityHarness(client);

            var error = await ObservabilityHarness.Operations[operation](harness, TenantScope.Global, CancellationToken.None);

            error.Should().NotBeNull($"status {status} on '{operation}' is a recognised provider fault (or the caller's own cancellation, which is not requested here)");
            error!.Message.Should().NotContainAny(AllSentinels);
            harness.Logger.Records.ShouldNotLeakIntoMessageOrState(RecordSentinel, KeySentinel, FilterSentinel, "6334");
            harness.Spans.ShouldNotLeak(AllSentinels);
        }
    }

    [Theory]
    [InlineData(StatusCode.DeadlineExceeded, "intelligence.timeout", ErrorType.Timeout)]
    [InlineData(StatusCode.ResourceExhausted, "intelligence.rate_limited", ErrorType.Unavailable)]
    [InlineData(StatusCode.Internal, "intelligence.engine_fault", ErrorType.Unexpected)]
    public async Task ErrorTypeFollowsTheCondition_OnBothAReadAndAWrite(StatusCode status, string code, ErrorType type)
    {
        var (client, _) = FaultingQdrantClient.Create(() => Rpc(status));
        using var harness = new ObservabilityHarness(client);

        var read = await ObservabilityHarness.Operations[IntelligenceOperations.Count](harness, TenantScope.Global, CancellationToken.None);
        var write = await ObservabilityHarness.Operations[IntelligenceOperations.Upsert](harness, TenantScope.Global, CancellationToken.None);

        foreach (var error in new[] { read, write })
        {
            error!.Code.Should().Be(code);
            error.Type.Should().Be(type);
        }
    }

    [Fact]
    public async Task QdrantException_IsAClassifiedEngineFault_WithoutItsMessage()
    {
        var (client, _) = FaultingQdrantClient.Create(() => new QdrantException($"{TextSentinel} {HostSentinel}"));
        using var harness = new ObservabilityHarness(client);

        var error = await ObservabilityHarness.Operations[IntelligenceOperations.Query](harness, TenantScope.Global, CancellationToken.None);

        error!.Code.Should().Be("intelligence.engine_fault");
        error.Message.Should().NotContainAny(AllSentinels);
        harness.Logger.Records.ShouldHaveLoggedWithException(new EventId(10113), typeof(QdrantException));
    }

    // ---- defects propagate -------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task UnclassifiedException_Propagates_AsTheSameInstance_WithItsOriginalStack_AndIsRecordedAsAnException(string operation)
    {
        var defect = ThrownFromUniqueFrameForStackProof();
        var (client, _) = FaultingQdrantClient.Create(() => defect);
        using var harness = new ObservabilityHarness(client);

        var act = () => ObservabilityHarness.Operations[operation](harness, TenantScope.Global, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Should().BeSameAs(defect);
        thrown.StackTrace.Should().Contain(nameof(ThrownFromUniqueFrameForStackProof));

        var span = harness.Spans.Should().ContainSingle().Which;
        span.ShouldBeRecordedException(typeof(InvalidOperationException));

        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        duration.TryGetTag(WellKnownTagKeys.Outcome, out var outcome).Should().BeTrue();
        outcome.Should().Be(WellKnownOutcomes.Exception);
        duration.TryGetTag(WellKnownTagKeys.ErrorType, out var errorType).Should().BeTrue();
        errorType.Should().Be(typeof(InvalidOperationException).FullName);

        harness.Logger.Records.Where(r => r.LogLevel >= LogLevel.Warning).Should().BeEmpty("an exception that propagates is the caller's to log, once");
    }

    // ---- cancellation ------------------------------------------------------------------------------------

    public static TheoryData<string, string> CancellationShapes
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var operation in ObservabilityHarness.Operations.Keys)
            {
                data.Add(operation, "rpc-cancelled");
                data.Add(operation, "operation-canceled");
                data.Add(operation, "wrapped");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CancellationShapes))]
    public async Task CallersCancellation_AlwaysSurfacesAsOperationCanceled_NeverAResult_AndIsNotAnError(string operation, string shape)
    {
        using var cts = new CancellationTokenSource();
        Exception Fault() => shape switch
        {
            "rpc-cancelled" => new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client.", new OperationCanceledException())),
            "operation-canceled" => new OperationCanceledException(cts.Token),
            _ => new InvalidOperationException("wrapper", new TaskCanceledException()),
        };

        var (client, controller) = FaultingQdrantClient.Create(Fault);
        controller.OnCall = _ => cts.Cancel();
        using var harness = new ObservabilityHarness(client);

        var act = () => ObservabilityHarness.Operations[operation](harness, TenantScope.Global, cts.Token);

        var thrown = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
        if (shape != "operation-canceled")
        {
            thrown.CancellationToken.Should().Be(cts.Token);
        }

        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedCancellation();
        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        duration.TryGetTag(WellKnownTagKeys.Outcome, out var outcome).Should().BeTrue();
        outcome.Should().Be(WellKnownOutcomes.Canceled);
        duration.TryGetTag(WellKnownTagKeys.ErrorType, out _).Should().BeFalse();
        harness.Logger.Records.Where(r => r.LogLevel >= LogLevel.Warning).Should().BeEmpty("a cancellation is not an error");
    }

    [Fact]
    public async Task AnOutageWhileTheTokenIsCancelled_IsStillAnOutage()
    {
        using var cts = new CancellationTokenSource();
        var (client, controller) = FaultingQdrantClient.Create(() => Rpc(StatusCode.Unavailable));
        controller.OnCall = _ => cts.Cancel();
        using var harness = new ObservabilityHarness(client);

        var error = await ObservabilityHarness.Operations[IntelligenceOperations.Count](harness, TenantScope.Global, cts.Token);

        error!.Code.Should().Be("intelligence.unavailable");
    }

    // ---- pre-I/O rejection -------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task TenantScopeMissing_IsRejectedBeforeAnyIo_YetStillYieldsASpanAndADuration(string operation)
    {
        var (client, controller) = FaultingQdrantClient.Create(() => Rpc(StatusCode.Unavailable));
        using var harness = new ObservabilityHarness(client, tenanted: true);

        var error = await ObservabilityHarness.Operations[operation](harness, TenantScope.Global, CancellationToken.None);

        error.Should().NotBeNull();
        error!.Code.Should().Be("intelligence.tenant_scope_missing");
        error.Type.Should().Be(ErrorType.Unauthorized);
        controller.Calls.Should().BeEmpty("a rejection performs no I/O");

        var span = harness.Spans.Should().ContainSingle().Which;
        span.ShouldBeRecordedFailure(error);
        span.GetTagItem(IntelligenceTagKeys.Operation).Should().Be(operation);
        harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle();
        harness.Logger.Records.Where(r => r.EventId.Id == 10113).Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidRecordIdRejection_LogsTypeAndCodeOnly_NeverTheRecordId()
    {
        var (client, controller) = FaultingQdrantClient.Create(() => Rpc(StatusCode.Unavailable));
        using var harness = new ObservabilityHarness(client);

        var result = await harness.Collection.DeleteAsync(RecordSentinel, TenantScope.Global);

        result.Error.Code.Should().Be("intelligence.invalid_record_id");
        controller.Calls.Should().BeEmpty();
        var rejected = harness.Logger.Records.Should().ContainSingle(r => r.EventId.Id == 10106).Which;
        rejected.TryGetProperty("ErrorType", out var type).Should().BeTrue();
        type.Should().Be("Validation");
        rejected.TryGetProperty("ErrorCode", out var code).Should().BeTrue();
        code.Should().Be("intelligence.invalid_record_id");
        rejected.TryGetProperty("Reason", out _).Should().BeFalse();
        harness.Logger.Records.ShouldNotLeakIntoMessageOrState(RecordSentinel);
        harness.Spans.ShouldNotLeak(RecordSentinel);
    }

    [Fact]
    public async Task ModelAndDimensionMismatches_AreRejectedBeforeAnyIo()
    {
        var (client, controller) = FaultingQdrantClient.Create(() => Rpc(StatusCode.Unavailable));
        using var harness = new ObservabilityHarness(client);

        var wrongModel = await harness.Collection.UpsertAsync(harness.Record("1") with { ModelId = "other-model" }, TenantScope.Global);
        var wrongDimension = await harness.Collection.UpsertAsync(harness.Record("1") with { Vector = new float[] { 1f } }, TenantScope.Global);

        wrongModel.Error.Code.Should().Be("intelligence.embedding_model_mismatch");
        wrongDimension.Error.Code.Should().Be("intelligence.dimension_mismatch");
        controller.Calls.Should().BeEmpty();
        harness.Spans.Should().HaveCount(2).And.OnlyContain(s => s.Status == ActivityStatusCode.Error);
    }

    // ---- logs --------------------------------------------------------------------------------------------

    [Fact]
    public void LogTemplates_CarryNoReasonHostOrPort_AndEveryIdIsUniqueAndInsideTheQdrantSubBlock()
    {
        var methods = typeof(SharedKernel.AI.Qdrant.Diagnostics.QdrantLog).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var attributes = methods
            .Select(m => m.GetCustomAttributes(typeof(LoggerMessageAttribute), inherit: false).Cast<LoggerMessageAttribute>().Single())
            .ToList();

        attributes.Should().HaveCount(15);
        attributes.Select(a => a.EventId).Should().OnlyHaveUniqueItems();
        attributes.Select(a => a.EventId).Should().OnlyContain(id => SharedKernel.Primitives.Logging.LoggingEventIdRanges.IsInSubBlock(id, SharedKernel.Primitives.Logging.LoggingEventIdRanges.Intelligence, 1));
        attributes.Select(a => a.Message).Should().NotContain(m => m!.Contains("{Reason}") || m.Contains("{Host}") || m.Contains("{Port}"));
        attributes.Single(a => a.EventId == 10113).Level.Should().Be(LogLevel.Warning);
    }
}
