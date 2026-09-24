using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Commands;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Tests.Support;
using SharedKernel.Application.Context;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Idempotency;

public sealed class IdempotencyBehaviorTests
{
    private sealed record TestCommand(string IdempotencyKey, string Payload) : ICommand<string>, IIdempotentRequest;

    private static FakeRequestContext User(string userId, Guid? tenantId = null) =>
        new(isAuthenticated: true) { UserId = userId, TenantId = tenantId };

    private static FakeRequestContext Anonymous(Guid? tenantId = null) =>
        new(isAuthenticated: false) { TenantId = tenantId };

    private static IdempotencyBehavior<TestCommand, Result<string>> CreateBehavior(
        IRequestIdempotencyStore store,
        IRequestContext? caller = null,
        ICommandScope? scope = null,
        ILogger<IdempotencyBehavior<TestCommand, Result<string>>>? logger = null)
        => new(
            store,
            caller ?? User("user-1"),
            scope ?? new FakeCommandScope(),
            logger ?? new FakeLogger<IdempotencyBehavior<TestCommand, Result<string>>>());

    private static Task<Result<string>> Send(
        IRequestIdempotencyStore store,
        IRequestContext caller,
        TestCommand request,
        Func<Task<Result<string>>> handler)
        => CreateBehavior(store, caller).Handle(request, () => handler(), CancellationToken.None);

    /// <summary>Wraps a <see cref="FakeIdempotencyStore"/> and forces <see cref="CompleteAsync"/> to report a lost reservation.</summary>
    private sealed class LostReservationOnCompleteStore(IRequestIdempotencyStore inner) : IRequestIdempotencyStore
    {
        public Task<IdempotencyBeginResult> TryBeginAsync(string key, string requestFingerprint, CancellationToken cancellationToken)
            => inner.TryBeginAsync(key, requestFingerprint, cancellationToken);

        public async Task<bool> CompleteAsync(string key, string reservationToken, string serializedResponse, CancellationToken cancellationToken)
        {
            await inner.CompleteAsync(key, reservationToken, serializedResponse, cancellationToken);
            return false;
        }

        public Task<bool> ReleaseAsync(string key, string reservationToken, CancellationToken cancellationToken)
            => inner.ReleaseAsync(key, reservationToken, cancellationToken);
    }

    [Fact]
    public async Task Handle_Nested_SkipsIdempotencyAndCallsNextDirectly()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateBehavior(store, scope: new FakeCommandScope(isNested: true));

