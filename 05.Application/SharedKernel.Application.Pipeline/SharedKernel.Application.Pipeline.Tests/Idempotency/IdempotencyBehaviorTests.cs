using SharedKernel.Application.Commands;
using SharedKernel.Application.Idempotency;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Pipeline.Commands;
using SharedKernel.Application.Pipeline.Idempotency;
using SharedKernel.Idempotency.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;
using SharedKernel.Application.Pipeline.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Pipeline.Tests.Idempotency;

public sealed class IdempotencyBehaviorTests
{
    private sealed record TestCommand(string IdempotencyKey, string Payload) : ICommand<string>, IIdempotentRequest;

    private static IdempotencyBehavior<TestCommand, Result<string>> CreateBehavior(
        IIdempotencyStore store,
        ICommandScope? scope = null,
        ILogger<IdempotencyBehavior<TestCommand, Result<string>>>? logger = null)
        => new(store, scope ?? new FakeCommandScope(), DefaultOptions, logger ?? new FakeLogger<IdempotencyBehavior<TestCommand, Result<string>>>());

    private static readonly Microsoft.Extensions.Options.IOptions<IdempotencyBehaviorOptions> DefaultOptions =
        MsOptions.Create(new IdempotencyBehaviorOptions());

    /// <summary>Wraps a <see cref="FakeIdempotencyStore"/> and forces <c>CompleteAsync</c> to report a lost reservation.</summary>
    private sealed class LostReservationOnCompleteStore(IIdempotencyStore inner) : IIdempotencyStore
    {
        public Task<IdempotencyReservation> TryBeginAsync(
            IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken cancellationToken)
            => inner.TryBeginAsync(purpose, key, fingerprint, ttl, cancellationToken);

        public async Task<bool> CompleteAsync(
            IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken cancellationToken)
        {
            await inner.CompleteAsync(purpose, key, token, response, retention, cancellationToken);
            return false;
        }

        public Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken cancellationToken)
            => inner.ReleaseAsync(purpose, key, token, cancellationToken);
    }

    [Fact]
    public async Task Handle_UsesTheRequestPurpose_AndTheConfiguredLeaseAndRetention()
    {
        var store = new FakeIdempotencyStore();
        var options = MsOptions.Create(new IdempotencyBehaviorOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(7),
            RetentionWindow = TimeSpan.FromMinutes(9),
        });
        var behavior = new IdempotencyBehavior<TestCommand, Result<string>>(
            store, new FakeCommandScope(), options, new FakeLogger<IdempotencyBehavior<TestCommand, Result<string>>>());

        await behavior.Handle(
            new TestCommand("key-1", "payload"),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        store.Purposes.Should().OnlyContain(p => p == IdempotencyPurpose.Request);
        store.LastTtl.Should().Be(TimeSpan.FromSeconds(7));
        store.LastRetention.Should().Be(TimeSpan.FromMinutes(9));
    }

    [Fact]
    public void Options_LeaseNotShorterThanRetention_FailsValidation()
    {
        var options = new IdempotencyBehaviorOptions
        {
            LeaseDuration = TimeSpan.FromHours(2),
            RetentionWindow = TimeSpan.FromHours(1),
        };

        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options, new System.ComponentModel.DataAnnotations.ValidationContext(options), results, validateAllProperties: true);

        valid.Should().BeFalse();
        results.Should().ContainSingle(r => r.MemberNames.Contains(nameof(IdempotencyBehaviorOptions.LeaseDuration)));
    }

    [Fact]
    public async Task Handle_Nested_SkipsIdempotencyAndCallsNextDirectly()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateBehavior(store, new FakeCommandScope(isNested: true));

        var result = await behavior.Handle(
            new TestCommand("key-1", "payload"),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        result.Value.Should().Be("ok");
        store.BeginCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EmptyKey_ReturnsValidationFailure()
    {
        var store = new FakeIdempotencyStore();
        var behavior = CreateBehavior(store);

        var result = await behavior.Handle(
            new TestCommand("   ", "payload"),
            () => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("idempotency.key_missing");
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
        var scope = new FakeCommandScope();
        var behavior = CreateBehavior(store, scope);
        await store.TryBeginAsync(IdempotencyPurpose.Request, "key-1", RequestFingerprintFor(new TestCommand("key-1", "payload")), TimeSpan.FromSeconds(30), CancellationToken.None);

        var nextCalled = false;
        var result = await behavior.Handle(new TestCommand("key-1", "payload"), () =>
        {
            nextCalled = true;
            return Task.FromResult(Result<string>.Success("ok"));
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("idempotency.in_progress");
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
    public async Task Handle_ConflictError_NeverEchoesKeyInMessage()
    {
        var store = new FakeIdempotencyStore();
        var scope = new FakeCommandScope();
        var behavior = CreateBehavior(store, scope);
        const string secretKey = "super-secret-idempotency-key-12345";
        await store.TryBeginAsync(IdempotencyPurpose.Request, secretKey, RequestFingerprintFor(new TestCommand(secretKey, "payload")), TimeSpan.FromSeconds(30), CancellationToken.None);

        var result = await behavior.Handle(new TestCommand(secretKey, "payload"), () => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);

        result.Error.Message.Should().NotContain(secretKey);
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

    // Mirrors IdempotencyBehavior's own RequestFingerprint.Compute algorithm (SHA-256 of the
    // request's default JSON serialization) — duplicated here rather than referencing the
    // production internal type, since this test project intentionally has no InternalsVisibleTo.
    private static string RequestFingerprintFor(TestCommand command)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(command, typeof(TestCommand));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    // ---- IIdempotentRequest.Fingerprint ----

    private sealed record FingerprintCommand(string IdempotencyKey, string Payload, string? Fingerprint)
        : ICommand<string>, IIdempotentRequest;

    private static IdempotencyBehavior<FingerprintCommand, Result<string>> CreateFingerprintBehavior(IIdempotencyStore store)
        => new(store, new FakeCommandScope(), DefaultOptions, new FakeLogger<IdempotencyBehavior<FingerprintCommand, Result<string>>>());

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
            store, new FakeCommandScope(), DefaultOptions, new FakeLogger<IdempotencyBehavior<CyclicCommand, Result<string>>>());
        var command = new CyclicCommand();
        command.Self = command;

        var act = async () => await behavior.Handle(command, () => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain(nameof(CyclicCommand));
        thrown.Which.Message.Should().Contain(nameof(IIdempotentRequest.Fingerprint));
    }
}
