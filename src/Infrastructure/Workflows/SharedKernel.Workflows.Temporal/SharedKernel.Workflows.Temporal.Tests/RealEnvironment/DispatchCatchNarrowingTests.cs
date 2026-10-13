using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Client.Interceptors;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// The dispatch sites catch only a Temporal failure (and the SDK failed-connect exception): any other exception, a
/// cancellation included, propagates unchanged. A Temporal failure the mapper has no row for becomes
/// <c>workflow.unmapped_failure</c> with a fixed message and is logged as event 17015 with its exception. A real client
/// is used, with an outbound interceptor that throws the exception under test.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class DispatchCatchNarrowingTests(TemporalTestFixture fixture)
{
    private const string UniqueText = "exception-text-that-must-not-leak";

    private sealed class SampleWorkflow : WorkflowBase
    {
    }

    internal sealed class UnmappedTemporalException(string message) : TemporalException(message)
    {
    }

    internal sealed class ThrowingInterceptor(Func<Exception> make) : IClientInterceptor
    {
        public ClientOutboundInterceptor InterceptClient(ClientOutboundInterceptor nextInterceptor) => new Outbound(nextInterceptor, make);

        private sealed class Outbound(ClientOutboundInterceptor next, Func<Exception> make) : ClientOutboundInterceptor(next)
        {
            public override Task<WorkflowHandle<TWorkflow, TResult>> StartWorkflowAsync<TWorkflow, TResult>(StartWorkflowInput input)
                => throw make();

            public override Task SignalWorkflowAsync(SignalWorkflowInput input) => throw make();

            public override Task<TQueryResult> QueryWorkflowAsync<TQueryResult>(QueryWorkflowInput input) => throw make();

            public override Task CancelWorkflowAsync(CancelWorkflowInput input) => throw make();

            public override Task TerminateWorkflowAsync(TerminateWorkflowInput input) => throw make();

            public override Task<Temporalio.Client.WorkflowExecutionDescription> DescribeWorkflowAsync(DescribeWorkflowInput input)
                => throw make();
        }
    }

    private async Task<(WorkflowDispatcher Dispatcher, InMemoryLogger<WorkflowDispatcher> Logger)> DispatcherThrowingAsync(Func<Exception> make)
    {
        ITemporalClient client = await TemporalClient.ConnectAsync(
            new TemporalClientConnectOptions(fixture.Environment.Client.Connection.Options.TargetHost!)
            {
                Namespace = fixture.Environment.Client.Options.Namespace,
                Interceptors = [new ThrowingInterceptor(make)],
            });
        var logger = new InMemoryLogger<WorkflowDispatcher>();
        return (new WorkflowDispatcher(client, new WorkflowIdFactory(), logger), logger);
    }

    private static WorkflowStartOptions Options() => new()
    {
        TaskQueue = "narrowing-queue",
        BusinessKey = "narrowing-key",
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    private static string OwnId() => $"{TestTenants.Observability}:SampleWorkflow:narrowing-key";

    private static TenantScope Scope() => TenantScope.For(TestTenants.Observability);

    public static TheoryData<string> Operations() => new() { "start", "describe", "signal", "query", "cancel", "terminate" };

    private static Task<Result> RunAsync(WorkflowDispatcher dispatcher, string operation, CancellationToken cancellationToken = default)
    {
        IWorkflowHandle HandleOf() => dispatcher.GetHandle(OwnId(), runId: null, Scope()).Value;
        return operation switch
        {
            "start" => ToResultAsync(dispatcher.StartAsync<SampleWorkflow>(Options(), Scope(), cancellationToken)),
            "describe" => ToResultAsync(dispatcher.DescribeAsync(OwnId(), Scope(), cancellationToken)),
            "signal" => HandleOf().SignalAsync("Signal", "go", cancellationToken),
            "query" => ToResultAsync(HandleOf().QueryAsync<string>("state", cancellationToken)),
            "cancel" => HandleOf().CancelAsync(cancellationToken),
            "terminate" => HandleOf().TerminateAsync("test reason", cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    private static async Task<Result> ToResultAsync<T>(Task<Result<T>> task)
    {
        Result<T> result = await task;
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ANonTemporalException_Propagates_AndIsNotTurnedIntoAResult(string operation)
    {
        (WorkflowDispatcher dispatcher, InMemoryLogger<WorkflowDispatcher> logger) =
            await DispatcherThrowingAsync(() => new InvalidOperationException(UniqueText));

        Func<Task> act = () => RunAsync(dispatcher, operation);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(UniqueText);
        logger.Records.Should().NotContain(record => record.EventId.Id == 17014 || record.EventId.Id == 17015);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ACancellation_Propagates_Unchanged(string operation)
    {
        (WorkflowDispatcher dispatcher, _) = await DispatcherThrowingAsync(() => new OperationCanceledException());

        Func<Task> act = () => RunAsync(dispatcher, operation);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ATemporalFailureWithNoMappingRow_IsUnmappedFailure_WithAFixedMessage_AndLogs17015WithTheException(string operation)
    {
        var failure = new UnmappedTemporalException(UniqueText);
        (WorkflowDispatcher dispatcher, InMemoryLogger<WorkflowDispatcher> logger) = await DispatcherThrowingAsync(() => failure);

        Result result = await RunAsync(dispatcher, operation);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.UnmappedFailure());
        result.Error.Code.Should().Be(WorkflowErrorCodes.UnmappedFailure);
        result.Error.Type.Should().Be(ErrorType.Unexpected);
        result.Error.Message.Should().NotContain(UniqueText);
        LogRecord record = logger.Records.ShouldHaveLogged(new EventId(17015), LogLevel.Error);
        record.Exception.Should().BeSameAs(failure);
        logger.Records.ShouldNotLeak(OwnId(), TestTenants.Observability.ToString(), "narrowing-key");
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task AMappedTemporalFailure_Logs17014WithCodeAndType_NotTheException(string operation)
    {
        (WorkflowDispatcher dispatcher, InMemoryLogger<WorkflowDispatcher> logger) =
            await DispatcherThrowingAsync(() => new WorkflowAlreadyStartedException("already started", "wf-id", "SampleWorkflow", "run-id"));

        Result result = await RunAsync(dispatcher, operation);

        result.Error.Code.Should().Be(WorkflowErrorCodes.AlreadyStarted);
        LogRecord record = logger.Records.ShouldHaveLogged(new EventId(17014), LogLevel.Warning);
        record.Exception.Should().BeNull();
        logger.Records.ShouldHaveLoggedWithProperty(new EventId(17014), "ErrorCode", WorkflowErrorCodes.AlreadyStarted);
        logger.Records.ShouldHaveLoggedWithProperty(new EventId(17014), "ErrorType", ErrorType.Conflict);
        logger.Records.ShouldNotLeak(OwnId(), TestTenants.Observability.ToString(), "narrowing-key");
        logger.Records.ShouldFollowLogConvention();
    }
}
