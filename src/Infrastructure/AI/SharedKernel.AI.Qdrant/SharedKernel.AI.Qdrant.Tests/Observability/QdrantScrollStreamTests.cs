using System.Diagnostics;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.Qdrant.Tests.Support;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.Qdrant.Tests.Observability;

/// <summary>
/// <c>ScrollAsync</c> is one of the two non-<c>Result</c> surfaces: a recognised fault is an
/// <see cref="IntelligenceStreamException"/>, a defect propagates untouched, the caller's cancellation is an
/// <see cref="OperationCanceledException"/>, and the span ends however the enumeration ends without ever replacing the
/// consumer's ambient activity.
/// </summary>
public sealed class QdrantScrollStreamTests
{
    private const string HostSentinel = "host-sentinel.internal";

    private static ScrollResponse Page(ulong id, ulong? next)
    {
        var response = new ScrollResponse { Result = { new RetrievedPoint { Id = new PointId { Num = id } } } };
        if (next is { } offset)
        {
            response.NextPageOffset = new PointId { Num = offset };
        }

        return response;
    }

    private static (FaultingQdrantClient Controller, ObservabilityHarness Harness) Create(Func<string, int, object?> respond, Func<Exception> fault)
    {
        var (client, controller) = FaultingQdrantClient.Create(fault);
        controller.Respond = respond;
        return (controller, new ObservabilityHarness(client));
    }

    private static object? FirstPageThenFault(string method, int index) =>
        method == "ScrollAsync" && index == 0 ? Page(1, next: 2) : null;

    [Fact]
    public async Task MidStreamClassifiedFault_ThrowsIntelligenceStreamException_WithTheMappedError_AndTheInnerException()
    {
        var rpc = new RpcException(new Status(StatusCode.Unavailable, $"{HostSentinel}:6334 provider-text-sentinel"));
        var (_, harness) = Create(FirstPageThenFault, () => rpc);
        using var _h = harness;
        var seen = new List<string>();

        var act = async () =>
        {
            await foreach (var record in harness.Collection.ScrollAsync(null, TenantScope.Global, batchSize: 1))
            {
                seen.Add(record.Id);
            }
        };

        var thrown = (await act.Should().ThrowAsync<IntelligenceStreamException>()).Which;
        seen.Should().Equal("1");
        thrown.Should().BeAssignableTo<SharedKernelException>().And.BeAssignableTo<IHasError>();
        thrown.Error.Code.Should().Be("intelligence.unreachable");
        thrown.Error.Type.Should().Be(ErrorType.Unavailable);
        thrown.Error.Message.Should().NotContain("sentinel").And.NotContain("6334");
        thrown.Message.Should().Be(thrown.Error.Message);
        thrown.InnerException.Should().BeSameAs(rpc);

        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedFailure(thrown.Error).ShouldHaveTag(IntelligenceWellKnown.OperationTagName, IntelligenceOperations.Scroll);
        harness.Logger.Records.Should().ContainSingle(r => r.EventId.Id == 10113).Which.Exception.Should().BeSameAs(rpc);
        harness.Logger.Records.ShouldNotLeakIntoMessageOrState("sentinel", "6334");
    }

    [Fact]
    public async Task TenantScopeRejection_ThrowsTheSameExceptionType_WithoutIo()
    {
        var (client, controller) = FaultingQdrantClient.Create(() => new InvalidOperationException("must not be called"));
        using var harness = new ObservabilityHarness(client, tenanted: true);

        var act = async () =>
        {
            await foreach (var _ in harness.Collection.ScrollAsync(null, TenantScope.Global, batchSize: 1))
            {
            }
        };

        var thrown = (await act.Should().ThrowAsync<IntelligenceStreamException>()).Which;
        thrown.Error.Code.Should().Be("intelligence.tenant_scope_missing");
        controller.Calls.Should().BeEmpty();
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedFailure(thrown.Error);
    }

    [Fact]
    public async Task UnclassifiedFaultMidStream_PropagatesUntouched_AndIsRecordedAsAnException()
    {
        var defect = new InvalidOperationException("defect");
        var (_, harness) = Create(FirstPageThenFault, () => defect);
        using var _h = harness;

        var act = async () =>
        {
            await foreach (var _ in harness.Collection.ScrollAsync(null, TenantScope.Global, batchSize: 1))
            {
            }
        };

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(defect);
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedException(typeof(InvalidOperationException));
        harness.Logger.Records.Where(r => r.LogLevel >= LogLevel.Warning).Should().BeEmpty();
    }

