using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Auditing;

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
    public async Task Handle_Success_RecordsEntryWithAfterSnapshot()
    {
        var writer = new FakeAuditTrailWriter();
        var behavior = CreateBehavior(writer);

        await behavior.Handle(new TestCommand("r-1"), () => Task.FromResult(Result<string>.Success("after")), CancellationToken.None);

        writer.RecordedEntries.Should().ContainSingle();
        var entry = writer.RecordedEntries[0];
        entry.Succeeded.Should().BeTrue();
        entry.AfterSnapshot.Should().Be("after");
        entry.ErrorCode.Should().BeNull();
        entry.BeforeSnapshot.Should().Be("before");
        entry.Action.Should().Be("test.action");
        entry.ResourceId.Should().Be("r-1");
    }

    [Fact]
    public async Task Handle_ResultFailure_RecordsEntryWithNullAfterSnapshotAndErrorCode()
    {
        var writer = new FakeAuditTrailWriter();
        var behavior = CreateBehavior(writer);
        var error = Error.BusinessRule("rule.denied", "not allowed");

        await behavior.Handle(new TestCommand("r-1"), () => Task.FromResult(Result<string>.Failure(error)), CancellationToken.None);

        writer.RecordedEntries.Should().ContainSingle();
        var entry = writer.RecordedEntries[0];
        entry.Succeeded.Should().BeFalse();
        entry.AfterSnapshot.Should().BeNull();
        entry.ErrorCode.Should().Be("rule.denied");
    }

    [Fact]
    public async Task Handle_ThrownException_RecordsFaultEntryAndRethrows()
    {
        var writer = new FakeAuditTrailWriter();
        var behavior = CreateBehavior(writer);

        var act = async () => await behavior.Handle(new TestCommand("r-1"), () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        writer.RecordedEntries.Should().ContainSingle();
        var entry = writer.RecordedEntries[0];
        entry.Succeeded.Should().BeFalse();
        entry.AfterSnapshot.Should().BeNull();
        entry.ErrorCode.Should().Be(typeof(InvalidOperationException).FullName);
        entry.BeforeSnapshot.Should().Be("before");
        entry.Action.Should().Be("test.action");
        entry.ResourceId.Should().Be("r-1");
    }

    [Fact]
    public async Task Handle_WriterThrowsOnResultOutcome_PropagatesUnchanged()
    {
        var writer = new ThrowingAuditTrailWriter();
        var behavior = CreateBehavior(writer);

        var act = async () => await behavior.Handle(new TestCommand("r-1"), () => Task.FromResult(Result<string>.Success("after")), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("writer exploded");
    }

    [Fact]
    public async Task Handle_HandlerThrows_AndWriterAlsoThrowsRecordingTheFault_OriginalExceptionStillPropagates_AndFailureIsLogged()
    {
        var writer = new ThrowingAuditTrailWriter();
        var logger = new FakeLogger<AuditingBehavior<TestCommand, Result<string>>>();
        var behavior = CreateBehavior(writer, logger);

        var act = async () => await behavior.Handle(new TestCommand("r-1"), () => throw new InvalidOperationException("handler boom"), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("handler boom");
        logger.Entries.Should().ContainSingle();
        var logEntry = logger.Entries[0];
        logEntry.Level.Should().Be(LogLevel.Error);
        logEntry.Exception.Should().BeOfType<InvalidOperationException>();
        logEntry.Exception!.Message.Should().Be("writer exploded");
    }

    private sealed class ThrowingAuditTrailWriter : IAuditTrailWriter
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
            => throw new InvalidOperationException("writer exploded");
    }
}
