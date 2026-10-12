using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Client;
using WorkflowExecutionDescription = SharedKernel.Workflows.Temporal.Dispatch.WorkflowExecutionDescription;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// The status-aware failure mapping against a real server: a missing workflow, a missing namespace and an unreachable
/// service each surface as their own code and <see cref="ErrorType"/>, and no message carries the id or the host.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class DispatchFailureMappingTests(TemporalTestFixture fixture, LocalDevServerFixture localServer)
    : IClassFixture<LocalDevServerFixture>
{
    private static string ComposedMissingId() => $"{TestTenants.Observability}:EchoWorkflow:missing-{Guid.NewGuid():N}";

    private static WorkflowDispatcher DispatcherFor(ITemporalClient client)
        => new(client, new WorkflowIdFactory(), NullLogger<WorkflowDispatcher>.Instance);

    [Fact]
    public async Task DescribeAsync_AMissingWorkflow_IsWorkflowNotFound()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        string workflowId = ComposedMissingId();

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(
            workflowId, TenantScope.For(TestTenants.Observability));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("workflow.not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Message.Should().NotContain(workflowId).And.NotContain("missing-");
    }

    [Fact]
    public async Task HandleSignalAndQuery_OnAMissingWorkflow_AreWorkflowNotFound()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        string workflowId = ComposedMissingId();
        IWorkflowHandle handle = dispatcher.GetHandle(workflowId, runId: null, TenantScope.For(TestTenants.Observability));

        Result signalled = await handle.SignalAsync("Signal", "go");
        Result<string> queried = await handle.QueryAsync<string>("state");

        signalled.IsFailure.Should().BeTrue();
        signalled.Error.Code.Should().Be("workflow.not_found");
        signalled.Error.Type.Should().Be(ErrorType.NotFound);
        queried.IsFailure.Should().BeTrue();
        queried.Error.Code.Should().Be("workflow.not_found");
        queried.Error.Message.Should().NotContain(workflowId);
    }

    [Fact]
    public async Task DescribeAsync_AClientForAMissingNamespace_IsNamespaceNotFound_AndStaysUnexpected()
    {
        ITemporalClient client = await TemporalClient.ConnectAsync(
            new TemporalClientConnectOptions(localServer.Environment.Client.Connection.Options.TargetHost!)
            {
                Namespace = $"no-such-namespace-{Guid.NewGuid():N}",
            });
        WorkflowDispatcher dispatcher = DispatcherFor(client);

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(
            ComposedMissingId(), TenantScope.For(TestTenants.Observability));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("workflow.namespace_not_found");
        result.Error.Type.Should().Be(ErrorType.Unexpected, because: "a deployment fault must not surface as the end user's 404");
    }

    [Fact]
    public async Task DescribeAsync_ALazyClientThatCannotConnect_IsAnUnmappedFailure_WithAFixedMessageThatNamesNoHost()
    {
        // The SDK reports a failed connect as InvalidOperationException, not as an RpcException with status Unavailable,
        // so the status table cannot see it; it surfaces as the unmapped failure. Unavailable-by-status is covered in
        // WorkflowFailureMapperRpcStatusTests.
        ITemporalClient client = TemporalClient.CreateLazy(
            new TemporalClientConnectOptions("127.0.0.1:1") { Namespace = "default" });
        WorkflowDispatcher dispatcher = DispatcherFor(client);

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(
            ComposedMissingId(), TenantScope.For(TestTenants.Observability));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("workflow.unmapped_failure");
        result.Error.Type.Should().Be(ErrorType.Unexpected);
        result.Error.Message.Should().NotContain("127.0.0.1").And.NotContain("tcp");
    }
}