    [Theory]
    [InlineData("between-items")]
    [InlineData("rpc-cancelled")]
    public async Task CallersCancellation_MidStream_IsOperationCanceled_NotWrapped_AndNotAnError(string shape)
    {
        using var cts = new CancellationTokenSource();
        var (controller, harness) = Create(
            (method, index) => method == "ScrollAsync" && index == 0 ? Page(1, next: 2) : null,
            () => new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client.", new OperationCanceledException())));
        using var _h = harness;

        var act = async () =>
        {
            await foreach (var record in harness.Collection.ScrollAsync(null, TenantScope.Global, batchSize: 1, cts.Token))
            {
                if (shape == "between-items")
                {
                    await cts.CancelAsync();
                }
                else
                {
                    controller.OnCall = _ => cts.Cancel();
                }
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedCancellation();
        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        duration.TryGetTag(WellKnownTagKeys.Outcome, out var outcome).Should().BeTrue();
        outcome.Should().Be(WellKnownOutcomes.Canceled);
        harness.Logger.Records.Where(r => r.LogLevel >= LogLevel.Warning).Should().BeEmpty();
    }

    [Fact]
    public async Task AnAbandonedEnumeration_StillEndsItsSpan_AndRecordsOneDuration()
    {
        var (_, harness) = Create((method, index) => method == "ScrollAsync" ? Page(1, next: 2) : null, () => new InvalidOperationException());
        using var _h = harness;

        await using (var enumerator = harness.Collection.ScrollAsync(null, TenantScope.Global, batchSize: 1).GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            harness.Spans.Should().BeEmpty("the span is still open while the enumerator is alive");
        }

        var span = harness.Spans.Should().ContainSingle().Which;
        span.IsStopped.Should().BeTrue();
        span.ShouldBeUnset();
        harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle();
        harness.Activities.StartedActivities.Where(a => a.GetTagItem(IntelligenceWellKnown.CollectionTagName) as string == harness.Name).Should().OnlyContain(a => a.IsStopped);
    }

    [Fact]
    public async Task TheConsumersActivity_IsNeverReplaced_BetweenItems_OrAfterTheWalk_AndTheSpanParentsTheProviderCall()
    {
        using var consumer = new Activity("consumer").Start();
        Activity? currentDuringProviderCall = null;
        var pages = 0;
        var (_, harness) = Create(
            (method, index) =>
            {
                if (method != "ScrollAsync")
                {
                    return null;
                }

                currentDuringProviderCall = Activity.Current;
                return pages++ == 0 ? Page(1, next: 2) : Page(2, next: null);
            },
            () => new InvalidOperationException());
        using var _h = harness;

        var betweenItems = new List<Activity?>();
        await foreach (var _ in harness.Collection.ScrollAsync(null, TenantScope.Global, batchSize: 1))
        {
            betweenItems.Add(Activity.Current);
        }

        betweenItems.Should().HaveCount(2).And.OnlyContain(a => ReferenceEquals(a, consumer));
        Activity.Current.Should().BeSameAs(consumer);
        currentDuringProviderCall.Should().NotBeNull();
        currentDuringProviderCall!.OperationName.Should().Be("ai scroll");
        harness.Spans.Should().ContainSingle().Which.Parent.Should().BeSameAs(consumer);
    }

    [Fact]
    public async Task ACompleteWalk_IsASuccessfulSpan_AndOneDuration()
    {
        var (_, harness) = Create((method, index) => method == "ScrollAsync" ? Page(7, next: null) : null, () => new InvalidOperationException());
        using var _h = harness;

        var ids = new List<string>();
        await foreach (var record in harness.Collection.ScrollAsync(null, TenantScope.Global, batchSize: 5))
        {
            ids.Add(record.Id);
        }

        ids.Should().Equal("7");
        harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedSuccess().ShouldFollowSpanConvention();
        var duration = harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName).Should().ContainSingle().Which;
        duration.TryGetTag(WellKnownTagKeys.Outcome, out var outcome).Should().BeTrue();
        outcome.Should().Be(WellKnownOutcomes.Success);
    }
}
