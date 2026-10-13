using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Commands;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Auditing;
using SharedKernel.Application.Pipeline.Authorization;
using SharedKernel.Application.Pipeline.Commands;
using SharedKernel.Application.Pipeline.Diagnostics;
using SharedKernel.Application.Pipeline.Idempotency;
using SharedKernel.Application.Pipeline.Shared;
using SharedKernel.Application.Pipeline.Tests.Support;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Application.Pipeline.Tests.Conformance;

/// <summary>
/// Pins the exception rules (E1-E4) and the log rules (L1-L8) of the pipeline: event ids, the undefined-status
/// fail-closed arm, the documented catch-alls, and the message shape of the rewritten exceptions.
/// </summary>
public sealed class ErrorAndLogConformanceTests
{
    private sealed record IdempotentCommand(string IdempotencyKey) : ICommand<string>, IIdempotentRequest;

    private sealed record AuditedCommand(string ResourceId) : ICommand<string>, IAuditableRequest<Result<string>>
    {
        public string Action => "test.action";
        public string ResourceType => "TestResource";
        public string? BeforeSnapshot => null;

        public string? GetAfterSnapshot(Result<string> response) => response.IsSuccess ? response.Value : null;
    }

    private sealed record PlainCommand : ICommand;

    /// <summary>A store that reports a reservation status no member of the enum defines.</summary>
    private sealed class UndefinedStatusStore : IIdempotencyStore
    {
        public Task<IdempotencyReservation> TryBeginAsync(
            IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken cancellationToken)
            => Task.FromResult(new IdempotencyReservation((IdempotencyReservationStatus)99, null, null));

