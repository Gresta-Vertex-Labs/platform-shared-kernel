using FluentAssertions;
using SharedKernel.Application.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Authoring;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Tests.Authoring;

/// <summary>
/// T-04 — the swallow-a-<see cref="Result.Failure"/> test, for every shipped activity base. A
/// <see cref="CommandActivity{TCommand}"/>/<see cref="CommandActivity{TCommand, TResult}"/> over an
/// <see cref="ISender"/> substitute returning a failed <see cref="Result"/>/<see cref="Result{T}"/>
/// must <b>throw</b> rather than return normally — a happy-path-only test would not catch this
/// domain's most damaging bug shape (a workflow proceeding with a value that was never produced).
/// </summary>
public sealed class CommandActivitySwallowFailureTests
{
    private sealed record SampleCommand(string Value) : ICommand;

    private sealed record SampleQueryCommand(string Value) : ICommand<string>;

    private sealed class SampleCommandActivity(ISender sender) : CommandActivity<SampleCommand>(
        sender, NullLogger.Instance, new FakeClock());

    private sealed class SampleQueryCommandActivity(ISender sender) : CommandActivity<SampleQueryCommand, string>(
        sender, NullLogger.Instance, new FakeClock());

    // A minimal, deterministic, no-real-time IClock — matches SharedKernel.Testing's FakeClock shape
    // (this test class avoids taking a ProjectReference to 16.Testing purely to keep this file
    // self-contained; the fixed instant convention is the same one FakeClock documents).
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.DateTime);
    }

    [Fact]
    public async Task CommandActivity_VoidResult_SenderReturnsFailure_Throws()
    {
        ISender sender = Substitute.For<ISender>();
        Error error = Error.Conflict("wf.test.conflict", "duplicate");
        sender.Send(Arg.Any<SampleCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(error)));

        var activity = new SampleCommandActivity(sender);

        Func<Task> act = () => activity.ExecuteAsync(new SampleCommand("x"));

        (await act.Should().ThrowAsync<ApplicationFailureException>())
            .Which.ErrorType.Should().Be(error.Code);
    }

    [Fact]
    public async Task CommandActivity_VoidResult_SenderReturnsSuccess_DoesNotThrow()
    {
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<SampleCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var activity = new SampleCommandActivity(sender);

        Func<Task> act = () => activity.ExecuteAsync(new SampleCommand("x"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CommandActivity_ValueResult_SenderReturnsFailure_Throws()
    {
        ISender sender = Substitute.For<ISender>();
        Error error = Error.NotFound("wf.test.not_found", "missing");
        sender.Send(Arg.Any<SampleQueryCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Failure(error)));

        var activity = new SampleQueryCommandActivity(sender);

        Func<Task> act = () => activity.ExecuteAsync(new SampleQueryCommand("x"));

        (await act.Should().ThrowAsync<ApplicationFailureException>())
            .Which.ErrorType.Should().Be(error.Code);
    }

    [Fact]
    public async Task CommandActivity_ValueResult_SenderReturnsSuccess_ReturnsUnwrappedValue()
    {
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<SampleQueryCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Success("the-value")));

        var activity = new SampleQueryCommandActivity(sender);

        string result = await activity.ExecuteAsync(new SampleQueryCommand("x"));

        result.Should().Be("the-value");
    }
}
