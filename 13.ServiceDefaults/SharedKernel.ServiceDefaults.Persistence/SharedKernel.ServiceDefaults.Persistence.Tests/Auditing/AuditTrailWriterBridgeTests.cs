using FluentAssertions;
using MediatR;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.ServiceDefaults.Persistence.Auditing;
using SharedKernel.Testing.Persistence;
using PersistenceAuditOutcome = SharedKernel.Persistence.Abstractions.Auditing.AuditOutcome;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.Auditing;

/// <summary>
/// Proves <see cref="AuditTrailWriterBridge"/> end to end THROUGH <c>05.Application.Behaviors</c>'s
/// real, unmodified <see cref="AuditingBehavior{TRequest,TResponse}"/> — the exact call shape a
/// MediatR pipeline uses, not a direct unit test of the bridge class in isolation.
/// </summary>
/// <remarks>
/// Uses <c>16.Testing</c>'s <see cref="FakeAuditTrailWriter"/> (the <c>06.Persistence.Abstractions</c>
/// contract fake) as the bridge's downstream target — this test's job is proving the BRIDGE's own
/// mapping/wiring logic (<see cref="AuditEntry.Succeeded"/> → <see cref="PersistenceAuditOutcome"/>,
/// <see cref="AuditEntry.ErrorCode"/> carried through), not re-proving real-PostgreSQL storage
/// semantics — <c>SharedKernel.Persistence.EfCore.Auditing.Tests</c>'s own suite already does that
/// exhaustively against the real <c>EfAuditTrailWriter</c> this bridge delegates to in production.
/// </remarks>
public sealed class AuditTrailWriterBridgeTests
{
    private sealed record TestAuditableCommand(string ResourceId, bool ShouldFail)
        : ICommandBase, IAuditableRequest<Result>, IRequest<Result>
    {
        public string Action => "TestAction";
        public string ResourceType => "TestResource";
        public string? BeforeSnapshot => "{\"before\":true}";

        public string? GetAfterSnapshot(Result response) => response.IsSuccess ? "{\"after\":true}": null;
    }

    [Fact]
    public async Task AuditingBehavior_SuccessfulCommand_RecordsSucceededOutcome_ThroughBridge()
    {
        var persistenceWriter = new FakeAuditTrailWriter();
        var bridge = new AuditTrailWriterBridge(persistenceWriter);
        var behavior = new AuditingBehavior<TestAuditableCommand, Result>(bridge, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditingBehavior<TestAuditableCommand, Result>>.Instance);

        var command = new TestAuditableCommand("resource-1", ShouldFail: false);

        var response = await behavior.Handle(command, () => Task.FromResult(Result.Success()), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();

        var recorded = persistenceWriter.Records.Should().ContainSingle().Subject;
        recorded.Action.Should().Be("TestAction");
        recorded.ResourceType.Should().Be("TestResource");
        recorded.ResourceId.Should().Be("resource-1");
        recorded.Outcome.Should().Be(PersistenceAuditOutcome.Succeeded);
        recorded.ErrorCode.Should().BeNull();
        recorded.BeforeSnapshot.Should().Be("{\"before\":true}");
        recorded.AfterSnapshot.Should().Be("{\"after\":true}");
    }

    [Fact]
    public async Task AuditingBehavior_ResultFailureCommand_RecordsFailedOutcome_WithErrorCode_ThroughBridge()
    {
        var persistenceWriter = new FakeAuditTrailWriter();
        var bridge = new AuditTrailWriterBridge(persistenceWriter);
        var behavior = new AuditingBehavior<TestAuditableCommand, Result>(bridge, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditingBehavior<TestAuditableCommand, Result>>.Instance);

        var command = new TestAuditableCommand("resource-2", ShouldFail: true);
        var error = Error.Validation("test.rejected", "Rejected for testing.");

        var response = await behavior.Handle(command, () => Task.FromResult(Result.Failure(error)), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();

        var recorded = persistenceWriter.Records.Should().ContainSingle().Subject;
        recorded.Outcome.Should().Be(PersistenceAuditOutcome.Failed);
        recorded.ErrorCode.Should().Be("test.rejected");
        recorded.AfterSnapshot.Should().BeNull("a rejected command produced no new state to snapshot");
    }

    [Fact]
    public async Task AuditingBehavior_ThrownException_RecordsFailedOutcome_WithExceptionTypeAsErrorCode_ThroughBridge()
    {
        var persistenceWriter = new FakeAuditTrailWriter();
        var bridge = new AuditTrailWriterBridge(persistenceWriter);
        var behavior = new AuditingBehavior<TestAuditableCommand, Result>(bridge, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditingBehavior<TestAuditableCommand, Result>>.Instance);

        var command = new TestAuditableCommand("resource-3", ShouldFail: true);

        Func<Task> act = async () => await behavior.Handle(
            command,
            () => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        var recorded = persistenceWriter.Records.Should().ContainSingle().Subject;
        recorded.Outcome.Should().Be(PersistenceAuditOutcome.Failed);
        recorded.ErrorCode.Should().Be(typeof(InvalidOperationException).FullName);
    }
}
