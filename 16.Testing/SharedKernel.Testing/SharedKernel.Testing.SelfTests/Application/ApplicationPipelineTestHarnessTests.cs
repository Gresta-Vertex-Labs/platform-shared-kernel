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

    /// <summary>
    /// Composes a harness with <c>IdempotencyBehavior</c> over <see cref="FakeRequestIdempotencyStore"/> and
    /// <paramref name="caller"/>, which the test may mutate between sends to act as another caller.
    /// </summary>
    private static ApplicationPipelineTestHarness IdempotencyHarness(
        IdempotentTestCommandHandler handler,
        FakeRequestContext caller,
        FakeRequestIdempotencyStore? store = null)
    {
        var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<IdempotentTestCommand, Result>>(handler);
        harness.Services.AddSingleton<IRequestIdempotencyStore>(store ?? new FakeRequestIdempotencyStore());
        harness.Services.AddSingleton<IRequestContext>(caller);
        harness.AddBehaviors().AddIdempotencyBehavior().Build();
        return harness.Build<ApplicationPipelineTestHarnessTests>();
    }

    [Fact]
    public async Task SendAsync_IdempotencyBehavior_DuplicateKey_ReplaysStoredResponse_HandlerRunsOnce()
    {
        var handler = new IdempotentTestCommandHandler();
        using var harness = IdempotencyHarness(handler, new FakeRequestContext());

        var first = await harness.SendAsync(new IdempotentTestCommand("key-1"));
        var second = await harness.SendAsync(new IdempotentTestCommand("key-1"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>
    /// Keys are reserved per tenant and caller: a second user sending the same key and body gets their own
    /// execution, never the first user's stored response. The first user's retry still replays.
    /// </summary>
    [Fact]
    public async Task SendAsync_IdempotencyBehavior_TwoUsersSameKeyAndBody_EachRunsTheHandler()
    {
        var handler = new IdempotentTestCommandHandler();
        var caller = new FakeRequestContext { UserId = "alice", TenantId = Guid.NewGuid() };
        using var harness = IdempotencyHarness(handler, caller);

        await harness.SendAsync(new IdempotentTestCommand("shared-key", "body"));
        caller.UserId = "bob";
        await harness.SendAsync(new IdempotentTestCommand("shared-key", "body"));
        caller.UserId = "alice";
        var aliceRetry = await harness.SendAsync(new IdempotentTestCommand("shared-key", "body"));

        Assert.True(aliceRetry.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    /// <summary>
    /// The documented residual risk: anonymous callers of one tenant share one scope, where only the fingerprint
    /// separates them, so the same key with the same body replays.
    /// </summary>
    [Fact]
    public async Task SendAsync_IdempotencyBehavior_AnonymousCallersSameKeyAndBody_Replay()
    {
        var handler = new IdempotentTestCommandHandler();
        using var harness = IdempotencyHarness(handler, new FakeRequestContext { IsAuthenticated = false, UserId = null });

        await harness.SendAsync(new IdempotentTestCommand("anonymous-key", "body"));
        var second = await harness.SendAsync(new IdempotentTestCommand("anonymous-key", "body"));

        Assert.True(second.IsSuccess);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_IdempotencyBehavior_AnonymousCallersSameKeyDifferentBody_ReturnsKeyReused()
    {
        var handler = new IdempotentTestCommandHandler();
        using var harness = IdempotencyHarness(handler, new FakeRequestContext { IsAuthenticated = false, UserId = null });

        await harness.SendAsync(new IdempotentTestCommand("anonymous-key", "a"));
        var second = await harness.SendAsync(new IdempotentTestCommand("anonymous-key", "b"));

        Assert.True(second.IsFailure);
        Assert.Equal("idempotency.key_reused", second.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_IdempotencyBehavior_EmptyKey_ReturnsKeyRequired()
    {
        var handler = new IdempotentTestCommandHandler();
        using var harness = IdempotencyHarness(handler, new FakeRequestContext());

        var result = await harness.SendAsync(new IdempotentTestCommand(" "));

        Assert.True(result.IsFailure);
        Assert.Equal("idempotency.key_required", result.Error.Code);
        Assert.Equal(0, handler.CallCount);
    }

    /// <summary>
    /// The fake stores the key the behavior hands over — already scoped to the tenant and caller — exactly like the
    /// Redis and EF Core stores, so <see cref="FakeRequestIdempotencyStore.Calls"/> never shows the raw key.
    /// </summary>
    [Fact]
    public async Task SendAsync_IdempotencyBehavior_StoreReceivesTheScopedKey_NotTheRawKey()
    {
        var handler = new IdempotentTestCommandHandler();
        var store = new FakeRequestIdempotencyStore();
        using var harness = IdempotencyHarness(handler, new FakeRequestContext(), store);

        await harness.SendAsync(new IdempotentTestCommand("order-42"));

        var key = Assert.Single(store.Calls, call => call.Member == nameof(FakeRequestIdempotencyStore.TryBeginAsync)).Key;
        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.DoesNotContain("order-42", key, StringComparison.Ordinal);
    }

    [Fact]
    public void AddIdempotencyBehavior_WithoutRequestContext_BuildThrows()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestIdempotencyStore, FakeRequestIdempotencyStore>();

        var exception = Assert.Throws<InvalidOperationException>(() => harness.AddBehaviors().AddIdempotencyBehavior().Build());

        Assert.Contains(nameof(IRequestContext), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_IdempotencyBehavior_SameKeyDifferentPayload_ReturnsConflict()
    {
        var handler = new IdempotentTestCommandHandler();
        using var harness = IdempotencyHarness(handler, new FakeRequestContext());

        await harness.SendAsync(new IdempotentTestCommand("key-1", "a"));
        var second = await harness.SendAsync(new IdempotentTestCommand("key-1", "b"));

        Assert.True(second.IsFailure);
        Assert.Equal("idempotency.key_reused", second.Error.Code);
        Assert.Equal(1, handler.CallCount);
    }
}
