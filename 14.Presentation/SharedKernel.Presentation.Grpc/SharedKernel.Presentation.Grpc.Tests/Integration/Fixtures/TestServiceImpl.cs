using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Extensions;
using SharedKernel.Localization;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;

/// <summary>
/// The test-only <c>TestService</c> (see <c>test.proto</c>): real RPCs for this package's in-process host tests. No
/// production behavior. Failed results end with <c>SharedKernel.Core</c>'s <c>GetValueOrThrow()</c>/<c>ThrowIfFailure()</c>,
/// as in a service.
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
        await AfterCancellation.WaitThenThrowAsync(request.Value, context.CancellationToken);
        return new EchoReply();
    }

    public override async Task StreamUntilCancelled(EchoRequest request, IServerStreamWriter<EchoReply> responseStream, ServerCallContext context)
    {
        await responseStream.WriteAsync(new EchoReply { Value = "first" });
        probe.Entered.TrySetResult();
        await AfterCancellation.WaitThenThrowAsync(request.Value, context.CancellationToken);
    }

    public override async Task<EchoReply> Relay(EchoRequest request, ServerCallContext context)
    {
        var downstream = context.GetHttpContext().RequestServices.GetRequiredService<Downstream>();

        try
        {
            return await downstream.Client.FailAsync(new EchoRequest { Value = request.Value }, cancellationToken: context.CancellationToken);
        }
        catch (RpcException exception)
        {
            // What this service received from the other one, for the test to compare with what its caller receives.
            downstream.Received.Enqueue(exception);
            throw;
        }
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

/// <summary>The other service an edge service's <c>Relay</c> calls, and every <see cref="RpcException"/> it received.</summary>
internal sealed class Downstream(TestService.TestServiceClient client)
{
    public TestService.TestServiceClient Client { get; } = client;

    public ConcurrentQueue<RpcException> Received { get; } = new();
}

/// <summary>
/// What <c>WaitForCancellation</c> and <c>StreamUntilCancelled</c> throw once their call is cancelled, named by the
/// request value — the exceptions a service really meets after the client went away (P-562 R33).
/// </summary>
internal static class AfterCancellation
{
    /// <summary>Nothing more: the <see cref="OperationCanceledException"/> of the wait itself.</summary>
    public const string Wait = "";

    /// <summary>The <see cref="IOException"/> of reading or writing an aborted stream.</summary>
    public const string Io = "io";

    /// <summary>The <see cref="InvalidOperationException"/> of writing to a completed call.</summary>
    public const string InvalidOperation = "invalid-operation";

    /// <summary>The <see cref="RpcException"/> of a downstream call cancelled with this one.</summary>
    public const string Rpc = "rpc";

    /// <summary>An error the service happened to raise on the way out.</summary>
    public const string ServerError = "server-error";

    public static async Task WaitThenThrowAsync(string failure, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) when (failure != Wait)
        {
            throw Create(failure);
        }
    }

    public static Exception Create(string failure) => failure switch
    {
        Io => new IOException("The request stream was aborted."),
        InvalidOperation => new InvalidOperationException("Can't write the message because the request is complete."),
        Rpc => new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client.")),
        ServerError => new DomainException(Error.Unavailable("search.unreachable", "Search cluster http://10.0.0.5:9200 is unreachable.")),
        _ => new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown failure."),
    };
}

/// <summary>The failures <c>Fail</c> and <c>StreamFail</c> raise, named by the request value.</summary>
internal static class Failures
{
    public const string NotFoundException = "not-found-exception";

    public const string ConflictException = "conflict-exception";

    public const string ValidationException = "validation-exception";

    public const string SingleValidationException = "single-validation-exception";

    public const string ValidationResult = "validation-result";

    public const string SingleValidationResult = "single-validation-result";

    public const string UnavailableException = "unavailable-exception";

    public const string TimeoutResult = "timeout-result";

    /// <summary>A <see cref="System.TimeoutException"/> of the service's own code.</summary>
    public const string TimeoutException = "timeout-exception";

    /// <summary>A wait the service cancels itself, as <c>HttpClient.Timeout</c> does: the call is not cancelled.</summary>
    public const string InternalTimeout = "internal-timeout";

    public const string UnavailableTaskResult = "unavailable-task-result";

    public const string ConflictTaskResult = "conflict-task-result";

    public const string LocalizedResult = "localized-result";

    public const string Unknown = "unknown";

    public const string UnknownWithStackText = "unknown-with-stack-text";

    public const string Rpc = "rpc";

    public const string InternalRpcWithTrailers = "internal-rpc-with-trailers";

    public const string OrderNotFoundMessage = "Order 42 was not found.";

    public const string UnavailableDetail = "Search cluster http://10.0.0.5:9200 is unreachable.";

