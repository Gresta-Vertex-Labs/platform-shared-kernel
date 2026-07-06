using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Idempotency;

/// <summary>
/// Verifies <see cref="IdempotentCommandBehavior{TRequest,TResponse}"/>'s opt-in response-replay
/// path (WO-039, P-242 — T-46/T-47/T-48).
/// </summary>
public sealed class IdempotentCommandBehaviorReplayTests
{
    private sealed record TestCommand(string IdempotencyKey) : ICommand, IIdempotentRequest;

    private sealed class TestCommandHandler(Func<Result> responseFactory) : IRequestHandler<TestCommand, Result>
    {
        public int InvocationCount { get; private set; }

        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(responseFactory());
        }
    }

    /// <summary>
    /// A test double implementing BOTH <see cref="IIdempotencyKeyStore"/> and
    /// <see cref="IIdempotencyResponseStore"/> — an in-memory dictionary-backed store, standing in
    /// for a real replay-capable implementation.
    /// </summary>
    private sealed class ReplayCapableStore : IIdempotencyKeyStore, IIdempotencyResponseStore
    {
        private readonly HashSet<string> _processedKeys = [];
        private readonly Dictionary<string, string> _storedResponses = [];

        public bool SuppressResponsePersistence { get; set; }

        public Task<bool> HasProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult(_processedKeys.Contains(idempotencyKey));

        public Task MarkProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            _processedKeys.Add(idempotencyKey);
            return Task.CompletedTask;
        }

        public Task<string?> TryGetStoredResponseAsync(string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult(_storedResponses.TryGetValue(idempotencyKey, out var value) ? value : null);

        public Task StoreResponseAsync(string idempotencyKey, string serializedResponse, CancellationToken cancellationToken)
        {
            if (!SuppressResponsePersistence)
                _storedResponses[idempotencyKey] = serializedResponse;
            return Task.CompletedTask;
        }

        /// <summary>Marks a key as processed WITHOUT a stored response — simulates a pre-replay-adoption key.</summary>
        public void MarkProcessedWithoutStoredResponse(string idempotencyKey) => _processedKeys.Add(idempotencyKey);
    }

    /// <summary>A store implementing ONLY <see cref="IIdempotencyKeyStore"/> — no replay capability.</summary>
    private sealed class NoReplayStore : IIdempotencyKeyStore
    {
        private readonly HashSet<string> _processedKeys = [];

        public Task<bool> HasProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult(_processedKeys.Contains(idempotencyKey));

        public Task MarkProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            _processedKeys.Add(idempotencyKey);
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildProvider(IIdempotencyKeyStore store, TestCommandHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestCommand, Result>>(sp => sp.GetRequiredService<TestCommandHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotentCommandBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<IdempotentCommandBehaviorReplayTests>());

        return services.BuildServiceProvider();
    }

    // ---- T-45: no-replay-capability duplicate still returns Error.Conflict, unchanged ----
    // (Also independently covered by the pre-existing IdempotentCommandBehaviorTests using an
    // NSubstitute mock that satisfies only IIdempotencyKeyStore — this test uses a hand-written
    // store to make the "no IIdempotencyResponseStore capability at all" case explicit.)

    [Fact]
    public async Task Handle_DuplicateKey_StoreWithoutReplayCapability_StillReturnsErrorConflict()
    {
        var store = new NoReplayStore();
        await store.MarkProcessedAsync("dup-key", CancellationToken.None);
        var handler = new TestCommandHandler(Result.Success);
        var provider = BuildProvider(store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand("dup-key"));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        handler.InvocationCount.Should().Be(0, "a duplicate must never invoke the handler a second time");
    }

    // ---- T-46: replay-capability duplicate after a successful first attempt returns the ORIGINAL success ----

    [Fact]
    public async Task Handle_DuplicateKey_ReplayCapableStore_SuccessfulFirstAttempt_ReplaysOriginalSuccess()
    {
        var store = new ReplayCapableStore();
        var handler = new TestCommandHandler(Result.Success);
        var provider = BuildProvider(store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var first = await sender.Send(new TestCommand("replay-success"));
        first.IsSuccess.Should().BeTrue();
        handler.InvocationCount.Should().Be(1);

        var duplicate = await sender.Send(new TestCommand("replay-success"));

        duplicate.IsSuccess.Should().BeTrue("the duplicate must replay the ORIGINAL success, not a fresh Error.Conflict");
        handler.InvocationCount.Should().Be(1, "the handler must never be invoked a second time for a replayed duplicate");
    }

    // ---- T-47: replay-capability duplicate after a Result.Failure first attempt replays the ORIGINAL failure ----

    [Fact]
    public async Task Handle_DuplicateKey_ReplayCapableStore_FailedFirstAttempt_ReplaysOriginalFailure()
    {
        var originalError = Error.BusinessRule("order.locked", "The order is locked for editing.");
        var store = new ReplayCapableStore();
        var handler = new TestCommandHandler(() => Result.Failure(originalError));
        var provider = BuildProvider(store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var first = await sender.Send(new TestCommand("replay-failure"));
        first.IsFailure.Should().BeTrue();
        first.Error.Code.Should().Be(originalError.Code);
        handler.InvocationCount.Should().Be(1);

        var duplicate = await sender.Send(new TestCommand("replay-failure"));

        duplicate.IsFailure.Should().BeTrue();
        duplicate.Error.Code.Should().Be(originalError.Code,
            "the duplicate must replay the ORIGINAL stored failure, not a fresh Error.Conflict");
        duplicate.Error.Type.Should().NotBe(ErrorType.Conflict,
            "a replayed original failure must not be masked as a generic conflict");
        handler.InvocationCount.Should().Be(1, "the handler must never be invoked a second time for a replayed duplicate");
    }

    // ---- T-48: TryGetStoredResponseAsync returns null for an already-processed key -> falls back to Error.Conflict ----

    [Fact]
    public async Task Handle_DuplicateKey_ReplayCapableStore_NoStoredResponseForProcessedKey_FallsBackToErrorConflict()
    {
        var store = new ReplayCapableStore();
        store.MarkProcessedWithoutStoredResponse("pre-replay-key");
        var handler = new TestCommandHandler(Result.Success);
        var provider = BuildProvider(store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand("pre-replay-key"));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict,
            "when a key is marked processed but no stored response exists (e.g. a pre-replay-adoption key), " +
            "the behavior must fall back to Error.Conflict rather than throwing or returning a default value");
        handler.InvocationCount.Should().Be(0);
    }

    // ---- Sanity: the serialized response actually round-trips a realistic Result<T> payload shape ----

    [Fact]
    public async Task Handle_DuplicateKey_ReplayCapableStore_ResultOfTPayload_RoundTripsCorrectly()
    {
        var store = new ReplayCapableStore();
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyKeyStore>(store);
        services.AddSingleton(store);
        var handler = new PayloadCommandHandler();
        services.AddSingleton<IRequestHandler<PayloadCommand, Result<string>>>(handler);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotentCommandBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<IdempotentCommandBehaviorReplayTests>());
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var first = await sender.Send(new PayloadCommand("payload-key"));
        first.IsSuccess.Should().BeTrue();
        first.Value.Should().Be("created-value");

        var duplicate = await sender.Send(new PayloadCommand("payload-key"));

        duplicate.IsSuccess.Should().BeTrue();
        duplicate.Value.Should().Be("created-value", "the replayed Result<T> payload must round-trip exactly");
        handler.InvocationCount.Should().Be(1);
    }

    private sealed record PayloadCommand(string IdempotencyKey) : ICommand<string>, IIdempotentRequest;

    private sealed class PayloadCommandHandler : IRequestHandler<PayloadCommand, Result<string>>
    {
        public int InvocationCount { get; private set; }

        public Task<Result<string>> Handle(PayloadCommand request, CancellationToken ct)
        {
            InvocationCount++;
            return Task.FromResult(Result<string>.Success("created-value"));
        }
    }
}
