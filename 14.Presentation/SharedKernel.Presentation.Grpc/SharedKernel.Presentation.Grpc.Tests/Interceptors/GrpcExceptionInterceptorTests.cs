using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
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
/// <see cref="ServerCallContext"/>: cancellation, pass-through, completion of result failures, and a context without
/// a request.
/// </summary>
public sealed class GrpcExceptionInterceptorTests
{
    private const string Domain = "orders.example.com";

    private readonly InMemoryLogger<GrpcExceptionInterceptor> _logger = new();

    [Fact]
    public async Task ClientCancellation_EndsAsCancelled_AndIsLoggedAtDebugOnly()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var context = TestServerCallContext.Create(cancellationToken: cancellation.Token);

        var exception = await ThrowsFromUnaryAsync(context, () => throw new OperationCanceledException(cancellation.Token));

        exception.StatusCode.Should().Be(StatusCode.Cancelled);
        _logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Debug);
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
    public async Task RpcExceptionOfTheService_IsNeverCaught()
    {
        var thrown = new RpcException(new Status(StatusCode.ResourceExhausted, "Quota exhausted."));

        var exception = await ThrowsFromUnaryAsync(TestServerCallContext.Create(), () => throw thrown);

        exception.Should().BeSameAs(thrown);
        _logger.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task ResultFailure_IsCompletedWithTheErrorDomain_AndNotLogged()
    {
        var context = TestServerCallContext.Create();

        var exception = await ThrowsFromUnaryAsync(context, () => Result.Failure(Error.NotFound("order.not_found", "Order 42 was not found.")).ThrowIfFailure());

        var errorInfo = exception.ShouldHaveRichStatus(StatusCode.NotFound).ErrorInfo();
        errorInfo.Reason.Should().Be("order.not_found");
        errorInfo.Domain.Should().Be(Domain);
        _logger.Records.Should().BeEmpty();
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
        status.FieldViolations().Should().ContainSingle().Which.Field.Should().Be("order.closed");
        _logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task ClientStreamingCall_ExceptionsAreMapped()
    {
        var interceptor = CreateInterceptor();

        var act = () => interceptor.ClientStreamingServerHandler<string, string>(
            new EmptyStreamReader<string>(),
            TestServerCallContext.Create(),
            (_, _) => throw new NotFoundException(Error.NotFound("order.not_found", "Not found.")));

        (await act.Should().ThrowAsync<RpcException>()).Which.ShouldHaveRichStatus(StatusCode.NotFound);
    }

    [Fact]
    public async Task DuplexStreamingCall_ExceptionsAreMapped()
    {
        var interceptor = CreateInterceptor();

        var act = () => interceptor.DuplexStreamingServerHandler<string, string>(
            new EmptyStreamReader<string>(),
            new NullStreamWriter<string>(),
            TestServerCallContext.Create(),
            (_, _, _) => throw new ConflictException(Error.Conflict("order.version_conflict", "Changed.")));

        (await act.Should().ThrowAsync<RpcException>()).Which.ShouldHaveRichStatus(StatusCode.Aborted);
    }

    [Fact]
    public async Task ServerStreamingCall_ExceptionsAreMapped()
    {
        var interceptor = CreateInterceptor();

        var act = () => interceptor.ServerStreamingServerHandler<string, string>(
            "request",
            new NullStreamWriter<string>(),
            TestServerCallContext.Create(),
            (_, _, _) => throw new InvalidOperationException("boom"));

        (await act.Should().ThrowAsync<RpcException>()).Which.ShouldHaveRichStatus(StatusCode.Internal)
            .Message.Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public async Task SuccessfulCall_PassesTheResponseThrough()
    {
        var response = await CreateInterceptor().UnaryServerHandler<string, string>(
            "request",
            TestServerCallContext.Create(),
            (request, _) => Task.FromResult(request + "!"));

        response.Should().Be("request!");
        _logger.Records.Should().BeEmpty();
    }

    private async Task<RpcException> ThrowsFromUnaryAsync(ServerCallContext context, Action service)
    {
        var act = () => CreateInterceptor().UnaryServerHandler<string, string>(
            "request",
            context,
            (_, _) =>
            {
                service();
                return Task.FromResult("unreachable");
            });

        return (await act.Should().ThrowAsync<RpcException>()).Which;
    }

    private GrpcExceptionInterceptor CreateInterceptor() =>
        new(_logger, new ProductionEnvironment(), Microsoft.Extensions.Options.Options.Create(new SharedKernelGrpcOptions { ErrorDomain = Domain }));

    private sealed class ProductionEnvironment : IHostEnvironment
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