    public const string TimeoutDetail = "Query against replica db-7 timed out after 30 s.";

    public const string UnknownDetail = "Connection string Server=db;Password=secret is invalid.";

    public const string StackTextDetail =
        "Object reference not set to an instance of an object.\n   at Orders.Data.OrderRepository.LoadAsync(Guid id) in /src/Orders/OrderRepository.cs:line 42";

    public const string RpcDetail = "Quota exhausted for tenant 7.";

    public const string InternalRpcDetail = "Replica db-7.internal refused the connection.";

    public const string InternalTrailerKey = "x-internal-host";

    public const string InternalTrailerValue = "db-7.internal";

    /// <summary>Prefix of a thrown <see cref="Core.Exceptions.ValidationException"/> with the given number of field errors, such as <c>many-violations:400</c>.</summary>
    public const string ManyViolations = "many-violations:";

    /// <summary>Prefix of a returned <c>Error.Validation(errors)</c> with the given number of field errors, such as <c>many-violations-result:400</c>.</summary>
    public const string ManyViolationsResult = "many-violations-result:";

    public static readonly LocalizedMessage<int> OrderNotFound =
        LocalizedMessage.Define<int>("order.not_found", "Order {orderId} was not found.", "orderId");

    public static readonly Error NameRequired = Field(Error.Validation("customer.name_required", "Name is required."), "customer.name");

    public static readonly Error EmailInvalid = Field(Error.Validation("customer.email_invalid", "Email is invalid."), "customer.email");

    // Names no field, so its violation is keyed by its code.
    public static readonly Error LinesEmpty = Error.Validation("order.lines_empty", "An order needs at least one line.");

    public static Task RaiseAsync(string failure)
    {
        if (failure.StartsWith(ManyViolations, StringComparison.Ordinal))
        {
            throw new ValidationException(ItemErrors(Count(failure, ManyViolations)));
        }

        if (failure.StartsWith(ManyViolationsResult, StringComparison.Ordinal))
        {
            return Throw(() => Result<string>.Failure(Error.Validation(ItemErrors(Count(failure, ManyViolationsResult)))).GetValueOrThrow());
        }

        return failure switch
        {
            NotFoundException => throw new NotFoundException(Error.NotFound("order.not_found", OrderNotFoundMessage)),
            ConflictException => throw new ConflictException(Error.Conflict("order.version_conflict", "The order was changed by someone else.")),
            ValidationException => throw new ValidationException([NameRequired, EmailInvalid, LinesEmpty]),
            SingleValidationException => throw new ValidationException(NameRequired),
            ValidationResult => Throw(() => Result<string>.Failure(Error.Validation([NameRequired, EmailInvalid, LinesEmpty])).GetValueOrThrow()),
            SingleValidationResult => Throw(() => Result<string>.Failure(NameRequired).GetValueOrThrow()),
            UnavailableException => throw new DomainException(Error.Unavailable("search.unreachable", UnavailableDetail)),
            TimeoutResult => Throw(() => Result.Failure(Error.Timeout("search.timeout", TimeoutDetail)).ThrowIfFailure()),
            TimeoutException => throw new TimeoutException(TimeoutDetail),
            InternalTimeout => WaitForAnInternalTimeoutAsync(),
            UnavailableTaskResult => Task.FromResult(Result<string>.Failure(Error.Unavailable("search.unreachable", UnavailableDetail))).GetValueOrThrow(),
            ConflictTaskResult => Task.FromResult(Result.Failure(Error.Conflict("order.version_conflict", "The order was changed by someone else."))).ThrowIfFailure(),
            LocalizedResult => Throw(() => Result<string>.Failure(OrderNotFound.ToError(ErrorType.NotFound, 42)).GetValueOrThrow()),
            Unknown => throw new InvalidOperationException(UnknownDetail),
            UnknownWithStackText => throw new InvalidOperationException(StackTextDetail),
            Rpc => throw new RpcException(new Status(StatusCode.ResourceExhausted, RpcDetail)),
            InternalRpcWithTrailers => throw new RpcException(
                new Status(StatusCode.Internal, InternalRpcDetail),
                new Metadata { { InternalTrailerKey, InternalTrailerValue } }),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown failure."),
        };
    }

    /// <summary>One field error per item, each naming its own field: <c>items[i].name</c>.</summary>
    public static Error[] ItemErrors(int count) =>
        [.. Enumerable.Range(0, count).Select(index =>
            Field(Error.Validation("item.name_required", "Name is required."), $"items[{index}].name"))];

    private static int Count(string failure, string prefix) =>
        int.Parse(failure.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture);

    private static async Task WaitForAnInternalTimeoutAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        await Task.Delay(Timeout.Infinite, timeout.Token);
    }

    private static Error Field(Error error, string path) =>
        error with { MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = path } };

    private static Task Throw(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
