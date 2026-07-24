using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using Temporalio.Api.Enums.V1;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// T-15 — propagation round-trip against the real environment: a correlation id and tenant id set
/// client-side arrive intact in <c>WorkflowBase.CorrelationId</c>/<c>.TenantScope</c> and in
/// <c>ActivityBase.TenantScope</c> for an activity that workflow invoked, including across a
/// child-workflow hop. Asserted against <c>01.Core</c>'s <see cref="WellKnownHeaders"/> constants,
/// never a retyped literal.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class PropagationTests(TemporalTestFixture fixture)
{
    private static WorkflowStartOptions Options(string businessKey) => new()
    {
        TaskQueue = TemporalTestFixture.TaskQueue,
        BusinessKey = businessKey,
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    [Fact]
    public async Task TenantScopeAndCorrelationId_PropagateToWorkflowActivityAndChildWorkflow()
    {
        // These constants are 01.Core's own WellKnownHeaders — the interceptor being exercised here
        // must be wired to the SAME constants, never an independently-declared literal (WO-042).
        WellKnownHeaders.TenantId.Should().NotBeNullOrWhiteSpace();
        WellKnownHeaders.CorrelationId.Should().NotBeNullOrWhiteSpace();

        using var activity = new Activity("propagation-test-op").Start();
        string expectedCorrelationId = Activity.Current!.Id!;

        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        Result<IWorkflowHandle<PropagationResult>> startResult = await dispatcher
            .StartAsync<PropagationParentWorkflow, string, PropagationResult>(
                "propagation-input",
                Options($"propagation-{Guid.NewGuid():N}"),
                TenantScope.Of("tenant-propagation"));

        startResult.IsSuccess.Should().BeTrue();

        Result<PropagationResult> result = await startResult.Value.GetResultAsync();

        result.IsSuccess.Should().BeTrue();
        PropagationResult propagation = result.Value;

        propagation.TenantScope.Should().Be("tenant-propagation", because: "the workflow must observe the tenant scope set at dispatch time");
        propagation.CorrelationId.Should().Be(expectedCorrelationId, because: "the workflow must observe the ambient correlation id set at dispatch time");
        propagation.ActivityTenantScope.Should().Be("tenant-propagation", because: "an activity invoked by the workflow must observe the same tenant scope");

        propagation.Child.TenantScope.Should().Be("tenant-propagation", because: "tenant scope must propagate across a child-workflow hop");
        propagation.Child.CorrelationId.Should().Be(expectedCorrelationId, because: "correlation id must propagate across a child-workflow hop");
        propagation.Child.ActivityTenantScope.Should().Be("tenant-propagation", because: "an activity invoked by the CHILD workflow must also observe the propagated tenant scope");
    }
}
