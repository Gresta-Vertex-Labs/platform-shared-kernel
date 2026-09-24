using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Extensions;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Presentation.Grpc.Options;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using Xunit;
using TestServerCallContext = SharedKernel.Testing.Grpc.TestServerCallContext;

namespace SharedKernel.Presentation.Grpc.Tests.Interceptors;

/// <summary>
/// The exception interceptor in isolation, driven through all four call shapes with a hand-built
/// <see cref="ServerCallContext"/>: cancellation (P-562 R33), rebuilt <see cref="RpcException"/>s (R30), failed
/// results ended with <c>SharedKernel.Core</c>'s extensions (R32), and a context without a request.
/// </summary>
public sealed class GrpcExceptionInterceptorTests
{
    private const string Domain = "orders.example.com";

    private readonly InMemoryLogger<GrpcExceptionInterceptor> _logger = new();

    public static TheoryData<Exception> ExceptionsAfterCancellation => new()
    {
        new OperationCanceledException(),
        new IOException("The request stream was aborted."),
        new InvalidOperationException("Can't write the message because the request is complete."),
        new ObjectDisposedException("HttpResponseStream"),
        new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client.")),
        new RpcException(new Status(StatusCode.Unavailable, "Error connecting to subchannel.")),
        new DomainException(Error.Unavailable("search.unreachable", "Cluster 10.0.0.5 is down.")),
    };

    [Theory]
    [MemberData(nameof(ExceptionsAfterCancellation))]
    public async Task AnyException_OnceTheCallIsCancelled_EndsAsCancelled_AndIsLoggedAtDebugOnly(Exception thrown)
    {
        foreach (var shape in Enum.GetValues<CallShape>())
        {
            var logger = new InMemoryLogger<GrpcExceptionInterceptor>();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            var context = TestServerCallContext.Create(cancellationToken: cancellation.Token);

            var exception = await ThrowsFromAsync(shape, context, () => throw thrown, logger);

            exception.StatusCode.Should().Be(StatusCode.Cancelled, $"a {shape} call was cancelled");
            exception.Trailers.Should().BeEmpty();
            var record = logger.Records.Should().ContainSingle().Subject;
            record.LogLevel.Should().Be(LogLevel.Debug);
            record.EventId.Id.Should().Be(14204);
            record.Exception.Should().BeSameAs(thrown);
        }
    }

