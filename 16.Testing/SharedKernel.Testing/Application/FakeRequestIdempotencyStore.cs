using System.Collections.Concurrent;
using SharedKernel.Application.Behaviors.Idempotency;

namespace SharedKernel.Testing.Application;

/// <summary>
/// In-memory, thread-safe fake implementation of <see cref="IRequestIdempotencyStore"/>
/// (<c>05.Application.Behaviors</c>) for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Implements the exact begin/complete/release contract <see cref="IdempotencyBehavior{TRequest,TResponse}"/>
/// depends on. <see cref="TryBeginAsync"/> atomically reserves <c>key</c> against a request
/// fingerprint (guarded by an internal lock, so a truly concurrent duplicate submission can never
/// observe two <see cref="IdempotencyBeginStatus.Started"/> results for the same key):
/// </para>
/// <list type="bullet">
///   <item><description>No entry exists for <c>key</c> — a reservation is created and
///   <see cref="IdempotencyBeginStatus.Started"/> is returned, carrying a fresh, opaque
///   <see cref="IdempotencyBeginResult.ReservationToken"/>.</description></item>
///   <item><description>An entry exists, not yet completed, with the <b>same</b> fingerprint —
///   <see cref="IdempotencyBeginStatus.InProgress"/>.</description></item>
///   <item><description>An entry exists, already completed, with the <b>same</b> fingerprint —
///   <see cref="IdempotencyBeginStatus.Completed"/>, carrying the stored response.</description></item>
///   <item><description>An entry exists (in-flight or completed) against a <b>different</b>
///   fingerprint — <see cref="IdempotencyBeginStatus.FingerprintMismatch"/>.</description></item>
/// </list>
/// <para>
/// This store is stateless in exactly the sense the real contract requires: it never remembers which
/// caller won a reservation on its own. <see cref="CompleteAsync"/> and <see cref="ReleaseAsync"/>
/// both require the caller to pass back the <see cref="IdempotencyBeginResult.ReservationToken"/> from
/// the winning <see cref="TryBeginAsync"/> call, and return <see langword="false"/> — never throw —
/// when that token no longer owns the reservation (unknown key, a foreign/stale token, or the
/// reservation was already completed/released).
/// </para>
/// <para>
/// <see cref="ReleaseAsync"/> discards the reservation entirely on success, so a later
/// <see cref="TryBeginAsync"/> call for the same key starts fresh (a new
/// <see cref="IdempotencyBeginStatus.Started"/>) — mirroring the real contract's "may be reserved
/// again" rule. Deliberately does NOT simulate the store-defined in-flight reservation TTL the real
/// contract documents: a fake proves behavioral correctness, not timing, and this store exposes no
/// clock seam to drive that expiry deterministically.
/// </para>
/// <para>
/// <b>Keys are stored exactly as given, never scoped here.</b> Through <see cref="IdempotencyBehavior{TRequest,TResponse}"/>
/// the store receives the command's key already scoped to the tenant and the caller (a 64-character lowercase
/// hexadecimal digest), the same key the Redis and EF Core stores receive, so a pipeline test sees the same
/// per-caller separation production does: two callers using one key each get their own reservation. Consequently
/// <see cref="Calls"/> records that scoped key, not the command's raw <see cref="IIdempotentRequest.IdempotencyKey"/>.
/// </para>
/// <para>
/// Local-seam-only scope: fakes <c>05.Application.Behaviors</c>' own <see cref="IRequestIdempotencyStore"/>
/// exclusively and never references <c>18.Idempotency</c> or <c>07.Messaging.Abstractions.IIdempotencyStore</c>
/// (consumer-side message deduplication, an unrelated contract that merely shares a naming pattern).
/// </para>
/// </remarks>
public sealed class FakeRequestIdempotencyStore : IRequestIdempotencyStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly ConcurrentQueue<RecordedCall> _calls = new();
    private int _tokenCounter;

    private readonly record struct Entry(string Fingerprint, bool Completed, string? StoredResponse, string Token);

    /// <summary>One call recorded against this store, in the order it happened.</summary>
    /// <param name="Member">The member invoked: <c>TryBeginAsync</c>, <c>CompleteAsync</c>, or <c>ReleaseAsync</c>.</param>
    /// <param name="Key">
    /// The key the call was made against. Through <see cref="IdempotencyBehavior{TRequest,TResponse}"/> this is the
    /// tenant- and caller-scoped digest, not the command's raw idempotency key.
    /// </param>
    public readonly record struct RecordedCall(string Member, string Key);

    /// <summary>Gets every call made against this store so far, in call order, for test assertions.</summary>
    public IReadOnlyList<RecordedCall> Calls => [.. _calls];

    /// <inheritdoc />
    public Task<IdempotencyBeginResult> TryBeginAsync(string key, string requestFingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(requestFingerprint);

        _calls.Enqueue(new RecordedCall(nameof(TryBeginAsync), key));

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var existing))
            {
                var token = $"fake-token-{++_tokenCounter}";
                _entries[key] = new Entry(requestFingerprint, Completed: false, StoredResponse: null, token);
                return Task.FromResult(IdempotencyBeginResult.Started(token));
            }

            if (existing.Fingerprint != requestFingerprint)
                return Task.FromResult(IdempotencyBeginResult.FingerprintMismatch());

            return Task.FromResult(
                existing.Completed
                    ? IdempotencyBeginResult.Completed(existing.StoredResponse!)
                    : IdempotencyBeginResult.InProgress());
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="false"/> — never throws — when <paramref name="key"/> was never
    /// reserved, was already completed or released, or <paramref name="reservationToken"/> does not
    /// match the token the winning <see cref="TryBeginAsync"/> call returned.
    /// </remarks>
    public Task<bool> CompleteAsync(string key, string reservationToken, string serializedResponse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reservationToken);
        ArgumentNullException.ThrowIfNull(serializedResponse);

        _calls.Enqueue(new RecordedCall(nameof(CompleteAsync), key));

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var existing) || existing.Token != reservationToken || existing.Completed)
                return Task.FromResult(false);

            _entries[key] = existing with { Completed = true, StoredResponse = serializedResponse };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="false"/> — never throws — when <paramref name="key"/> has no
    /// reservation, was already completed or released, or <paramref name="reservationToken"/> does
    /// not match the token the winning <see cref="TryBeginAsync"/> call returned.
    /// </remarks>
    public Task<bool> ReleaseAsync(string key, string reservationToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reservationToken);

        _calls.Enqueue(new RecordedCall(nameof(ReleaseAsync), key));

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var existing) || existing.Token != reservationToken || existing.Completed)
                return Task.FromResult(false);

            _entries.Remove(key);
            return Task.FromResult(true);
        }
    }

    /// <summary>Clears every reservation and the recorded call list.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        _calls.Clear();
    }
}
