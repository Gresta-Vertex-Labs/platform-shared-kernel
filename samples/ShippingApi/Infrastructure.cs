using System.Collections.Concurrent;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Idempotency.Abstractions;

namespace ShippingApi;

/// <summary>
/// An <see cref="IIdempotencyStore"/> kept in this process, so the sample can demonstrate
/// at-most-once consumption without a second piece of infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not a template for production.</strong> Deduplication is only as wide as the store, and
/// this one is one process: two replicas would each consume the same message once. A real service
/// registers <c>SharedKernel.Idempotency.Redis</c> or <c>.EfCore</c>, both of which reserve
/// atomically across replicas. It is here because the alternative — standing up Redis in a sample
/// about messaging — would obscure what the sample is for.
/// </para>
/// <para>
/// The reservation protocol itself is implemented faithfully, because a store that only pretended
/// to would make the sample misleading: a started reservation issues a token, a second attempt on
/// an in-flight id reports <see cref="IdempotencyReservationStatus.InProgress"/>, a completed one
/// reports <see cref="IdempotencyReservationStatus.Completed"/>, and a release makes the id
/// available again so a failed consume can be retried. Keys are scoped by purpose and by the tenant
/// of the ambient request context, as the real stores scope them. Time is not modelled: a
/// reservation never expires.
/// </para>
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly IRequestContextAccessor _requestContextAccessor = new RequestContextAccessor();
    private readonly ConcurrentDictionary<(string Scope, IdempotencyPurpose Purpose, string Key), Entry> _entries = new();

    /// <inheritdoc />
    public Task<IdempotencyReservation> TryBeginAsync(
        IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        Entry entry = _entries.GetOrAdd(Id(purpose, key), _ => new Entry(token, fingerprint, Completed: false, Response: null));

        // GetOrAdd returns what is in the map: our own entry when this call won the race, someone
        // else's when it did not. The token identifies which, and only the winner may consume.
        if (entry.Token == token)
        {
            return Task.FromResult(IdempotencyReservation.Started(token));
        }

        if (entry.Fingerprint != fingerprint)
        {
            return Task.FromResult(IdempotencyReservation.FingerprintMismatch());
        }

        return Task.FromResult(entry.Completed
            ? IdempotencyReservation.Completed(entry.Response)
            : IdempotencyReservation.InProgress());
    }

    /// <inheritdoc />
    public Task<bool> CompleteAsync(
        IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken cancellationToken)
    {
        var id = Id(purpose, key);
        var completed = _entries.TryGetValue(id, out var entry)
            && entry.Token == token
            && !entry.Completed
            && _entries.TryUpdate(id, entry with { Completed = true, Response = response }, entry);

        return Task.FromResult(completed);
    }

    /// <inheritdoc />
    public Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken cancellationToken)
    {
        // Conditional on the token: a release must never drop a reservation another delivery holds.
        var id = Id(purpose, key);
        var released = _entries.TryGetValue(id, out var entry)
            && entry.Token == token
            && !entry.Completed
            && _entries.TryRemove(new KeyValuePair<(string, IdempotencyPurpose, string), Entry>(id, entry));

        return Task.FromResult(released);
    }

    private (string, IdempotencyPurpose, string) Id(IdempotencyPurpose purpose, string key) =>
        (IdempotencyTenantScope.Current(_requestContextAccessor), purpose, key);

    private sealed record Entry(string Token, string Fingerprint, bool Completed, string? Response);
}

/// <summary>
/// The caller identity this sample's HTTP endpoints act under, taken from two request headers.
/// </summary>
/// <remarks>
/// <para>
/// A real service registers <c>13.ServiceDefaults</c>' <c>AddSharedKernelRequestContext()</c>, which
/// builds this from the authenticated principal. Headers stand in for that here so the sample needs
/// no identity provider — and so a test can act as two different tenants in one process.
/// </para>
/// <para>
/// It is registered <em>before</em> the messaging builder, which is the order
/// <c>WithInboundRequestContext()</c> requires: the container resolves the last
/// <c>IRequestContext</c> registered, and the message-aware one has to be that.
/// </para>
/// </remarks>
public sealed class HeaderRequestContext : IRequestContext
{
    /// <summary>The header carrying the tenant this request acts for.</summary>
    public const string TenantHeader = "X-Demo-Tenant";

    /// <summary>The header carrying the subject id this request acts as.</summary>
    public const string ActorHeader = "X-Demo-Actor";

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initialises the context.</summary>
    /// <param name="httpContextAccessor">Access to the current request, if there is one.</param>
    public HeaderRequestContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public bool IsAuthenticated => UserId is not null;

    /// <inheritdoc />
    public string? UserId => Header(ActorHeader);

    /// <inheritdoc />
    public TenantId? TenantId => SharedKernel.Execution.Tenancy.TenantId.TryParse(Header(TenantHeader), out var tenantId) ? tenantId : null;

    /// <inheritdoc />
    public ActorKind ActorKind => IsAuthenticated ? ActorKind.User : ActorKind.Anonymous;

    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(IsAuthenticated);

    private string? Header(string name)
    {
        string? value = _httpContextAccessor.HttpContext?.Request.Headers[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
