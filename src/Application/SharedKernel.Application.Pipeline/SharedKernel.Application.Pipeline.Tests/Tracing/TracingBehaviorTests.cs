using FluentAssertions;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Tests.Support;
using SharedKernel.Application.Pipeline.Tracing;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Application.Pipeline.Tests.Tracing;

/// <summary>The request span, recorded through the shared recorder helpers.</summary>
public sealed class TracingBehaviorTests
{
    private const string SecretMessage = "sentinel-exception-message-7f3a";

    private sealed record TracingProbeCommand : ICommand;

    private sealed record TracingProbeQuery : IQuery<string>;

    public static TheoryData<ErrorType> AllErrorTypes()
    {
        var data = new TheoryData<ErrorType>();
        foreach (var type in Enum.GetValues<ErrorType>().Where(static t => t != ErrorType.None))
            data.Add(type);
        return data;
    }

    private static async Task<System.Diagnostics.Activity> RunAsync<TRequest, TResponse>(
        TelemetryProbe probe,
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        var behavior = new TracingBehavior<TRequest, TResponse>();
        try
        {
            await behavior.Handle(request, () => next(), cancellationToken);
        }
        catch (Exception)
        {
            // The exception paths assert the thrown exception themselves.
        }

        return probe.Spans.Single(span => span.OperationName == ApplicationSpanNames.Send);
    }

    [Fact]
    public async Task Handle_Success_RecordsTheSendSpanWithTheRequestTags()
    {
        using var probe = new TelemetryProbe();

        var span = await RunAsync(probe, new TracingProbeCommand(), () => Task.FromResult(Result.Success()));

        span.OperationName.Should().Be("application send");
        span.ShouldHaveKind(System.Diagnostics.ActivityKind.Internal)
            .ShouldHaveSource(ApplicationDiagnostics.ActivitySourceName)
            .ShouldHaveTag("application.request_type", typeof(TracingProbeCommand).FullName)
            .ShouldHaveTag("application.request_kind", "command")
            .ShouldHaveOutcome(WellKnownOutcomes.Success)
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention();
    }

    [Fact]
    public async Task Handle_Query_TagsTheKindQuery()
    {
        using var probe = new TelemetryProbe();

        var span = await RunAsync(probe, new TracingProbeQuery(), () => Task.FromResult(Result<string>.Success("v")));

        span.ShouldHaveTag("application.request_kind", "query");
    }

    [Theory]
    [MemberData(nameof(AllErrorTypes))]
    public async Task Handle_ResultFailure_IsRecordedForEveryErrorType(ErrorType type)
    {
        using var probe = new TelemetryProbe();
        var error = new Error("test.failure", "a failure message", type);

        var span = await RunAsync(probe, new TracingProbeCommand(), () => Task.FromResult(Result.Failure(error)));

        span.ShouldBeRecordedFailure(error).ShouldHaveOutcome(WellKnownOutcomes.Failure).ShouldFollowSpanConvention();
    }

    [Fact]
    public async Task Handle_ThrownException_RecordsTheFullNameAsDescriptionNeverTheMessage()
    {
        using var probe = new TelemetryProbe();

        var span = await RunAsync<TracingProbeCommand, Result>(
            probe,
            new TracingProbeCommand(),
            () => throw new InvalidOperationException(SecretMessage));

        span.ShouldBeRecordedException(typeof(InvalidOperationException))
            .ShouldHaveOutcome(WellKnownOutcomes.Exception);
        span.StatusDescription.Should().Be(typeof(InvalidOperationException).FullName).And.NotContain(SecretMessage);
    }

    [Fact]
    public async Task Handle_ThrownException_IsRethrownUnchanged()
    {
        using var probe = new TelemetryProbe();
        var thrown = new InvalidOperationException("boom");
        var behavior = new TracingBehavior<TracingProbeCommand, Result>();

        var act = async () => await behavior.Handle(new TracingProbeCommand(), () => throw thrown, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(thrown);
    }

    [Fact]
    public async Task Handle_ThrownKernelException_RecordsItsErrorCode()
    {
        using var probe = new TelemetryProbe();
        var error = Error.Conflict("test.conflict", "conflict");

        var span = await RunAsync<TracingProbeCommand, Result>(
            probe,
            new TracingProbeCommand(),
            () => throw error.ToException());

        span.ShouldBeRecordedException(error.ToException().GetType(), "test.conflict");
    }

    [Fact]
    public async Task Handle_Cancellation_IsNotAnErrorAndHasTheCanceledOutcome()
    {
        using var probe = new TelemetryProbe();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var span = await RunAsync<TracingProbeCommand, Result>(
            probe,
            new TracingProbeCommand(),
            () => throw new OperationCanceledException(cts.Token),
            cts.Token);

        span.ShouldBeRecordedCancellation().ShouldHaveOutcome(WellKnownOutcomes.Canceled);
    }
}
