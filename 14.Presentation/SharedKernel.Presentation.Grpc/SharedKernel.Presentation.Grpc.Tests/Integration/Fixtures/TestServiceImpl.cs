using System.Diagnostics;
using Grpc.Core;
using SharedKernel.Core.Exceptions;
using SharedKernel.Localization;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;

/// <summary>
/// The test-only <c>TestService</c> (see <c>test.proto</c>): real RPCs for this package's in-process host tests. No
/// production behavior.
/// </summary>
internal sealed class TestServiceImpl(CallProbe probe) : TestService.TestServiceBase
{
    public override Task<EchoReply> Echo(EchoRequest request, ServerCallContext context) =>
        Task.FromResult(new EchoReply { Value = request.Value });

    public override async Task<EchoReply> Fail(EchoRequest request, ServerCallContext context)
    {
        await Failures.RaiseAsync(request.Value);
        return new EchoReply();
    }

    public override async Task StreamFail(EchoRequest request, IServerStreamWriter<EchoReply> responseStream, ServerCallContext context)
    {
        await responseStream.WriteAsync(new EchoReply { Value = "first" });
        await Failures.RaiseAsync(request.Value);
    }

    public override async Task<EchoReply> WaitForCancellation(EchoRequest request, ServerCallContext context)
    {
        probe.Entered.TrySetResult();
        await Task.Delay(Timeout.Infinite, context.CancellationToken);
        return new EchoReply();
    }

    public override Task<ContextReply> GetContext(EchoRequest request, ServerCallContext context) =>
        Task.FromResult(new ContextReply
        {
            CorrelationId = context.GetHttpContext().GetCorrelationId() ?? string.Empty,
            BaggageCorrelationId = Activity.Current?.GetBaggageItem(WellKnownBaggageKeys.CorrelationId) ?? string.Empty,
        });

    [RequirePermission(TestAuthentication.ReadPermission)]
    public override Task<EchoReply> ReadOrders(EchoRequest request, ServerCallContext context) =>
        Task.FromResult(new EchoReply { Value = "orders" });

    [RequireFreshAuthentication(60)]
    public override Task<EchoReply> ApproveOrder(EchoRequest request, ServerCallContext context) =>
        Task.FromResult(new EchoReply { Value = "approved" });
}

/// <summary>Signals the tests what a call is doing on the server.</summary>
internal sealed class CallProbe
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>The failures <c>Fail</c> and <c>StreamFail</c> raise, named by the request value.</summary>
internal static class Failures
{
    public const string NotFoundException = "not-found-exception";

    public const string ConflictException = "conflict-exception";

    public const string ValidationException = "validation-exception";

    public const string SingleValidationException = "single-validation-exception";

    public const string ValidationResult = "validation-result";

    public const string UnavailableException = "unavailable-exception";

    public const string TimeoutResult = "timeout-result";

    public const string UnavailableTaskResult = "unavailable-task-result";

    public const string ConflictTaskResult = "conflict-task-result";

    public const string LocalizedResult = "localized-result";

    public const string Unknown = "unknown";

    public const string Rpc = "rpc";

    public const string OrderNotFoundMessage = "Order 42 was not found.";

    public const string UnavailableDetail = "Search cluster http://10.0.0.5:9200 is unreachable.";

    public const string TimeoutDetail = "Query against replica db-7 timed out after 30 s.";

    public const string UnknownDetail = "Connection string Server=db;Password=secret is invalid.";

    public const string RpcDetail = "Quota exhausted for tenant 7.";

    public static readonly LocalizedMessage<int> OrderNotFound =
        LocalizedMessage.Define<int>("order.not_found", "Order {orderId} was not found.", "orderId");

    public static readonly Error NameRequired = Field(Error.Validation("customer.name_required", "Name is required."), "customer.name");

    public static readonly Error EmailInvalid = Field(Error.Validation("customer.email_invalid", "Email is invalid."), "customer.email");

    // Names no field, so its violation is keyed by its code.
    public static readonly Error LinesEmpty = Error.Validation("order.lines_empty", "An order needs at least one line.");

    public static Task RaiseAsync(string failure) => failure switch
    {
        NotFoundException => throw new NotFoundException(Error.NotFound("order.not_found", OrderNotFoundMessage)),
        ConflictException => throw new ConflictException(Error.Conflict("order.version_conflict", "The order was changed by someone else.")),
        ValidationException => throw new ValidationException([NameRequired, EmailInvalid, LinesEmpty]),
        SingleValidationException => throw new ValidationException(NameRequired),
        ValidationResult => Throw(() => Result<string>.Failure(Error.Validation([NameRequired, EmailInvalid, LinesEmpty])).GetValueOrThrow()),
        UnavailableException => throw new DomainException(Error.Unavailable("search.unreachable", UnavailableDetail)),
        TimeoutResult => Throw(() => Result.Failure(Error.Timeout("search.timeout", TimeoutDetail)).ThrowIfFailure()),
        UnavailableTaskResult => Task.FromResult(Result<string>.Failure(Error.Unavailable("search.unreachable", UnavailableDetail))).GetValueOrThrow(),
        ConflictTaskResult => Task.FromResult(Result.Failure(Error.Conflict("order.version_conflict", "The order was changed by someone else."))).ThrowIfFailure(),
        LocalizedResult => Throw(() => Result<string>.Failure(OrderNotFound.ToError(ErrorType.NotFound, 42)).GetValueOrThrow()),
        Unknown => throw new InvalidOperationException(UnknownDetail),
        Rpc => throw new RpcException(new Status(StatusCode.ResourceExhausted, RpcDetail)),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown failure."),
    };

    private static Error Field(Error error, string path) =>
        error with { MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = path } };

    private static Task Throw(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
