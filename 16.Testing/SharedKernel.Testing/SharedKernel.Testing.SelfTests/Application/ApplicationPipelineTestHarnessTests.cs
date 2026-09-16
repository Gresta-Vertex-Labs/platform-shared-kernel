using System.Linq;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Context;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Application;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class ApplicationPipelineTestHarnessTests
{
    private sealed record SucceedingCommand : ICommand;

    private sealed class SucceedingCommandHandler : IRequestHandler<SucceedingCommand, Result>
    {
        public Task<Result> Handle(SucceedingCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed record FailingResultCommand : ICommand;

    private sealed class FailingResultCommandHandler : IRequestHandler<FailingResultCommand, Result>
    {
        public Task<Result> Handle(FailingResultCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure(Error.Validation("x", "bad")));
    }

    private sealed record ThrowingCommand : ICommand;

    private sealed class ThrowingCommandHandler : IRequestHandler<ThrowingCommand, Result>
    {
        public Task<Result> Handle(ThrowingCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task SendAsync_ReturnsExpectedResponse_ThroughComposedBehaviors()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.AddBehaviors().AddLoggingBehavior().AddMetricsBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new SucceedingCommand());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SendAsync_ReturnsFailureResponse_ThroughComposedBehaviors()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<FailingResultCommand, Result>, FailingResultCommandHandler>();
        harness.AddBehaviors().AddLoggingBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new FailingResultCommand());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SendAsync_ThrownHandlerException_PropagatesUnchanged()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<ThrowingCommand, Result>, ThrowingCommandHandler>();
        harness.AddBehaviors().AddLoggingBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.SendAsync(new ThrowingCommand()));

        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task WithActivityCapture_AddTracingBehavior_RecordsSpanTaggedWithRequestType()
    {
        using var harness = new ApplicationPipelineTestHarness().WithActivityCapture();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.AddBehaviors().AddTracingBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        await harness.SendAsync(new SucceedingCommand());

        var expectedTag = typeof(SucceedingCommand).FullName ?? nameof(SucceedingCommand);
        var spans = harness.CapturedActivities
            .Where(a => Equals(a.GetTagItem("request.type"), expectedTag))
            .ToList();
        Assert.Single(spans);
    }

    [Fact]
    public async Task CapturedMeasurements_AddMetricsBehavior_RecordsRequestDurationEntry()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.AddBehaviors().AddMetricsBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        await harness.SendAsync(new SucceedingCommand());

        Assert.Contains(
            harness.CapturedMeasurements,
            measurement => measurement.InstrumentName == "sharedkernel.application.request.duration");
    }

    [Fact]
    public async Task SendAsync_BeforeBuild_ThrowsInvalidOperationException()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.AddBehaviors().Build();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.SendAsync(new SucceedingCommand()));
    }

    private sealed record AuthorizedCommand(string Permission) : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> RequiredPermissions => [Permission];
    }

    private sealed class AuthorizedCommandHandler : IRequestHandler<AuthorizedCommand, Result>
    {
        public Task<Result> Handle(AuthorizedCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    [Fact]
    public async Task SendAsync_AuthorizationBehavior_UnauthenticatedRequestContext_ReturnsUnauthenticatedFailure()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext { IsAuthenticated = false });
        harness.Services.AddSingleton<IRequestHandler<AuthorizedCommand, Result>, AuthorizedCommandHandler>();
        harness.AddBehaviors().AddAuthorizationBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new AuthorizedCommand("orders:create"));

        Assert.True(result.IsFailure);
        Assert.Equal("authorization.unauthenticated", result.Error.Code);
    }

    [Fact]
    public async Task SendAsync_AuthorizationBehavior_AuthenticatedWithoutPermission_ReturnsForbiddenFailure()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext());
        harness.Services.AddSingleton<IRequestHandler<AuthorizedCommand, Result>, AuthorizedCommandHandler>();
        harness.AddBehaviors().AddAuthorizationBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new AuthorizedCommand("orders:create"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    [Fact]
    public async Task SendAsync_AuthorizationBehavior_GrantedPermission_Succeeds()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext { Permissions = ["orders:create"] });
        harness.Services.AddSingleton<IRequestHandler<AuthorizedCommand, Result>, AuthorizedCommandHandler>();
        harness.AddBehaviors().AddAuthorizationBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new AuthorizedCommand("orders:create"));

        Assert.True(result.IsSuccess);
    }

    private sealed record IdempotentTestCommand(string IdempotencyKey, string Payload = "") : ICommand, IIdempotentRequest;

    private sealed class IdempotentTestCommandHandler : IRequestHandler<IdempotentTestCommand, Result>
    {
        public int CallCount { get; private set; }

        public Task<Result> Handle(IdempotentTestCommand request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task SendAsync_IdempotencyBehavior_DuplicateKey_ReplaysStoredResponse_HandlerRunsOnce()
    {
        using var harness = new ApplicationPipelineTestHarness();
        var handler = new IdempotentTestCommandHandler();
        harness.Services.AddSingleton<IRequestHandler<IdempotentTestCommand, Result>>(handler);
        harness.Services.AddSingleton<IRequestIdempotencyStore, FakeRequestIdempotencyStore>();
        harness.AddBehaviors().AddIdempotencyBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var first = await harness.SendAsync(new IdempotentTestCommand("key-1"));
        var second = await harness.SendAsync(new IdempotentTestCommand("key-1"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_IdempotencyBehavior_SameKeyDifferentPayload_ReturnsConflict()
    {
        using var harness = new ApplicationPipelineTestHarness();
        var handler = new IdempotentTestCommandHandler();
        harness.Services.AddSingleton<IRequestHandler<IdempotentTestCommand, Result>>(handler);
        harness.Services.AddSingleton<IRequestIdempotencyStore, FakeRequestIdempotencyStore>();
        harness.AddBehaviors().AddIdempotencyBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        await harness.SendAsync(new IdempotentTestCommand("key-1", "a"));
        var second = await harness.SendAsync(new IdempotentTestCommand("key-1", "b"));

        Assert.True(second.IsFailure);
        Assert.Equal("idempotency.key_reused", second.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }
}
