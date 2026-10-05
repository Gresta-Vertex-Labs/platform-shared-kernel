using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SharedKernel.Core.Extensions;
using SharedKernel.Localization;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using Xunit;
using TestServerCallContext = SharedKernel.Testing.Grpc.TestServerCallContext;

namespace SharedKernel.Presentation.Grpc.Tests.Interceptors;

/// <summary>
/// P-562 R32: the package's own <c>ThrowIfFailure</c>/<c>GetValueOrThrow</c> are gone; a service ends a failed result
/// with <c>SharedKernel.Core</c>'s, which throw <c>Error.ToException()</c>. Through the interceptor, every error still
/// gets exactly the status the removed result path built — <see cref="RpcStatusFactory.Create"/> of the error with the
/// call's request and the error domain: the same code, message, <c>ErrorInfo</c> (reason, domain, metadata) and
/// <c>BadRequest</c> violations — synchronously, on a <see cref="Task{TResult}"/> and on a <see cref="ValueTask{TResult}"/>.
/// </summary>
public sealed class ResultFailureStatusTests
{
    private const string Domain = "orders.example.com";

    private static readonly LocalizedMessage<int> OrderNotFound =
        LocalizedMessage.Define<int>("order.not_found", "Order {orderId} was not found.", "orderId");

    public static TheoryData<string, Error> Errors => new()
    {
        { "not found", Error.NotFound("order.not_found", "Order 42 was not found.") },
        { "forbidden", Error.Forbidden("order.forbidden", "Not permitted.") },
        { "unauthorized", Error.Unauthorized("auth.expired", "The session expired.") },
        { "conflict", Error.Conflict("order.version_conflict", "The order was changed by someone else.") },
        { "business rule", new Error("order.closed", "The order is closed.", ErrorType.BusinessRule) },
        { "unexpected", Error.Unexpected("ledger.corrupt", "Ledger row 7 failed its checksum.") },
        { "unavailable", Error.Unavailable("search.unreachable", "Search cluster http://10.0.0.5:9200 is unreachable.") },
        { "timeout", Error.Timeout("search.timeout", "Query against replica db-7 timed out after 30 s.") },
        { "one validation error", Field(Error.Validation("customer.name_required", "Name is required."), "customer.name") },
        {
            "several validation errors",
            Error.Validation([
                Field(Error.Validation("customer.name_required", "Name is required."), "customer.name"),
                Error.Validation("order.lines_empty", "An order needs at least one line."),
            ])
        },
        { "localized", OrderNotFound.ToError(ErrorType.NotFound, 42) },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task EveryWayToEndAFailedResult_GetsTheStatusOfTheRemovedResultPath(string description, Error error)
    {
        var endings = new (string Name, Func<Task> End)[]
        {
            ("Result.ThrowIfFailure()", () => Sync(() => Result.Failure(error).ThrowIfFailure())),
            ("Result<T>.GetValueOrThrow()", () => Sync(() => Result<int>.Failure(error).GetValueOrThrow())),
            ("Task<Result>.ThrowIfFailure()", () => Task.FromResult(Result.Failure(error)).ThrowIfFailure()),
            ("Task<Result<T>>.GetValueOrThrow()", () => Task.FromResult(Result<int>.Failure(error)).GetValueOrThrow()),
            ("ValueTask<Result>.ThrowIfFailure()", () => ValueTask.FromResult(Result.Failure(error)).ThrowIfFailure().AsTask()),
            ("ValueTask<Result<T>>.GetValueOrThrow()", () => ValueTask.FromResult(Result<int>.Failure(error)).GetValueOrThrow().AsTask()),
        };

        foreach (var (name, end) in endings)
        {
            var context = TestServerCallContext.Create(correlationId: "flow-1");

            var exception = await ThrowsThroughTheInterceptorAsync(context, end);

            var expected = RpcStatusFactory.Create(error, context.GetHttpContext(), Domain);
            exception.GetRpcStatus().Should().Be(expected, $"{description} ended with {name}");
            exception.StatusCode.Should().Be((StatusCode)expected.Code);
            exception.Status.Detail.Should().Be(expected.Message);
        }
    }

    [Fact]
    public async Task SeveralValidationErrors_KeepTheirCodeAndEveryFieldViolation()
    {
        var error = Error.Validation([
            Field(Error.Validation("customer.name_required", "Name is required."), "customer.name"),
            Error.Validation("order.lines_empty", "An order needs at least one line."),
        ]);

        var exception = await ThrowsThroughTheInterceptorAsync(
            TestServerCallContext.Create(),
            () => Task.FromResult(Result<string>.Failure(error)).GetValueOrThrow());

        var status = exception.ShouldHaveRichStatus(StatusCode.InvalidArgument);
        status.Message.Should().Be("2 validation errors occurred.");
        status.ErrorInfo().Should().BeEquivalentTo(new { Reason = ErrorCodes.Validation.Failed, Domain });
        status.FieldViolations().Should().Equal(
            ("customer.name", "Name is required.", "customer.name_required"),
            ("order.lines_empty", "An order needs at least one line.", "order.lines_empty"));
    }

    [Theory]
    [InlineData(ErrorType.Unavailable, StatusCode.Unavailable, "The service is temporarily unavailable. Try again later.")]
    [InlineData(ErrorType.Timeout, StatusCode.DeadlineExceeded, "The operation did not complete in time.")]
    [InlineData(ErrorType.Unexpected, StatusCode.Internal, "An unexpected error occurred.")]
    public async Task ServerErrors_ThrownByCoreAsDomainException_KeepTheirStatusCode_AndAreRedacted(ErrorType type, StatusCode expected, string generic)
    {
        var error = new Error("search.unreachable", "Search cluster http://10.0.0.5:9200 is unreachable.", type);

        var exception = await ThrowsThroughTheInterceptorAsync(
            TestServerCallContext.Create(),
            () => Sync(() => Result.Failure(error).ThrowIfFailure()));

        var status = exception.ShouldHaveRichStatus(expected);
        status.Message.Should().Be(generic);
        status.ErrorInfo().Reason.Should().Be("search.unreachable");
    }

    [Fact]
    public async Task Success_ReturnsTheValue_AndThrowsNothing()
    {
        var interceptor = CreateInterceptor();

        var response = await interceptor.UnaryServerHandler<string, string>(
            "request",
            TestServerCallContext.Create(),
            (_, _) => Task.FromResult(Result<string>.Success("ok")).GetValueOrThrow());

        response.Should().Be("ok");
    }

    private static async Task<RpcException> ThrowsThroughTheInterceptorAsync(ServerCallContext context, Func<Task> service)
    {
        var act = () => CreateInterceptor().UnaryServerHandler<string, string>(
            "request",
            context,
            async (_, _) =>
            {
                await service();
                return "unreachable";
            });

        return (await act.Should().ThrowAsync<RpcException>()).Which;
    }

    private static GrpcExceptionInterceptor CreateInterceptor() =>
        new(
            new InMemoryLogger<GrpcExceptionInterceptor>(),
            new ProductionEnvironment(),
            Microsoft.Extensions.Options.Options.Create(new SharedKernelGrpcOptions { ErrorDomain = Domain }));

    private static Task Sync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static Error Field(Error error, string path) =>
        error with { MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = path } };

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "orders";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
