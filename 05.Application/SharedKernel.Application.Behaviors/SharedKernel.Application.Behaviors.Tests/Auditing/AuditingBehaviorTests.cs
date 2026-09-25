using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Auditing;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Auditing;

/// <summary>
/// The outer half of auditing in isolation: it records failures only. The success half runs inside
/// the transaction and is proven through the composed pipeline in <c>TransactionalAuditingOrderingTests</c>.
/// </summary>
public sealed class AuditingBehaviorTests
{
    private sealed record TestCommand(string ResourceId) : ICommand<string>, IAuditableRequest<Result<string>>
    {
        public string Action => "test.action";
        public string ResourceType => "TestResource";
        public string? BeforeSnapshot => "before";

        public string? GetAfterSnapshot(Result<string> response) => response.IsSuccess ? response.Value : null;
    }

    private static AuditingBehavior<TestCommand, Result<string>> CreateBehavior(
        IAuditTrailWriter writer,
        ILogger<AuditingBehavior<TestCommand, Result<string>>>? logger = null)
        => new(writer, logger ?? new FakeLogger<AuditingBehavior<TestCommand, Result<string>>>());

    [Fact]
    public async Task Handle_Success_RecordsNothing_TheInnerHalfOwnsSuccess()
    {
        var writer = new FakeAuditTrailWriter();
        var behavior = CreateBehavior(writer);

        await behavior.Handle(new TestCommand("r-1"), () => Task.FromResult(Result<string>.Success("after")), CancellationToken.None);

        writer.RecordedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ResultFailure_RecordsFailedEntryWithErrorCode()
    {
        var writer = new FakeAuditTrailWriter();
        var behavior = CreateBehavior(writer);
        var error = Error.BusinessRule("rule.denied", "not allowed");

        await behavior.Handle(new TestCommand("r-1"), () => Task.FromResult(Result<string>.Failure(error)), CancellationToken.None);

        var entry = writer.RecordedEntries.Should().ContainSingle().Subject;
        entry.Outcome.Should().Be(AuditOutcome.Failed);
        entry.AfterSnapshot.Should().BeNull();
        entry.ErrorCode.Should().Be("rule.denied");
        entry.BeforeSnapshot.Should().Be("before");
    }

    [Fact]
    public async Task Handle_ThrownException_RecordsFaultEntryAndRethrows()
    {
        var writer = new FakeAuditTrailWriter();
        var behavior = CreateBehavior(writer);

        var act = async () => await behavior.Handle(new TestCommand("r-1"), () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        var entry = writer.RecordedEntries.Should().ContainSingle().Subject;
        entry.Outcome.Should().Be(AuditOutcome.Failed);
        entry.AfterSnapshot.Should().BeNull();
        entry.ErrorCode.Should().Be(typeof(InvalidOperationException).FullName);
        entry.Action.Should().Be("test.action");
        entry.ResourceId.Should().Be("r-1");
    }

    [Fact]
    public async Task Handle_WriterThrowsOnResultFailure_PropagatesUnchanged()
    {
        var writer = new FakeAuditTrailWriter { FailWith = new InvalidOperationException("writer exploded") };
        var behavior = CreateBehavior(writer);

        var act = async () => await behavior.Handle(
            new TestCommand("r-1"),
            () => Task.FromResult(Result<string>.Failure(Error.BusinessRule("rule", "denied"))),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("writer exploded");
    }

    [Fact]
    public async Task Handle_HandlerThrows_AndWriterAlsoThrowsRecordingTheFault_OriginalExceptionStillPropagates_AndFailureIsLogged()
    {
        var writer = new FakeAuditTrailWriter { FailWith = new InvalidOperationException("writer exploded") };
        var logger = new FakeLogger<AuditingBehavior<TestCommand, Result<string>>>();
        var behavior = CreateBehavior(writer, logger);

        var act = async () => await behavior.Handle(new TestCommand("r-1"), () => throw new InvalidOperationException("handler boom"), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("handler boom");
        var logEntry = logger.Entries.Should().ContainSingle().Subject;
        logEntry.Level.Should().Be(LogLevel.Error);
        logEntry.Exception!.Message.Should().Be("writer exploded");
    }
}
