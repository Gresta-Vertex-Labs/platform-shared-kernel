using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Streaming;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Application;
using SharedKernel.Testing.Idempotency;
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

    private sealed record CountStream(int Count) : IStreamQuery<int>;

    private sealed class CountStreamHandler : IStreamQueryHandler<CountStream, int>
    {
        public async IAsyncEnumerable<int> Handle(CountStream request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < request.Count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
    }

    /// <summary>
    /// A harness with a caller registered: this assembly declares a <c>[RequirePermission]</c> request
    /// (<see cref="AuthorizedCommand"/>), so the always-on authorization needs an <see cref="IRequestContext"/>
    /// at start when <see cref="ApplicationPipelineTestHarness.Build{TMarker}"/> scans it, even for tests that never
    /// send it.
    /// </summary>
    private static ApplicationPipelineTestHarness NewHarness()
    {
        var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext());
        return harness;
    }

    [Fact]
    public async Task SendAsync_ReturnsExpectedResponse_ThroughComposedBehaviors()
    {
        using var harness = NewHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new SucceedingCommand());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SendAsync_BuildOverTheMarkerAssembly_FindsTheHandlersWithoutRegistration()
    {
        using var harness = NewHarness();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new FailingResultCommand());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SendAsync_ReturnsFailureResponse_ThroughComposedBehaviors()
    {
        using var harness = NewHarness();
        harness.Services.AddSingleton<IRequestHandler<FailingResultCommand, Result>, FailingResultCommandHandler>();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new FailingResultCommand());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SendAsync_ThrownHandlerException_PropagatesUnchanged()
    {
        using var harness = NewHarness();
        harness.Services.AddSingleton<IRequestHandler<ThrowingCommand, Result>, ThrowingCommandHandler>();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.SendAsync(new ThrowingCommand()));

        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task WithActivityCapture_AddTracingBehavior_RecordsSpanTaggedWithRequestType()
    {
        using var harness = NewHarness().WithActivityCapture();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
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
        using var harness = NewHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        await harness.SendAsync(new SucceedingCommand());

        Assert.Contains(
            harness.CapturedMeasurements,
            measurement => measurement.InstrumentName == "sharedkernel.application.request.duration");
    }

    [Fact]
    public async Task SendAsync_BeforeBuild_ThrowsInvalidOperationException()
    {
        using var harness = NewHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.SendAsync(new SucceedingCommand()));
    }

    // ---- Build(): no mediator, handlers registered by the test ----

    [Fact]
    public async Task SendThroughPipelineAsync_WithoutAMediator_RunsTheBehaviorsAndTheHandler()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<FailingResultCommand, Result>, FailingResultCommandHandler>();
        harness.Build();

        var result = await harness.SendThroughPipelineAsync<FailingResultCommand, Result>(new FailingResultCommand());

        Assert.True(result.IsFailure);
        Assert.Contains(
            harness.CapturedMeasurements,
            measurement => measurement.InstrumentName == "sharedkernel.application.request.duration");
    }

    [Fact]
    public async Task SendAsync_AfterBuildWithoutAMediator_SendsThroughThePipeline()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.Build();

        var result = await harness.SendAsync(new SucceedingCommand());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SendAsync_AfterBuildWithoutAMediator_ForAnUnregisteredHandler_Throws()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.SendAsync(new SucceedingCommand()));
    }

    [Fact]
    public async Task CreateStream_AfterBuildWithoutAMediator_YieldsTheHandlersItems()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddTransient<IStreamQueryHandler<CountStream, int>, CountStreamHandler>();
        harness.Build();

        // The harness sends requests only; a stream is read through the ISender that Build() registered.
        using var provider = harness.Services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();
        var items = new List<int>();
        await foreach (var item in sender.CreateStream(new CountStream(3)))
            items.Add(item);

        Assert.Equal([0, 1, 2], items);
    }

    [Fact]
    public void Build_WithoutAMediator_WithIdempotencyButNoStore_FailsTheStartCheck()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext());

        var exception = Assert.Throws<OptionsValidationException>(
            () => harness.Configure(app => app.WithIdempotency()).Build());

        Assert.Contains(nameof(IIdempotencyStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_WithoutAMediator_WithTheFakeSeams_RunsTheOptedInBehaviors()
    {
        var handler = new IdempotentTestCommandHandler();
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddFakeApplicationBehaviorServices();
        harness.Services.AddSingleton<IRequestHandler<IdempotentTestCommand, Result>>(handler);
        harness.Configure(app => app.WithIdempotency().WithTransactions()).Build();

        await harness.SendAsync(new IdempotentTestCommand("key-1"));
        await harness.SendAsync(new IdempotentTestCommand("key-1"));

        Assert.Equal(1, handler.CallCount);
    }

    // ---- Authorization ----

    [RequirePermission("orders:create")]
    private sealed record AuthorizedCommand : ICommand;

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
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new AuthorizedCommand());

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized.default", result.Error.Code);
    }

    [Fact]
    public async Task SendAsync_AuthorizationBehavior_AuthenticatedWithoutPermission_ReturnsForbiddenFailure()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext());
        harness.Services.AddSingleton<IRequestHandler<AuthorizedCommand, Result>, AuthorizedCommandHandler>();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new AuthorizedCommand());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    [Fact]
    public void Build_RequirePermissionRequestWithoutRequestContext_FailsTheStartCheckNamingTheRequest()
    {
        using var harness = new ApplicationPipelineTestHarness();

        var exception = Assert.Throws<OptionsValidationException>(
            () => harness.Build<ApplicationPipelineTestHarnessTests>());

        Assert.Contains(nameof(IRequestContext), exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(AuthorizedCommand).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_AuthorizationBehavior_GrantedPermission_Succeeds()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext { Permissions = ["orders:create"] });
        harness.Services.AddSingleton<IRequestHandler<AuthorizedCommand, Result>, AuthorizedCommandHandler>();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new AuthorizedCommand());

        Assert.True(result.IsSuccess);
    }

    // ---- Idempotency ----

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
    /// Composes a harness with <c>IdempotencyBehavior</c> over <see cref="FakeIdempotencyStore"/> and
    /// <paramref name="caller"/>, which the test may mutate between sends to act as another caller.
    /// </summary>
    private static ApplicationPipelineTestHarness IdempotencyHarness(
        IdempotentTestCommandHandler handler,
        FakeRequestContext caller,
        FakeIdempotencyStore? store = null)
    {
        var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<IdempotentTestCommand, Result>>(handler);
        harness.Services.AddKeyedSingleton<IIdempotencyStore>(IdempotencyPurpose.Request, store ?? new FakeIdempotencyStore());
        harness.Services.AddSingleton<IRequestContext>(caller);
        harness.Configure(app => app.WithIdempotency());
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
        var caller = new FakeRequestContext { UserId = "alice", TenantId = new TenantId(Guid.NewGuid()) };
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
    /// Redis and EF Core stores, so <see cref="FakeIdempotencyStore.Calls"/> never shows the raw key.
    /// </summary>
    [Fact]
    public async Task SendAsync_IdempotencyBehavior_StoreReceivesTheScopedKey_NotTheRawKey()
    {
        var handler = new IdempotentTestCommandHandler();
        var store = new FakeIdempotencyStore();
        using var harness = IdempotencyHarness(handler, new FakeRequestContext(), store);

        await harness.SendAsync(new IdempotentTestCommand("order-42"));

        var key = Assert.Single(store.Calls, call => call.Member == nameof(FakeIdempotencyStore.TryBeginAsync)).Key;
        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.DoesNotContain("order-42", key, StringComparison.Ordinal);
    }

    [Fact]
    public void WithIdempotency_WithoutRequestContext_BuildFailsTheStartCheck()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddFakeIdempotencyStore(IdempotencyPurpose.Request);

        var exception = Assert.Throws<OptionsValidationException>(
            () => harness.Configure(app => app.WithIdempotency()).Build<ApplicationPipelineTestHarnessTests>());

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