    [Fact]
    public async Task OperationCanceled_WhileTheCallIsNotCancelled_IsAnUnexpectedError()
    {
        // An internal timeout is a failure of the service, not a client that went away.
        var context = TestServerCallContext.Create();

        var exception = await ThrowsFromUnaryAsync(context, () => throw new TaskCanceledException("HttpClient timed out."));

        exception.ShouldHaveRichStatus(StatusCode.Internal).ErrorInfo().Reason.Should().Be(ErrorCodes.Unexpected.Default);
        _logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task R30_RpcExceptionOfTheService_IsRebuiltWithoutItsTrailers_KeepingItsCodeAndDetail()
    {
        // As received from another service: its own rich status and a trailer of its own.
        var foreignStatus = RpcStatusFactory.Create(Error.NotFound("quota.unknown", "Unknown quota."), httpContext: null, "billing.internal");
        var thrown = new RpcException(
            new Status(StatusCode.ResourceExhausted, "Quota exhausted."),
            new Metadata
            {
                { "x-internal-host", "db-7.internal" },
                { "grpc-status-details-bin", foreignStatus.ToByteArray() },
            });

        var exception = await ThrowsFromUnaryAsync(TestServerCallContext.Create(), () => throw thrown);

        exception.Should().NotBeSameAs(thrown);
        var status = exception.ShouldHaveRichStatus(StatusCode.ResourceExhausted);
        status.Message.Should().Be("Quota exhausted.", "a client category's detail is kept");
        status.Details.Should().ContainSingle();
        status.ErrorInfo().Should().BeEquivalentTo(new { Reason = "grpc.resource_exhausted", Domain });
        exception.Trailers.Select(entry => entry.Key).Should().Equal("grpc-status-details-bin");
        exception.EverythingTheClientSees().Should().NotContain("billing.internal").And.NotContain("db-7.internal");

        var record = _logger.Records.Should().ContainSingle().Subject;
        record.LogLevel.Should().Be(LogLevel.Debug);
        record.EventId.Id.Should().Be(14203);
    }

    [Theory]
    [InlineData(StatusCode.Unknown, "An unexpected error occurred.")]
    [InlineData(StatusCode.Internal, "An unexpected error occurred.")]
    [InlineData(StatusCode.DataLoss, "An unexpected error occurred.")]
    [InlineData(StatusCode.Unavailable, "The service is temporarily unavailable. Try again later.")]
    [InlineData(StatusCode.DeadlineExceeded, "The operation did not complete in time.")]
    public async Task R30_RpcExceptionOfAServerCategory_OutsideDevelopment_IsRedacted_AndLoggedAtError(StatusCode code, string generic)
    {
        var thrown = new RpcException(new Status(code, "Replica db-7.internal refused the connection."));

        var exception = await ThrowsFromUnaryAsync(TestServerCallContext.Create(), () => throw thrown);

        var status = exception.ShouldHaveRichStatus(code);
        status.Message.Should().Be(generic);
        status.ErrorInfo().Reason.Should().Be(GrpcErrorCodes.ForStatus(code));
        var record = _logger.Records.Should().ContainSingle().Subject;
        record.LogLevel.Should().Be(LogLevel.Error);
        record.EventId.Id.Should().Be(14202);
        record.Exception.Should().BeSameAs(thrown);
    }

    [Theory]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.AlreadyExists)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.OutOfRange)]
    [InlineData(StatusCode.Unimplemented)]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.Cancelled)]
    public async Task R30_RpcExceptionOfAClientCategory_KeepsItsDetail_AndIsLoggedAtDebug(StatusCode code)
    {
        var exception = await ThrowsFromUnaryAsync(TestServerCallContext.Create(), () => throw new RpcException(new Status(code, "The answer.")));

        exception.ShouldHaveRichStatus(code).Message.Should().Be("The answer.");
        _logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task R30_RpcExceptionOfAServerCategory_InDevelopment_KeepsItsDetail()
    {
        var context = TestServerCallContext.Create(
            configureServices: services => services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = Environments.Development }));

        var exception = await ThrowsFromUnaryAsync(context, () => throw new RpcException(new Status(StatusCode.Internal, "Replica db-7 refused.")));

        exception.ShouldHaveRichStatus(StatusCode.Internal).Message.Should().Be("Replica db-7 refused.");
    }

    [Fact]
    public async Task R30_RpcExceptionWithStatusOk_IsNoAnswer_AndEndsAsUnknown()
    {
        var exception = await ThrowsFromUnaryAsync(TestServerCallContext.Create(), () => throw new RpcException(Status.DefaultSuccess));

        exception.ShouldHaveRichStatus(StatusCode.Unknown).ErrorInfo().Reason.Should().Be("grpc.unknown");
    }

    [Fact]
    public async Task ResultFailure_EndedWithCoresExtension_GetsTheRichStatusWithTheErrorDomain()
    {
        var context = TestServerCallContext.Create();

        var exception = await ThrowsFromUnaryAsync(context, () => Result.Failure(Error.NotFound("order.not_found", "Order 42 was not found.")).ThrowIfFailure());

        var status = exception.ShouldHaveRichStatus(StatusCode.NotFound);
        status.Message.Should().Be("Order 42 was not found.");
        status.ErrorInfo().Should().BeEquivalentTo(new { Reason = "order.not_found", Domain });

        // Thrown by Core as a NotFoundException, it is logged like one: a client error, at Debug.
        var record = _logger.Records.Should().ContainSingle().Subject;
        record.LogLevel.Should().Be(LogLevel.Debug);
        record.Exception.Should().BeOfType<NotFoundException>();
    }

    [Fact]
    public async Task ContextWithoutARequest_IsStillMapped_AsInProduction()
    {
        // A ServerCallContext not hosted by ASP.NET Core has no HttpContext: GetHttpContext() throws.
        var context = global::Grpc.Core.Testing.TestServerCallContext.Create(
            "test-method", "localhost", DateTime.MaxValue, new Metadata(), CancellationToken.None, "peer", null, null,
            _ => Task.CompletedTask, () => null, _ => { });

        var exception = await ThrowsFromUnaryAsync(context, () => throw new DomainException(Error.Unavailable("search.unreachable", "Cluster 10.0.0.5 is down.")));

        var status = exception.ShouldHaveRichStatus(StatusCode.Unavailable);
        status.Message.Should().Be("The service is temporarily unavailable. Try again later.");
        status.ErrorInfo().Domain.Should().Be(Domain);
    }

    [Theory]
    [InlineData(ErrorType.BusinessRule)]
    [InlineData(ErrorType.Unexpected)]
    public async Task ValidationException_IsAlwaysInvalidArgument_AndLoggedAsAClientError(ErrorType type)
    {
        // As over HTTP (always 400, logged at Debug): a ValidationException is a rejected request, whatever its error's type.
        var exception = await ThrowsFromUnaryAsync(
            TestServerCallContext.Create(),
            () => throw new ValidationException(new Error("order.closed", "The order is closed.", type)));

        var status = exception.ShouldHaveRichStatus(StatusCode.InvalidArgument);
        status.ErrorInfo().Reason.Should().Be("order.closed");
        status.GetDetail<Google.Rpc.BadRequest>().Should().BeNull("one error without field errors of its own lists no violation, as over HTTP");
        _logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task ClientStreamingCall_ExceptionsAreMapped()
    {
        var exception = await ThrowsFromAsync(
            CallShape.ClientStreaming,
            TestServerCallContext.Create(),
            () => throw new NotFoundException(Error.NotFound("order.not_found", "Not found.")),
            _logger);

        exception.ShouldHaveRichStatus(StatusCode.NotFound);
    }

    [Fact]
    public async Task DuplexStreamingCall_ExceptionsAreMapped()
    {
        var exception = await ThrowsFromAsync(
            CallShape.DuplexStreaming,
            TestServerCallContext.Create(),
            () => throw new ConflictException(Error.Conflict("order.version_conflict", "Changed.")),
            _logger);

        exception.ShouldHaveRichStatus(StatusCode.Aborted);
    }

    [Fact]
    public async Task ServerStreamingCall_ExceptionsAreMapped()
    {
        var exception = await ThrowsFromAsync(
            CallShape.ServerStreaming,
            TestServerCallContext.Create(),
            () => throw new InvalidOperationException("boom"),
            _logger);

        exception.ShouldHaveRichStatus(StatusCode.Internal).Message.Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public async Task SuccessfulCall_PassesTheResponseThrough()
    {
        var response = await CreateInterceptor(_logger).UnaryServerHandler<string, string>(
            "request",
            TestServerCallContext.Create(),
            (request, _) => Task.FromResult(request + "!"));

        response.Should().Be("request!");
        _logger.Records.Should().BeEmpty();
    }

    private Task<RpcException> ThrowsFromUnaryAsync(ServerCallContext context, Action service) =>
        ThrowsFromAsync(CallShape.Unary, context, service, _logger);

    private static async Task<RpcException> ThrowsFromAsync(
        CallShape shape,
        ServerCallContext context,
        Action service,
        InMemoryLogger<GrpcExceptionInterceptor> logger)
    {
        var interceptor = CreateInterceptor(logger);

        Func<Task> act = shape switch
        {
            CallShape.Unary => () => interceptor.UnaryServerHandler<string, string>(
                "request", context, (_, _) => Run(service, "unreachable")),
            CallShape.ClientStreaming => () => interceptor.ClientStreamingServerHandler<string, string>(
                new EmptyStreamReader<string>(), context, (_, _) => Run(service, "unreachable")),
            CallShape.ServerStreaming => () => interceptor.ServerStreamingServerHandler<string, string>(
                "request", new NullStreamWriter<string>(), context, (_, _, _) => Run(service)),
            CallShape.DuplexStreaming => () => interceptor.DuplexStreamingServerHandler<string, string>(
                new EmptyStreamReader<string>(), new NullStreamWriter<string>(), context, (_, _, _) => Run(service)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        return (await act.Should().ThrowAsync<RpcException>()).Which;
    }

    private static async Task<T> Run<T>(Action service, T result)
    {
        await Task.Yield();
        service();
        return result;
    }

    private static async Task Run(Action service)
    {
        await Task.Yield();
        service();
    }

    private static GrpcExceptionInterceptor CreateInterceptor(InMemoryLogger<GrpcExceptionInterceptor> logger) =>
        new(logger, new TestEnvironment(), Microsoft.Extensions.Options.Options.Create(new SharedKernelGrpcOptions { ErrorDomain = Domain }));

    public enum CallShape
    {
        Unary,
        ClientStreaming,
        ServerStreaming,
        DuplexStreaming,
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "orders";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class EmptyStreamReader<T> : IAsyncStreamReader<T>
    {
        public T Current => throw new InvalidOperationException("The stream is empty.");

        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NullStreamWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message) => Task.CompletedTask;
    }
}