        public Task<bool> CompleteAsync(
            IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    [Fact]
    public void EventIds_AreLiteralAndInsideSubBlockOne()
    {
        PipelineLoggingEventIds.Base.Should().Be(5100);
        PipelineLoggingEventIds.Handling.Should().Be(5100);
        PipelineLoggingEventIds.HandledSuccess.Should().Be(5101);
        PipelineLoggingEventIds.HandledSlow.Should().Be(5102);
        PipelineLoggingEventIds.HandledFailure.Should().Be(5103);
        PipelineLoggingEventIds.HandlingThrew.Should().Be(5104);
        PipelineLoggingEventIds.HandlingCanceled.Should().Be(5105);
        PipelineLoggingEventIds.CommandCallbackFailed.Should().Be(5110);
        PipelineLoggingEventIds.IdempotencyReservationLost.Should().Be(5120);
        PipelineLoggingEventIds.IdempotencyStoreUnavailable.Should().Be(5121);
        PipelineLoggingEventIds.IdempotencyCompleteFailed.Should().Be(5122);
        PipelineLoggingEventIds.IdempotencyReleaseFailed.Should().Be(5123);
        PipelineLoggingEventIds.AuditWriteFailedDuringException.Should().Be(5130);
        PipelineLoggingEventIds.StreamCompleted.Should().Be(5140);
        PipelineLoggingEventIds.StreamCanceled.Should().Be(5141);
        PipelineLoggingEventIds.StreamFailedClassified.Should().Be(5142);
        PipelineLoggingEventIds.StreamFailed.Should().Be(5143);
        PipelineLoggingEventIds.DomainEventHandlerFailed.Should().Be(5150);
        PipelineLoggingEventIds.TransactionRolledBackByResult.Should().Be(5160);
        PipelineLoggingEventIds.TransactionRolledBackByException.Should().Be(5161);
        PipelineLoggingEventIds.TransactionReplayed.Should().Be(5162);

        var ids = typeof(PipelineLoggingEventIds)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => field.IsLiteral && field.FieldType == typeof(int) && field.Name != nameof(PipelineLoggingEventIds.Base))
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToList();
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Application, 1));
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Idempotency_UndefinedReservationStatus_FailsClosedAndNeverRunsTheHandler()
    {
        var behavior = new IdempotencyBehavior<IdempotentCommand, Result<string>>(
            new UndefinedStatusStore(),
            new FakeRequestContext(isAuthenticated: true) { UserId = "user-1" },
            new FakeCommandScope(),
            MsOptions.Create(new IdempotencyBehaviorOptions()),
            new InMemoryLogger<IdempotencyBehavior<IdempotentCommand, Result<string>>>());
        var handlerRan = false;

        var act = async () => await behavior.Handle(
            new IdempotentCommand("key-1"),
            () =>
            {
                handlerRan = true;
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<UnreachableException>();
        thrown.Which.Message.Should().EndWith(".").And.Contain("'99'");
        handlerRan.Should().BeFalse();
    }

    [Fact]
    public async Task Auditing_AuditWriteFailsAfterAHandlerFault_LogsWithTheExceptionLastAndKeepsTheOriginal()
    {
        using var probe = new TelemetryProbe();
        var writerFault = new InvalidOperationException("writer down");
        var writer = new FakeAuditTrailWriter { FailWith = writerFault };
        var logger = new InMemoryLogger<AuditingBehavior<AuditedCommand, Result<string>>>();
        var behavior = new AuditingBehavior<AuditedCommand, Result<string>>(writer, logger);
        var original = new InvalidOperationException("handler fault");

        var act = async () => await behavior.Handle(new AuditedCommand("r-1"), () => throw original, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(original);
        var record = logger.Records.ShouldHaveLogged(new EventId(5130), LogLevel.Error);
        record.Exception.Should().BeSameAs(writerFault);
        record.Message.Should().NotContain("writer down");
        logger.Records.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Application, 1);
    }

    [Fact]
    public async Task Auditing_AuditWriteFailsAfterAHandlerFault_AddsTheExceptionEventToTheCurrentSpanWithoutSettingTheStatus()
    {
        using var probe = new TelemetryProbe();
        var writer = new FakeAuditTrailWriter { FailWith = new InvalidOperationException("writer down") };
        var behavior = new AuditingBehavior<AuditedCommand, Result<string>>(
            writer, new InMemoryLogger<AuditingBehavior<AuditedCommand, Result<string>>>());

        var act = async () => await behavior.Handle(
            new AuditedCommand("r-1"), () => throw new InvalidOperationException("handler fault"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        probe.Root.ShouldHaveEvent("exception");
        probe.Root.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task Auditing_CancellationDuringTheFaultWrite_Propagates()
    {
        var writer = new FakeAuditTrailWriter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        writer.FailWith = new OperationCanceledException(cts.Token);
        var logger = new InMemoryLogger<AuditingBehavior<AuditedCommand, Result<string>>>();
        var behavior = new AuditingBehavior<AuditedCommand, Result<string>>(writer, logger);

        var act = async () => await behavior.Handle(
            new AuditedCommand("r-1"), () => throw new InvalidOperationException("handler fault"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Records.ShouldNotHaveLogged(new EventId(5130));
    }

    [Fact]
    public async Task CommandScope_CallbackThrows_LogsWithTheExceptionAddsAnEventAndKeepsTheResponse()
    {
        using var probe = new TelemetryProbe();
        var scope = new CommandScope();
        var logger = new InMemoryLogger<CommandScopeBehavior<PlainCommand, Result>>();
        var behavior = new CommandScopeBehavior<PlainCommand, Result>(scope, logger);
        var fault = new InvalidOperationException("callback down");

        var result = await behavior.Handle(
            new PlainCommand(),
            () =>
            {
                scope.OnCompleted(_ => throw fault);
                return Task.FromResult(Result.Success());
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var record = logger.Records.ShouldHaveLogged(new EventId(5110), LogLevel.Error);
        record.Exception.Should().BeSameAs(fault);
        record.Message.Should().NotContain("callback down");
        probe.Root.ShouldHaveEvent("exception");
        probe.Root.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task CommandScope_CancellationInACallback_Propagates()
    {
        var scope = new CommandScope();
        var logger = new InMemoryLogger<CommandScopeBehavior<PlainCommand, Result>>();
        var behavior = new CommandScopeBehavior<PlainCommand, Result>(scope, logger);
        using var cts = new CancellationTokenSource();

        var act = async () => await behavior.Handle(
            new PlainCommand(),
            async () =>
            {
                scope.OnCompleted(token => Task.FromCanceled(token));
                await cts.CancelAsync();
                return Result.Success();
            },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Records.ShouldNotHaveLogged(new EventId(5110));
    }

    [Fact]
    public async Task Idempotency_CompleteFaults_AddsTheExceptionEventAndReturnsTheResponse()
    {
        using var probe = new TelemetryProbe();
        var store = new FakeIdempotencyStore { CompleteFault = new InvalidOperationException("store down") };
        var logger = new InMemoryLogger<IdempotencyBehavior<IdempotentCommand, Result<string>>>();
        var behavior = new IdempotencyBehavior<IdempotentCommand, Result<string>>(
            store,
            new FakeRequestContext(isAuthenticated: true) { UserId = "user-1" },
            new FakeCommandScope(),
            MsOptions.Create(new IdempotencyBehaviorOptions()),
            logger);

        var result = await behavior.Handle(
            new IdempotentCommand("key-1"), () => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        logger.Records.ShouldHaveLogged(new EventId(5122), LogLevel.Error);
        probe.Root.ShouldHaveEvent("exception");
        probe.Root.Status.Should().Be(ActivityStatusCode.Unset);
        logger.Records.ShouldNotLeak("key-1");
    }

    [Fact]
    public void FailureResponse_ForAResponseThatIsNotAResult_ThrowsASentenceWithAQuotedType()
    {
        var act = () => FailureResponse.Create<string>(Error.Validation("x.y", "z"));

        // The cached factory builds in a static initializer, so the sentence arrives as the inner exception.
        var message = act.Should().Throw<TypeInitializationException>().Which.InnerException!.Message;
        message.Should().EndWith(".").And.Contain("'System.String'");
    }

    [Fact]
    public void MissingRequestContext_IsASentenceWithTheQuotedRequestAndTheRemedy()
    {
        var message = PermissionCheck.MissingRequestContextMessage(typeof(PlainCommand));

        message.Should().StartWith("The request '").And.EndWith(".");
        message.Should().Contain(typeof(PlainCommand).FullName!).And.Contain("AddSharedKernelRequestContext()");
        message.Should().NotContain(";");
    }

    [Fact]
    public void ApplicationErrors_UseTheKernelSharedCodesAndFixedMessages()
    {
        ApplicationErrors.IdempotencyKeyRequired().Should().Be(
            Error.Validation(ErrorCodes.Idempotency.KeyRequired, "An idempotency key is required."));
        ApplicationErrors.IdempotencyInProgress().Type.Should().Be(ErrorType.Conflict);
        ApplicationErrors.IdempotencyKeyReused().Code.Should().Be(ErrorCodes.Idempotency.KeyReused);
        ApplicationErrors.IdempotencyStoreUnavailable().Type.Should().Be(ErrorType.Unavailable);
        ApplicationErrors.Unauthenticated().Type.Should().Be(ErrorType.Unauthorized);
        ApplicationErrors.InsufficientPermission().Code.Should().Be(ErrorCodes.Forbidden.InsufficientPermission);
    }
}
