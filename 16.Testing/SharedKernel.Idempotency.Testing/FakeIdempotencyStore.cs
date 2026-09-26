using System.Collections.Concurrent;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;

namespace SharedKernel.Testing.Idempotency;

/// <summary>
/// In-memory, thread-safe fake <see cref="IIdempotencyStore"/> for unit tests, serving both
/// <see cref="IdempotencyPurpose.Request"/> (the pipeline's <c>IdempotencyBehavior</c>) and
/// <see cref="IdempotencyPurpose.Message"/> (MassTransit's consumer idempotency).
/// </summary>
/// <remarks>
/// <para>
/// Implements the real reservation protocol under one lock, so concurrent duplicates never both see
/// <see cref="IdempotencyReservationStatus.Started"/>. Entries are keyed by tenant scope
/// (<see cref="IdempotencyTenantScope"/>, read from the ambient request context), purpose and key, exactly as the
/// Redis and EF Core stores key them:
/// </para>
/// <list type="bullet">
///   <item><description>No entry — <see cref="IdempotencyReservationStatus.Started"/> with a fresh token.</description></item>
///   <item><description>An entry for a different fingerprint — <see cref="IdempotencyReservationStatus.FingerprintMismatch"/>.</description></item>
///   <item><description>An in-flight entry — <see cref="IdempotencyReservationStatus.InProgress"/>.</description></item>
///   <item><description>A completed entry — <see cref="IdempotencyReservationStatus.Completed"/> with its stored response.</description></item>
/// </list>
/// <para>
/// <see cref="CompleteAsync"/> and <see cref="ReleaseAsync"/> act only for the token that owns an in-flight entry and
/// return <see langword="false"/> otherwise, never throwing. A release removes the entry, so the key starts fresh.
/// Time is not simulated: call <see cref="Expire"/> to model a reservation's TTL running out.
/// </para>
/// <para>
/// <b>Keys are stored exactly as given; only the tenant scope is added here.</b> Through the pipeline's
/// <c>IdempotencyBehavior</c> the store receives the command's key already scoped to the tenant and the caller (a
/// 64-character lowercase hexadecimal digest), the same key the Redis and EF Core stores receive, so a pipeline test
/// sees the same per-caller separation production does: two callers using one key each get their own reservation.
/// Consequently <see cref="Calls"/> records that scoped key, not the command's raw
/// <c>IIdempotentRequest.IdempotencyKey</c>.
/// </para>
/// </remarks>
public sealed class FakeIdempotencyStore : IIdempotencyStore
{
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly object _gate = new();
    private readonly Dictionary<(string Scope, IdempotencyPurpose Purpose, string Key), Entry> _entries = [];
    private readonly ConcurrentQueue<RecordedCall> _calls = new();
    private int _tokenCounter;

    private readonly record struct Entry(string Fingerprint, bool Completed, string? StoredResponse, string Token);

    /// <summary>Creates a store scoped by the ambient request context (<see cref="RequestContextScope"/>).</summary>
    public FakeIdempotencyStore()
        : this(new RequestContextAccessor())
    {
    }

    /// <summary>Creates a store scoped by the tenant <paramref name="requestContextAccessor"/> reports.</summary>
    /// <param name="requestContextAccessor">Supplies the tenant each key is scoped by.</param>
    public FakeIdempotencyStore(IRequestContextAccessor requestContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(requestContextAccessor);
        _requestContextAccessor = requestContextAccessor;
    }

    /// <summary>One call recorded against this store, in the order it happened.</summary>
    /// <param name="Member">The member invoked: <c>TryBeginAsync</c>, <c>CompleteAsync</c>, or <c>ReleaseAsync</c>.</param>
    /// <param name="Purpose">The purpose the call was made for.</param>
    /// <param name="Key">The idempotency key the call was made against.</param>
    public readonly record struct RecordedCall(string Member, IdempotencyPurpose Purpose, string Key);

    /// <summary>Gets every call made against this store so far, in call order, for test assertions.</summary>
    public IReadOnlyList<RecordedCall> Calls => [.. _calls];

    /// <summary>The last in-flight TTL passed to <see cref="TryBeginAsync"/>, or <see langword="null"/>.</summary>
    public TimeSpan? LastTtl { get; private set; }

    /// <summary>The last retention window passed to <see cref="CompleteAsync"/>, or <see langword="null"/>.</summary>
    public TimeSpan? LastRetention { get; private set; }

    /// <inheritdoc />
    public Task<IdempotencyReservation> TryBeginAsync(
        IdempotencyPurpose purpose,
        string key,
        string fingerprint,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        _calls.Enqueue(new RecordedCall(nameof(TryBeginAsync), purpose, key));

        lock (_gate)
        {
            LastTtl = ttl;
            var id = Id(purpose, key);
            if (!_entries.TryGetValue(id, out var existing))
            {
                var token = $"fake-token-{++_tokenCounter}";
                _entries[id] = new Entry(fingerprint, Completed: false, StoredResponse: null, token);
                return Task.FromResult(IdempotencyReservation.Started(token));
            }

            if (existing.Fingerprint != fingerprint)
                return Task.FromResult(IdempotencyReservation.FingerprintMismatch());

            return Task.FromResult(
                existing.Completed
                    ? IdempotencyReservation.Completed(existing.StoredResponse)
                    : IdempotencyReservation.InProgress());
        }
    }

    /// <inheritdoc />
    public Task<bool> CompleteAsync(
        IdempotencyPurpose purpose,
        string key,
        string token,
        string? response,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

        _calls.Enqueue(new RecordedCall(nameof(CompleteAsync), purpose, key));

        lock (_gate)
        {
            LastRetention = retention;
            var id = Id(purpose, key);
            if (!_entries.TryGetValue(id, out var existing) || existing.Token != token || existing.Completed)
                return Task.FromResult(false);

            _entries[id] = existing with { Completed = true, StoredResponse = response };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ReleaseAsync(
        IdempotencyPurpose purpose,
        string key,
        string token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(token);

        _calls.Enqueue(new RecordedCall(nameof(ReleaseAsync), purpose, key));

        lock (_gate)
        {
            var id = Id(purpose, key);
            if (!_entries.TryGetValue(id, out var existing) || existing.Token != token || existing.Completed)
                return Task.FromResult(false);

            _entries.Remove(id);
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Simulates the reservation or retention TTL of <paramref name="key"/> running out in the current tenant scope:
    /// the entry is dropped, so the next <see cref="TryBeginAsync"/> starts fresh and the old token no longer owns it.
    /// </summary>
    /// <param name="purpose">The purpose the key was reserved for.</param>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> if an entry was dropped.</returns>
    public bool Expire(IdempotencyPurpose purpose, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            return _entries.Remove(Id(purpose, key));
        }
    }

    /// <summary>Clears every reservation and the recorded call list.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _entries.Clear();
            LastTtl = null;
            LastRetention = null;
        }

        _calls.Clear();
    }

    private (string Scope, IdempotencyPurpose Purpose, string Key) Id(IdempotencyPurpose purpose, string key) =>
        (IdempotencyTenantScope.Current(_requestContextAccessor), purpose, key);
}
