using SharedKernel.Idempotency.Abstractions;

namespace SharedKernel.Idempotency.EfCore.Tests.Support;

/// <summary>
/// The store seen the way <c>IdempotencyBehavior</c> uses it: purpose <see cref="IdempotencyPurpose.Request"/>, a fixed
/// in-flight TTL and a fixed retention. Keeps the carried-over request tests readable.
/// </summary>
internal sealed class RequestView(IIdempotencyStore store, TimeSpan ttl)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    public Task<IdempotencyReservation> TryBeginAsync(string key, string fingerprint, CancellationToken ct) =>
        store.TryBeginAsync(IdempotencyPurpose.Request, key, fingerprint, ttl, ct);

    public Task<bool> CompleteAsync(string key, string token, string response, CancellationToken ct) =>
        store.CompleteAsync(IdempotencyPurpose.Request, key, token, response, Retention, ct);

    public Task<bool> ReleaseAsync(string key, string token, CancellationToken ct) =>
        store.ReleaseAsync(IdempotencyPurpose.Request, key, token, ct);
}

/// <summary>
/// The store seen the way MassTransit's consumer idempotency uses it: purpose <see cref="IdempotencyPurpose.Message"/>,
/// the message id in "D" form as key, a fixed fingerprint and no stored response.
/// </summary>
internal sealed class MessageView(IIdempotencyStore store, TimeSpan ttl)
{
    public const string Fingerprint = "message";
    public static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    public Task<IdempotencyReservation> TryBeginAsync(Guid messageId, CancellationToken ct) =>
        store.TryBeginAsync(IdempotencyPurpose.Message, messageId.ToString("D"), Fingerprint, ttl, ct);

    public Task<bool> CompleteAsync(Guid messageId, string token, CancellationToken ct) =>
        store.CompleteAsync(IdempotencyPurpose.Message, messageId.ToString("D"), token, null, Retention, ct);

    public Task<bool> ReleaseAsync(Guid messageId, string token, CancellationToken ct) =>
        store.ReleaseAsync(IdempotencyPurpose.Message, messageId.ToString("D"), token, ct);
}
