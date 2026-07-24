using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Workflows.Temporal.Errors;

namespace SharedKernel.Workflows.Temporal.Tests.Errors;

/// <summary>
/// T-01 — every <see cref="WorkflowErrors"/> factory returns the expected <see cref="ErrorType"/>
/// and a non-empty code; none returns <see cref="Error.None"/>; <see cref="ErrorType.BusinessRule"/>
/// is used zero times; <see cref="WorkflowErrors.TenantScopeMissing"/> is <see cref="ErrorType.Unauthorized"/>
/// and <see cref="WorkflowErrors.AlreadyStarted"/> is <see cref="ErrorType.Conflict"/>.
/// </summary>
public sealed class WorkflowErrorsTests
{
    public static TheoryData<string, Error, ErrorType> AllFactories() => new()
    {
        { nameof(WorkflowErrors.NotFound), WorkflowErrors.NotFound("wf-1"), ErrorType.NotFound },
        { nameof(WorkflowErrors.AlreadyStarted), WorkflowErrors.AlreadyStarted("wf-1"), ErrorType.Conflict },
        { nameof(WorkflowErrors.Cancelled), WorkflowErrors.Cancelled("wf-1"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.Terminated), WorkflowErrors.Terminated("wf-1"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.TimedOut), WorkflowErrors.TimedOut("wf-1"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.QueryFailed), WorkflowErrors.QueryFailed("reason"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.SignalFailed), WorkflowErrors.SignalFailed("reason"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.ServiceUnavailable), WorkflowErrors.ServiceUnavailable("reason"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.NamespaceNotFound), WorkflowErrors.NamespaceNotFound("ns"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.TenantScopeMissing), WorkflowErrors.TenantScopeMissing(), ErrorType.Unauthorized },
        { nameof(WorkflowErrors.InvalidWorkflowId), WorkflowErrors.InvalidWorkflowId("reason"), ErrorType.Validation },
        { nameof(WorkflowErrors.WorkerNotConfigured), WorkflowErrors.WorkerNotConfigured("tq"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.DeterminismViolation), WorkflowErrors.DeterminismViolation("reason"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.PayloadCodecFailure), WorkflowErrors.PayloadCodecFailure("reason"), ErrorType.Unexpected },
        { nameof(WorkflowErrors.InvalidWorkflowRegistration), WorkflowErrors.InvalidWorkflowRegistration("reason"), ErrorType.Validation },
    };

    [Theory]
    [MemberData(nameof(AllFactories))]
    public void Factory_ReturnsExpectedErrorType(string factoryName, Error error, ErrorType expectedType)
    {
        error.Type.Should().Be(expectedType, because: $"{factoryName} must classify as {expectedType}");
    }

    [Theory]
    [MemberData(nameof(AllFactories))]
    public void Factory_NeverReturnsErrorNone(string factoryName, Error error, ErrorType _)
    {
        error.Should().NotBe(Error.None, because: $"{factoryName} must never surface Error.None");
        error.Type.Should().NotBe(ErrorType.None);
    }

    [Theory]
    [MemberData(nameof(AllFactories))]
    public void Factory_ProducesNonEmptyCodeAndMessage(string factoryName, Error error, ErrorType _)
    {
        error.Code.Should().NotBeNullOrWhiteSpace(because: $"{factoryName} must carry a stable machine-readable code");
        error.Message.Should().NotBeNullOrWhiteSpace(because: $"{factoryName} must carry a human-readable message");
    }

    [Fact]
    public void NoFactory_EverProducesBusinessRule()
    {
        foreach (var row in AllFactories())
        {
            var error = (Error)row[1]!;
            error.Type.Should().NotBe(ErrorType.BusinessRule, because: "nothing in this capability package is a domain rule");
        }
    }

    [Fact]
    public void TenantScopeMissing_IsUnauthorized_NotValidation()
    {
        WorkflowErrors.TenantScopeMissing().Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public void AlreadyStarted_IsConflict()
    {
        WorkflowErrors.AlreadyStarted("wf-1").Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void AllFactories_ProduceCodesPrefixedWithWorkflow()
    {
        foreach (var row in AllFactories())
        {
            var error = (Error)row[1]!;
            error.Code.Should().StartWith("workflow.");
        }
    }
}
