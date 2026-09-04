using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.DualApproval;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Auditing;

/// <summary>
/// Verifies <see cref="AuditingBehavior{TRequest,TResponse}"/>'s always-record-on-normal-return
/// semantics (both success and business failure), the dual-approval linkage, the
/// never-records-on-thrown-exception rule, fail-closed propagation from
/// <see cref="IAuditTrailWriter.RecordAsync"/>, and query exclusion (WO-071, T-75).
/// </summary>
public sealed class AuditingBehaviorTests
{
    private sealed record TestCommand(bool ShouldFail)
        : ICommand, IAuditableRequest<Result>
    {
        public string Action => "test.action";
        public string ResourceType => "TestResource";
        public string ResourceId => "resource-1";
        public string? BeforeSnapshot => "before";
        public string? GetAfterSnapshot(Result response) => response.IsSuccess ? "after-success" : "after-failure";
    }

    private sealed record TestCommandWithApproval(string ApprovalKey)
        : ICommand, IAuditableRequest<Result>, IRequiresDualApproval
    {
        public string Action => "test.approved-action";
        public string ResourceType => "TestResource";
        public string ResourceId => "resource-2";
        public string? BeforeSnapshot => null;
        public string? GetAfterSnapshot(Result response) => "after";
    }

    private sealed record TestQuery : IQuery<string>;

    private sealed class TestCommandHandler : IRequestHandler<TestCommand, Result>
    {
        public int InvocationCount { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            InvocationCount++;

            if (ShouldThrow)
                throw new InvalidOperationException("handler blew up");

            return Task.FromResult(request.ShouldFail
                ? Result.Failure(SharedKernel.Primitives.Errors.Error.Conflict("test.conflict", "conflict"))
                : Result.Success());
        }
    }

    private sealed class TestCommandWithApprovalHandler : IRequestHandler<TestCommandWithApproval, Result>
    {
        public Task<Result> Handle(TestCommandWithApproval request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private static ServiceProvider BuildProvider(IAuditTrailWriter writer, TestCommandHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(writer);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestCommand, Result>>(sp => sp.GetRequiredService<TestCommandHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<AuditingBehaviorTests>());

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildApprovalProvider(IAuditTrailWriter writer, TestCommandWithApprovalHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(writer);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestCommandWithApproval, Result>>(
            sp => sp.GetRequiredService<TestCommandWithApprovalHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<AuditingBehaviorTests>());

        return services.BuildServiceProvider();
    }

    // ---- (a): a command implementing IAuditableRequest<TResponse> alone (no dual-approval) calls
    // next() then calls RecordAsync exactly once with ApprovalId = null, for both success and failure ----

    [Fact]
    public async Task Handle_SuccessfulCommand_CallsNextThenRecordsExactlyOnceWithNullApprovalId()
    {
        var writer = Substitute.For<IAuditTrailWriter>();
        var handler = new TestCommandHandler();
        var provider = BuildProvider(writer, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand(ShouldFail: false));

        result.IsSuccess.Should().BeTrue();
        handler.InvocationCount.Should().Be(1);
        await writer.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "test.action" &&
                e.ResourceType == "TestResource" &&
                e.ResourceId == "resource-1" &&
                e.BeforeSnapshot == "before" &&
                e.AfterSnapshot == "after-success" &&
                e.ApprovalId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BusinessFailureCommand_StillCallsRecordAsyncExactlyOnce()
    {
        // A rejected high-risk attempt is itself often the compliance-relevant event — never only
        // record a successful outcome.
        var writer = Substitute.For<IAuditTrailWriter>();
        var handler = new TestCommandHandler();
        var provider = BuildProvider(writer, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand(ShouldFail: true));

        result.IsFailure.Should().BeTrue();
        await writer.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.AfterSnapshot == "after-failure" && e.ApprovalId == null),
            Arg.Any<CancellationToken>());
    }

    // ---- (b): a command additionally implementing IRequiresDualApproval produces a recorded entry
    // whose ApprovalId equals ApprovalKey ----

    [Fact]
    public async Task Handle_CommandAlsoRequiringDualApproval_RecordsApprovalIdEqualToApprovalKey()
    {
        var writer = Substitute.For<IAuditTrailWriter>();
        var handler = new TestCommandWithApprovalHandler();
        var provider = BuildApprovalProvider(writer, handler);
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new TestCommandWithApproval("approval-key-1"));

        await writer.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.ApprovalId == "approval-key-1"),
            Arg.Any<CancellationToken>());
    }

    // ---- (c): a command NOT implementing IAuditableRequest<TResponse> never resolves
    // AuditingBehavior into its pipeline at all ----

    [Fact]
    public void AuditingBehavior_DoesNotResolveIntoPipeline_ForQueryType()
    {
        // TRequest : ICommandBase, IAuditableRequest<TResponse>, IRequest<TResponse> — TestQuery
        // never implements ICommandBase or IAuditableRequest<TResponse>, so
        // AuditingBehavior<TestQuery, string> cannot be constructed: a DI-level fact, not a runtime
        // branch. Mirrors DualApprovalBehaviorTests' equivalent query-exclusion assertion.
        typeof(TestQuery).Should().NotBeAssignableTo<ICommandBase>();

        var closesOverQuery = () => typeof(AuditingBehavior<,>)
            .MakeGenericType(typeof(TestQuery), typeof(string));

        closesOverQuery.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AuditingBehavior_NotRegistered_SpyWriterReceivesZeroCalls_ForNonAuditedCommand()
    {
        // A real DI-contract proof: a command not opted into IAuditableRequest<TResponse> never
        // triggers a call to IAuditTrailWriter.RecordAsync, since AuditingBehavior<,> is never
        // resolved into a pipeline it does not close over.
        var writer = Substitute.For<IAuditTrailWriter>();
        var services = new ServiceCollection();
        services.AddSingleton(writer);
        services.AddSingleton<IRequestHandler<PlainCommand, Result>, PlainCommandHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<AuditingBehaviorTests>());
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new PlainCommand());

        await writer.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    private sealed record PlainCommand : ICommand;

    private sealed class PlainCommandHandler : IRequestHandler<PlainCommand, Result>
    {
        public Task<Result> Handle(PlainCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    // ---- (d): IAuditTrailWriter.RecordAsync is never called when next() throws ----

    [Fact]
    public async Task Handle_HandlerThrows_NeverCallsRecordAsync()
    {
        var writer = Substitute.For<IAuditTrailWriter>();
        var handler = new TestCommandHandler { ShouldThrow = true };
        var provider = BuildProvider(writer, handler);
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand(ShouldFail: false));

        await act.Should().ThrowAsync<InvalidOperationException>();
        await writer.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    // ---- (e): an exception thrown by RecordAsync itself propagates unchanged, never caught/swallowed ----

    [Fact]
    public async Task Handle_RecordAsyncThrows_ExceptionPropagatesUnchanged()
    {
        var writer = Substitute.For<IAuditTrailWriter>();
        writer.RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("audit store unavailable"));
        var handler = new TestCommandHandler();
        var provider = BuildProvider(writer, handler);
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand(ShouldFail: false));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("audit store unavailable");
        handler.InvocationCount.Should().Be(1, "the handler itself DID run — only the audit write failed");
    }
}