        var result = await behavior.Handle(
            new TestCommand("key-1", "payload"),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        result.Value.Should().Be("ok");
        store.BeginCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task Handle_EmptyKey_ReturnsKeyRequiredValidationFailure(string key)
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateBehavior(store);

        var result = await behavior.Handle(
            new TestCommand(key, "payload"),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("idempotency.key_required", "the same code 14.Presentation answers a missing header with");
        store.BeginCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NewKey_Success_CompletesReservation()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateBehavior(store);

        var result = await behavior.Handle(
            new TestCommand("key-1", "payload"),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        store.CompleteCallCount.Should().Be(1);
        store.ReleaseCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NewKey_ResultFailure_ReleasesReservationInsteadOfCompleting()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateBehavior(store);

        var result = await behavior.Handle(
            new TestCommand("key-1", "payload"),
            () => Task.FromResult(Result<string>.Failure(Error.BusinessRule("rule", "denied"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        store.CompleteCallCount.Should().Be(0);
        store.ReleaseCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NewKey_ThrownException_ReleasesReservationAndRethrows()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateBehavior(store);

        var act = async () => await behavior.Handle(
            new TestCommand("key-1", "payload"),
            () => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        store.CompleteCallCount.Should().Be(0);
        store.ReleaseCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_InProgressKey_ReturnsConflictWithoutCallingNext()
    {
        var store = new FakeIdempotencyStore();
        var caller = User("user-1");
        var request = new TestCommand("key-1", "payload");
        var firstHandlerGate = new TaskCompletionSource<Result<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The first attempt reserves the key and then waits inside its handler, so the key is in flight.
        var first = Send(store, caller, request, () => firstHandlerGate.Task);

        var nextCalled = false;
        var second = await Send(store, caller, request, () =>
        {
            nextCalled = true;
            return Task.FromResult(Result<string>.Success("ok"));
        });

        nextCalled.Should().BeFalse();
        second.IsFailure.Should().BeTrue();
        second.Error.Type.Should().Be(ErrorType.Conflict);
        second.Error.Code.Should().Be("idempotency.in_progress");

        firstHandlerGate.SetResult(Result<string>.Success("first"));
        (await first).Value.Should().Be("first");
    }

    [Fact]
    public async Task Handle_CompletedKey_SameFingerprint_ReplaysStoredResponseWithoutCallingNext()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateBehavior(store);
        var request = new TestCommand("key-1", "payload");

        var first = await behavior.Handle(request, () => Task.FromResult(Result<string>.Success("original")), CancellationToken.None);
        var nextCalled = false;
        var second = await behavior.Handle(request, () =>
        {
            nextCalled = true;
            return Task.FromResult(Result<string>.Success("different"));
        }, CancellationToken.None);

        first.Value.Should().Be("original");
        nextCalled.Should().BeFalse();
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().Be("original");
    }

    [Fact]
    public async Task Handle_SameKeyDifferentFingerprint_ReturnsKeyReusedConflict()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateBehavior(store);

        await behavior.Handle(new TestCommand("key-1", "payload-a"), () => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);
        var result = await behavior.Handle(new TestCommand("key-1", "payload-b"), () => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("idempotency.key_reused");
    }

    [Fact]
    public async Task Handle_ConflictErrors_NeverEchoTheKeyInTheirMessage()
    {
        var store = new FakeIdempotencyStore();
        var caller = User("user-1");
        const string secretKey = "super-secret-idempotency-key-12345";
        var firstHandlerGate = new TaskCompletionSource<Result<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Send(store, caller, new TestCommand(secretKey, "payload"), () => firstHandlerGate.Task);
        var inProgress = await Send(store, caller, new TestCommand(secretKey, "payload"), () => Task.FromResult(Result<string>.Success("ok")));
        firstHandlerGate.SetResult(Result<string>.Success("first"));
        await first;
        var reused = await Send(store, caller, new TestCommand(secretKey, "other payload"), () => Task.FromResult(Result<string>.Success("ok")));

        inProgress.Error.Code.Should().Be("idempotency.in_progress");
        inProgress.Error.Message.Should().NotContain(secretKey);
        reused.Error.Code.Should().Be("idempotency.key_reused");
        reused.Error.Message.Should().NotContain(secretKey);
    }

    [Fact]
    public async Task Handle_CompleteAsyncReportsReservationLost_StillReturnsResponse_AndLogsWarning()
    {
        var inner = new FakeIdempotencyStore();
        var store = new LostReservationOnCompleteStore(inner);
        var logger = new FakeLogger<IdempotencyBehavior<TestCommand, Result<string>>>();
        var behavior = CreateBehavior(store, logger: logger);

        var result = await behavior.Handle(
            new TestCommand("key-1", "payload"),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("idempotency reservation was lost"));
    }

    [Fact]
    public async Task Handle_ReleaseAsyncReportsReservationLost_DoesNotLog()
    {
        var inner = new FakeIdempotencyStore();
        var store = new LostReservationOnCompleteStore(inner); // Release path delegates to inner unmodified; the point here is no warning fires on this path at all.
        var logger = new FakeLogger<IdempotencyBehavior<TestCommand, Result<string>>>();
        var behavior = CreateBehavior(store, logger: logger);

        var result = await behavior.Handle(
            new TestCommand("key-1", "payload"),
            () => Task.FromResult(Result<string>.Failure(Error.BusinessRule("rule", "denied"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        logger.Entries.Should().BeEmpty();
    }

    // ---- Per-caller scoping (P-562 X3) ----

    /// <summary>
    /// The finding X3 fixes: a caller who learned another caller's key and sent the same body used to be handed that
    /// caller's stored response. Now each caller gets its own reservation and its own execution.
    /// </summary>
    [Fact]
    public async Task Handle_TwoUsersOfOneTenant_SameKeyAndBody_EachGetsTheirOwnExecution()
    {
        var store = new FakeIdempotencyStore();
        var tenant = Guid.NewGuid();
        var alice = User("alice", tenant);
        var bob = User("bob", tenant);
        var request = new TestCommand("shared-key", "identical body");
        var bobHandlerRuns = 0;

        var aliceFirst = await Send(store, alice, request, () => Task.FromResult(Result<string>.Success("alice's one-time secret")));
        var bobAttempt = await Send(store, bob, request, () =>
        {
            bobHandlerRuns++;
            return Task.FromResult(Result<string>.Success("bob's own result"));
        });

        aliceFirst.Value.Should().Be("alice's one-time secret");
        bobHandlerRuns.Should().Be(1, "bob's request is his own execution, not a replay of alice's");
        bobAttempt.Value.Should().Be("bob's own result");
        bobAttempt.Value.Should().NotContain("alice");
        store.BegunKeys.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_SameUserRetry_ReplaysTheFirstResponse()
    {
        var store = new FakeIdempotencyStore();
        var tenant = Guid.NewGuid();
        var request = new TestCommand("retry-key", "body");
        var retryRan = false;

        await Send(store, User("alice", tenant), request, () => Task.FromResult(Result<string>.Success("first")));
        var retry = await Send(store, User("alice", tenant), request, () =>
        {
            retryRan = true;
            return Task.FromResult(Result<string>.Success("second"));
        });

        retryRan.Should().BeFalse();
        retry.Value.Should().Be("first");
    }

    /// <summary>
    /// The session is deliberately not part of the scope: a client that signs in again and retries must find its
    /// first attempt, or the retry would run the command a second time.
    /// </summary>
    [Fact]
    public async Task Handle_SameUserRetry_FromANewSession_StillReplays()
    {
        var store = new FakeIdempotencyStore();
        var request = new TestCommand("retry-key", "body");
        var retryRan = false;

        await Send(store, new FakeRequestContext(true) { UserId = "alice", SessionId = "session-1" }, request,
            () => Task.FromResult(Result<string>.Success("first")));
        var retry = await Send(store, new FakeRequestContext(true) { UserId = "alice", SessionId = "session-2" }, request, () =>
        {
            retryRan = true;
            return Task.FromResult(Result<string>.Success("second"));
        });

        retryRan.Should().BeFalse();
        retry.Value.Should().Be("first");
    }

    /// <summary>
    /// The documented residual risk: callers the context cannot identify share one scope per tenant, so only the
    /// fingerprint separates them. An anonymous caller who knows another anonymous caller's key and sends the same
    /// body receives the stored response.
    /// </summary>
    [Fact]
    public async Task Handle_AnonymousCallersOfOneTenant_SameKeyAndBody_ReplayTheFirstResponse()
    {
        var store = new FakeIdempotencyStore();
        var tenant = Guid.NewGuid();
        var request = new TestCommand("anonymous-key", "identical body");
        var secondRan = false;

        await Send(store, Anonymous(tenant), request, () => Task.FromResult(Result<string>.Success("first sender's response")));
        var second = await Send(store, Anonymous(tenant), request, () =>
        {
            secondRan = true;
            return Task.FromResult(Result<string>.Success("second sender's response"));
        });

        secondRan.Should().BeFalse();
        second.Value.Should().Be("first sender's response");
    }

    [Fact]
    public async Task Handle_AnonymousCallersOfOneTenant_SameKeyDifferentBody_AreRejectedAsKeyReused()
    {
        var store = new FakeIdempotencyStore();
        var tenant = Guid.NewGuid();
        var secondRan = false;

        await Send(store, Anonymous(tenant), new TestCommand("anonymous-key", "body a"), () => Task.FromResult(Result<string>.Success("a")));
        var second = await Send(store, Anonymous(tenant), new TestCommand("anonymous-key", "body b"), () =>
        {
            secondRan = true;
            return Task.FromResult(Result<string>.Success("b"));
        });

        secondRan.Should().BeFalse();
        second.Error.Type.Should().Be(ErrorType.Conflict);
        second.Error.Code.Should().Be("idempotency.key_reused");
    }

    [Fact]
    public async Task Handle_AnonymousCallersOfDifferentTenants_AreScopedSeparately()
    {
        var store = new FakeIdempotencyStore();
        var request = new TestCommand("anonymous-key", "identical body");

        await Send(store, Anonymous(Guid.NewGuid()), request, () => Task.FromResult(Result<string>.Success("tenant a")));
        var other = await Send(store, Anonymous(Guid.NewGuid()), request, () => Task.FromResult(Result<string>.Success("tenant b")));

        other.Value.Should().Be("tenant b");
    }

    [Fact]
    public async Task Handle_SameUserInDifferentTenants_IsScopedSeparately()
    {
        var store = new FakeIdempotencyStore();
        var request = new TestCommand("key", "body");

        await Send(store, User("alice", Guid.NewGuid()), request, () => Task.FromResult(Result<string>.Success("tenant a")));
        var other = await Send(store, User("alice", Guid.NewGuid()), request, () => Task.FromResult(Result<string>.Success("tenant b")));

        other.Value.Should().Be("tenant b");
    }

    /// <summary>
    /// Service and system actors are callers too, and the actor kind is part of the scope: the same identifier under
    /// a different kind is a different caller.
    /// </summary>
    [Fact]
    public async Task Handle_ServiceSystemAndUserActorsWithTheSameIdentifier_AreScopedSeparately()
    {
        var store = new FakeIdempotencyStore();
        var tenant = Guid.NewGuid();
        var request = new TestCommand("key", "body");
        var callers = new[]
        {
            new FakeRequestContext(true) { UserId = "orders", TenantId = tenant, ActorKind = ActorKind.User },
            new FakeRequestContext(true) { UserId = "orders", ClientId = "orders", TenantId = tenant, ActorKind = ActorKind.Service },
            new FakeRequestContext(true) { UserId = "orders", TenantId = tenant, ActorKind = ActorKind.System },
        };
        var executions = 0;

        foreach (var caller in callers)
        {
            await Send(store, caller, request, () =>
            {
                executions++;
                return Task.FromResult(Result<string>.Success(caller.ActorKind.ToString()));
            });
        }

        executions.Should().Be(3);
        store.BegunKeys.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_TwoServicesOfOneTenant_AreScopedSeparately_AndEachRetryReplays()
    {
        var store = new FakeIdempotencyStore();
        var tenant = Guid.NewGuid();
        var billing = new FakeRequestContext(true) { UserId = "billing", ClientId = "billing", TenantId = tenant, ActorKind = ActorKind.Service };
        var shipping = new FakeRequestContext(true) { UserId = "shipping", ClientId = "shipping", TenantId = tenant, ActorKind = ActorKind.Service };
        var request = new TestCommand("key", "body");

        await Send(store, billing, request, () => Task.FromResult(Result<string>.Success("billing")));
        var shippingFirst = await Send(store, shipping, request, () => Task.FromResult(Result<string>.Success("shipping")));
        var billingRetry = await Send(store, billing, request, () => Task.FromResult(Result<string>.Success("billing again")));

        shippingFirst.Value.Should().Be("shipping");
        billingRetry.Value.Should().Be("billing");
    }

    [Fact]
    public async Task Handle_SystemContexts_AreScopedByTheirIdentity()
    {
        var store = new FakeIdempotencyStore();
        var request = new TestCommand("nightly", "body");

        await Send(store, new SystemRequestContext([], "billing-job"), request, () => Task.FromResult(Result<string>.Success("billing-job")));
        var otherJob = await Send(store, new SystemRequestContext([], "cleanup-job"), request, () => Task.FromResult(Result<string>.Success("cleanup-job")));
        var retry = await Send(store, new SystemRequestContext([], "billing-job"), request, () => Task.FromResult(Result<string>.Success("again")));

        otherJob.Value.Should().Be("cleanup-job");
        retry.Value.Should().Be("billing-job");
    }

    [Fact]
    public async Task Handle_SameUserThroughDifferentClients_IsScopedSeparately()
    {
        var store = new FakeIdempotencyStore();
        var request = new TestCommand("key", "body");

        await Send(store, new FakeRequestContext(true) { UserId = "alice", ClientId = "web-app" }, request,
            () => Task.FromResult(Result<string>.Success("web")));
        var mobile = await Send(store, new FakeRequestContext(true) { UserId = "alice", ClientId = "third-party-app" }, request,
            () => Task.FromResult(Result<string>.Success("third party")));

        mobile.Value.Should().Be("third party");
    }

    [Fact]
    public async Task Handle_ImpersonatorActingForAUser_IsScopedSeparatelyFromTheUser()
    {
        var store = new FakeIdempotencyStore();
        var request = new TestCommand("key", "body");

        await Send(store, new FakeRequestContext(true) { UserId = "alice" }, request, () => Task.FromResult(Result<string>.Success("alice")));
        var support = await Send(store, new FakeRequestContext(true) { UserId = "alice", ImpersonatorId = "support-agent" }, request,
            () => Task.FromResult(Result<string>.Success("support")));

        support.Value.Should().Be("support");
    }

    [Fact]
    public async Task Handle_KeyHandedToTheStore_IsAFixedLengthLowercaseHexDigest_CarryingNeitherTheKeyNorTheIdentity()
    {
        var store = new FakeIdempotencyStore();
        var caller = new FakeRequestContext(true) { UserId = "alice@example.com", ClientId = "web-app", TenantId = Guid.NewGuid() };

        await Send(store, caller, new TestCommand("order-42", "body"), () => Task.FromResult(Result<string>.Success("ok")));
        await Send(store, caller, new TestCommand(new string('k', 10_000), "body"), () => Task.FromResult(Result<string>.Success("ok")));

        store.BegunKeys.Should().HaveCount(2).And.OnlyContain(key => IsLowercaseHexDigest(key));
        store.BegunKeys[0].Should().NotContain("order-42").And.NotContain("alice").And.NotContain("web-app");
    }

    /// <summary>
    /// Pairs that plain concatenation with a separator would merge. Each must reach the store under a different key.
    /// </summary>
    [Fact]
    public async Task Handle_ScopeEncoding_IsUnambiguous()
    {
        (FakeRequestContext Caller, string Key)[][] pairs =
        [
            // A separator moved from the subject into the key.
            [(new FakeRequestContext(true) { UserId = "a:b" }, "c"), (new FakeRequestContext(true) { UserId = "a" }, "b:c")],
            // No subject versus an empty subject.
            [(new FakeRequestContext(true) { UserId = null }, "k"), (new FakeRequestContext(true) { UserId = "" }, "k")],
            // The same value as the client instead of the subject.
            [(new FakeRequestContext(true) { UserId = null, ClientId = "x" }, "k"), (new FakeRequestContext(true) { UserId = "x", ClientId = null }, "k")],
            // The same value as the impersonator instead of the client.
            [(new FakeRequestContext(true) { UserId = "u", ClientId = "x" }, "k"), (new FakeRequestContext(true) { UserId = "u", ImpersonatorId = "x" }, "k")],
            // The subject's tail moved into the client.
            [(new FakeRequestContext(true) { UserId = "ab", ClientId = "c" }, "k"), (new FakeRequestContext(true) { UserId = "a", ClientId = "bc" }, "k")],
            // Two different unpaired surrogates, which UTF-8 would both replace with U+FFFD.
            [(new FakeRequestContext(true) { UserId = "\uD800" }, "k"), (new FakeRequestContext(true) { UserId = "\uD801" }, "k")],
        ];

        foreach (var pair in pairs)
        {
            var first = await ScopedKeyFor(pair[0].Caller, pair[0].Key);
            var second = await ScopedKeyFor(pair[1].Caller, pair[1].Key);

            first.Should().NotBe(second);
        }
    }

    [Fact]
    public async Task Handle_UndefinedActorKind_IsScopedWithoutThrowing()
    {
        var key = await ScopedKeyFor(new FakeRequestContext(true) { ActorKind = (ActorKind)42 }, "k");

        IsLowercaseHexDigest(key).Should().BeTrue();
    }

    /// <summary>
    /// Stores find reservations by this digest, so its layout is a stored format: a change orphans every reservation
    /// already stored. The expected value was computed outside .NET (PowerShell, from the layout documented on the
    /// scoping helper) and is re-derived here by an independent implementation.
    /// </summary>
    [Fact]
    public async Task Handle_ScopedKey_MatchesThePublishedV1Layout()
    {
        var tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var caller = new FakeRequestContext(true) { UserId = "alice", ClientId = "web-app", TenantId = tenant, SessionId = "ignored" };

        var key = await ScopedKeyFor(caller, "order-42");

        key.Should().Be("c41e86cd57ac2a1753e85cc1326f53853ee91bf3ef5b495c27fbea1853dd1548");
        key.Should().Be(ExpectedV1ScopedKey(tenant, ActorKind.User, "alice", "web-app", impersonatorId: null, "order-42"));
    }

    [Fact]
    public async Task Handle_SameCallerAndKey_AlwaysProduceTheSameScopedKey()
    {
        var tenant = Guid.NewGuid();

        var first = await ScopedKeyFor(User("alice", tenant), "k");
        var second = await ScopedKeyFor(User("alice", tenant), "k");

        first.Should().Be(second);
    }

    /// <summary>
    /// The behavior resolves the scoped caller from DI on every dispatch, through a real composed pipeline.
    /// </summary>
    [Fact]
    public async Task Dispatch_ComposedPipeline_ScopesEachReservationToTheCurrentCaller()
    {
        var caller = new FakeRequestContext(true) { UserId = "alice", TenantId = Guid.NewGuid() };
        var journal = new ComposedJournal();
        var services = new ServiceCollection();
        services.AddSingleton<IRequestContext>(caller);
        services.AddSingleton<IRequestIdempotencyStore>(new FakeIdempotencyStore());
        services.AddSingleton(journal);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<IdempotencyBehaviorTests>());
        services.AddSharedKernelApplicationBehaviors().AddIdempotencyBehavior().Build();
        await using var provider = services.BuildServiceProvider();

        async Task<Result<string>> SendAs(string userId)
        {
            caller.UserId = userId;
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ComposedCommand("shared-key"));
        }

        var alice = await SendAs("alice");
        var bob = await SendAs("bob");
        var aliceRetry = await SendAs("alice");

        journal.HandlerCalls.Should().Be(2);
        alice.Value.Should().Be("result for alice");
        bob.Value.Should().Be("result for bob");
        aliceRetry.Value.Should().Be("result for alice");
    }

    private sealed record ComposedCommand(string IdempotencyKey) : ICommand<string>, IIdempotentRequest;

    private sealed class ComposedJournal
    {
        public int HandlerCalls { get; set; }
    }

    private sealed class ComposedCommandHandler(IRequestContext caller, ComposedJournal journal) : ICommandHandler<ComposedCommand, string>
    {
        public Task<Result<string>> Handle(ComposedCommand request, CancellationToken cancellationToken)
        {
            journal.HandlerCalls++;
            return Task.FromResult(Result<string>.Success($"result for {caller.UserId}"));
        }
    }

    private static async Task<string> ScopedKeyFor(IRequestContext caller, string idempotencyKey)
    {
        var store = new FakeIdempotencyStore();
        await Send(store, caller, new TestCommand(idempotencyKey, "body"), () => Task.FromResult(Result<string>.Success("ok")));
        return store.BegunKeys.Single();
    }

    private static bool IsLowercaseHexDigest(string key) =>
        key.Length == 64 && key.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    // An independent implementation of the v1 layout: SHA-256 over (label, tenant "D", actor kind as a number,
    // subject, client, impersonator, key), each field 0x00 when absent or 0x01 + big-endian UTF-16 length + UTF-16LE
    // code units. Encoding.Unicode matches the code units for the well-formed strings used here.
    private static string ExpectedV1ScopedKey(
        Guid? tenantId,
        ActorKind actorKind,
        string? userId,
        string? clientId,
        string? impersonatorId,
        string idempotencyKey)
    {
        var bytes = new List<byte>();

        void Field(string? value)
        {
            if (value is null)
            {
                bytes.Add(0x00);
                return;
            }

            bytes.Add(0x01);
            var length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
            bytes.AddRange(length);
            bytes.AddRange(Encoding.Unicode.GetBytes(value));
        }

        Field("SharedKernel.Application.Behaviors.Idempotency.KeyScope.v1");
        Field(tenantId?.ToString("D"));
        Field(((int)actorKind).ToString(CultureInfo.InvariantCulture));
        Field(userId);
        Field(clientId);
        Field(impersonatorId);
        Field(idempotencyKey);

        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }

    // ---- IIdempotentRequest.Fingerprint ----

    private sealed record FingerprintCommand(string IdempotencyKey, string Payload, string? Fingerprint)
        : ICommand<string>, IIdempotentRequest;

    private static IdempotencyBehavior<FingerprintCommand, Result<string>> CreateFingerprintBehavior(IRequestIdempotencyStore store)
        => new(store, User("user-1"), new FakeCommandScope(), new FakeLogger<IdempotencyBehavior<FingerprintCommand, Result<string>>>());

    [Fact]
    public async Task Handle_CallerSuppliedFingerprint_TakesPrecedenceOverAutomaticHash_SoAPayloadChangeStillReplays()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateFingerprintBehavior(store);

        var first = await behavior.Handle(
            new FingerprintCommand("key-1", "payload-a", "explicit-fp"),
            () => Task.FromResult(Result<string>.Success("first")),
            CancellationToken.None);

        var nextCalled = false;
        var second = await behavior.Handle(
            new FingerprintCommand("key-1", "payload-b", "explicit-fp"), // different Payload, same explicit Fingerprint
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("second"));
            },
            CancellationToken.None);

        first.Value.Should().Be("first");
        nextCalled.Should().BeFalse();
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().Be("first"); // replayed, not re-run
    }

    [Fact]
    public async Task Handle_NullFingerprint_FallsBackToAutomaticHash_SoAPayloadChangeIsAKeyReusedConflict()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateFingerprintBehavior(store);

        await behavior.Handle(
            new FingerprintCommand("key-1", "payload-a", Fingerprint: null),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        var result = await behavior.Handle(
            new FingerprintCommand("key-1", "payload-b", Fingerprint: null),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("idempotency.key_reused");
    }

    [Fact]
    public async Task Handle_WhitespaceOnlyFingerprint_FallsBackToAutomaticHash_SoAPayloadChangeIsAKeyReusedConflict()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateFingerprintBehavior(store);

        await behavior.Handle(
            new FingerprintCommand("key-1", "payload-a", Fingerprint: "   "),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        var result = await behavior.Handle(
            new FingerprintCommand("key-1", "payload-b", Fingerprint: "   "),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        // A whitespace-only Fingerprint is never used literally (which would make both calls match
        // regardless of Payload) — it falls back to the payload-sensitive automatic hash instead.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("idempotency.key_reused");
    }

    private sealed class CyclicCommand : ICommand<string>, IIdempotentRequest
    {
        public string IdempotencyKey => "cyclic-key";

        // Self-reference forces System.Text.Json's default serializer to throw a JsonException
        // (max-depth/cycle detection) — never assigned via the constructor so the test controls it.
        public CyclicCommand? Self { get; set; }
    }

    [Fact]
    public async Task Handle_UnserializableCommand_NoExplicitFingerprint_ThrowsClearInvalidOperationException()
    {
        var store = new FakeIdempotencyStore();
        var behavior = new IdempotencyBehavior<CyclicCommand, Result<string>>(
            store, User("user-1"), new FakeCommandScope(), new FakeLogger<IdempotencyBehavior<CyclicCommand, Result<string>>>());
        var command = new CyclicCommand();
        command.Self = command;

        var act = async () => await behavior.Handle(command, () => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain(nameof(CyclicCommand));
        thrown.Which.Message.Should().Contain(nameof(IIdempotentRequest.Fingerprint));
    }
}
