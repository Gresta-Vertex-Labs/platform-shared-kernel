using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
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

        // P-566, defect 4: the correlation id is the dispatching caller's, never the ambient Activity's id.
        using var activity = new Activity("propagation-test-op").Start();
        const string expectedCorrelationId = "dispatch-caller-correlation";
        using var caller = RequestContextScope.Begin(new SystemRequestContext([], correlationId: expectedCorrelationId));

        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        Result<IWorkflowHandle<PropagationResult>> startResult = await dispatcher
            .StartAsync<PropagationParentWorkflow, string, PropagationResult>(
                "propagation-input",
                Options($"propagation-{Guid.NewGuid():N}"),
                TenantScope.For(TestTenants.Propagation));

        startResult.IsSuccess.Should().BeTrue();

        Result<PropagationResult> result = await startResult.Value.GetResultAsync();

        result.IsSuccess.Should().BeTrue();
        PropagationResult propagation = result.Value;

        propagation.TenantScope.Should().Be(TestTenants.Propagation.ToString(), because: "the workflow must observe the tenant scope set at dispatch time");
        propagation.CorrelationId.Should().Be(expectedCorrelationId, because: "the workflow must observe the ambient correlation id set at dispatch time");
        string expectedActivity = $"{TestTenants.Propagation}|{TestTenants.Propagation}|{expectedCorrelationId}";
        propagation.ActivityTenantScope.Should().Be(expectedActivity, because: "an activity invoked by the workflow must observe the same tenant scope, and run inside an ambient request context with that tenant and the dispatching caller's correlation id");

        propagation.Child.TenantScope.Should().Be(TestTenants.Propagation.ToString(), because: "tenant scope must propagate across a child-workflow hop");
        propagation.Child.CorrelationId.Should().Be(expectedCorrelationId, because: "correlation id must propagate across a child-workflow hop");
        propagation.Child.ActivityTenantScope.Should().Be(expectedActivity, because: "an activity invoked by the CHILD workflow must also observe the propagated tenant scope");
    }
}
