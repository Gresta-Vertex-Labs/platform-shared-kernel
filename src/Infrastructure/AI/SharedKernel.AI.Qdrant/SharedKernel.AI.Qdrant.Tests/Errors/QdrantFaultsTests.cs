using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using SharedKernel.AI.Qdrant.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Qdrant.Tests.Errors;

/// <summary>The classify-or-rethrow table of <see cref="QdrantFaults"/>, with the sentinel proof that no provider text enters an error.</summary>
public sealed class QdrantFaultsTests
{
    private const string Sentinel = "host-sentinel.internal:6334 filter-value-sentinel record-id-sentinel key-sentinel";

    private static Error? Classify(Exception exception, CancellationToken cancellationToken, out bool matched)
    {
        matched = QdrantFaults.TryClassify(exception, cancellationToken, "qdrant", "query", "orders", out var error);
        return error;
    }

    public static TheoryData<StatusCode, string, ErrorType> StatusTable => new()
    {
        { StatusCode.NotFound, "intelligence.collection_not_found", ErrorType.NotFound },
        { StatusCode.AlreadyExists, "intelligence.collection_already_exists", ErrorType.Conflict },
        { StatusCode.InvalidArgument, "intelligence.invalid_query", ErrorType.Validation },
        { StatusCode.FailedPrecondition, "intelligence.invalid_query", ErrorType.Validation },
        { StatusCode.PermissionDenied, "intelligence.engine_unauthorized", ErrorType.Unexpected },
        { StatusCode.Unauthenticated, "intelligence.engine_unauthorized", ErrorType.Unexpected },
        { StatusCode.ResourceExhausted, "intelligence.rate_limited", ErrorType.Unavailable },
        { StatusCode.Unavailable, "intelligence.unavailable", ErrorType.Unavailable },
        { StatusCode.DeadlineExceeded, "intelligence.timeout", ErrorType.Timeout },
        { StatusCode.Internal, "intelligence.engine_fault", ErrorType.Unexpected },
        { StatusCode.Unknown, "intelligence.engine_fault", ErrorType.Unexpected },
        { StatusCode.DataLoss, "intelligence.engine_fault", ErrorType.Unexpected },
        { StatusCode.Unimplemented, "intelligence.engine_fault", ErrorType.Unexpected },
        { StatusCode.Aborted, "intelligence.engine_fault", ErrorType.Unexpected },
        { StatusCode.OutOfRange, "intelligence.engine_fault", ErrorType.Unexpected },
        { StatusCode.Cancelled, "intelligence.engine_fault", ErrorType.Unexpected },
    };

    [Theory]
    [MemberData(nameof(StatusTable))]
    public void EveryGrpcStatus_IsClassified_WithTheCodeAndTypeOfItsCondition_AndNoProviderText(
        StatusCode status, string code, ErrorType type)
    {
        var exception = new RpcException(new Status(status, Sentinel, new InvalidOperationException(Sentinel)));

        var error = Classify(exception, CancellationToken.None, out var matched);

        matched.Should().BeTrue();
        error.Should().NotBeNull();
        error!.Code.Should().Be(code);
        error.Type.Should().Be(type);
        error.Message.Should().NotContain("sentinel").And.NotContain("6334");
        error.Details.Should().BeNullOrEmpty();
    }

    [Fact]
    public void EveryDefinedGrpcStatusExceptOk_IsClassified()
    {
        foreach (var status in Enum.GetValues<StatusCode>().Where(s => s != StatusCode.OK))
        {
            var error = Classify(new RpcException(new Status(status, "x")), CancellationToken.None, out var matched);

            matched.Should().BeTrue($"gRPC status {status} is a closed enum value and must be classified");
            error.Should().NotBeNull();
            error!.Type.Should().NotBe(ErrorType.None);
        }
    }

    [Fact]
    public void EngineFaultFromAGrpcStatus_NamesOnlyTheStatus()
    {
        var error = Classify(new RpcException(new Status(StatusCode.Internal, Sentinel)), CancellationToken.None, out _);

        error!.Message.Should().Contain("Internal").And.NotContain(Sentinel);
    }

    [Fact]
    public void QdrantException_IsAClassifiedEngineFault_WithoutItsMessage()
    {
        var error = Classify(new QdrantException(Sentinel), CancellationToken.None, out var matched);

        matched.Should().BeTrue();
        error!.Code.Should().Be("intelligence.engine_fault");
        error.Message.Should().NotContain("sentinel");
    }

    public static TheoryData<Exception> Defects => new()
    {
        new InvalidOperationException(Sentinel),
        new NullReferenceException(),
        new InvalidCastException(),
        new ArgumentException(Sentinel),
        new System.Runtime.CompilerServices.SwitchExpressionException(3),
        new KeyNotFoundException(),
    };

    [Theory]
    [MemberData(nameof(Defects))]
    public void AnythingElse_IsNotClassified(Exception defect)
    {
        var error = Classify(defect, CancellationToken.None, out var matched);

        matched.Should().BeFalse();
        error.Should().BeNull();
    }

    [Fact]
    public void CallersCancellation_AsRpcCancelled_MatchesWithNoError_SoItSurfacesAsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var exception = new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client.", new OperationCanceledException()));

        var error = Classify(exception, cts.Token, out var matched);

        matched.Should().BeTrue();
        error.Should().BeNull();
        var logger = NullLogger.Instance;
        var act = () => QdrantFaults.Surface(exception, error, cts.Token, logger, "query", "orders");
        var thrown = act.Should().Throw<OperationCanceledException>().Which;
        thrown.CancellationToken.Should().Be(cts.Token);
        thrown.InnerException.Should().BeSameAs(exception);
    }

    [Fact]
    public void CallersCancellation_WrappedInAnotherException_MatchesWithNoError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var wrapped = new InvalidOperationException("wrapper", new TaskCanceledException());

        Classify(wrapped, cts.Token, out var matched).Should().BeNull();

        matched.Should().BeTrue();
    }

    [Fact]
    public void ACancelledToken_DoesNotHideARealOutage()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var error = Classify(new RpcException(new Status(StatusCode.Unavailable, "down")), cts.Token, out var matched);

        matched.Should().BeTrue();
        error!.Code.Should().Be("intelligence.unavailable");
    }

    [Fact]
    public void APlainOperationCanceledException_IsNeverCaught_SoItPropagatesUntouched()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Classify(new OperationCanceledException(cts.Token), cts.Token, out var matched);

        matched.Should().BeFalse();
        QdrantFaults.IsCancellationOf(new OperationCanceledException(cts.Token), cts.Token).Should().BeFalse();
    }

    [Fact]
    public void RpcCancelled_WithALiveToken_IsAnEngineFault_NotACancellation()
    {
        var error = Classify(new RpcException(new Status(StatusCode.Cancelled, "x")), CancellationToken.None, out var matched);

        matched.Should().BeTrue();
        error!.Code.Should().Be("intelligence.engine_fault");
        QdrantFaults.IsCancellationOf(new RpcException(new Status(StatusCode.Cancelled, "x")), CancellationToken.None).Should().BeFalse();
    }
}
