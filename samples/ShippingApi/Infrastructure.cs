using System.Collections.Concurrent;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Security.Abstractions;

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
/// DEVELOPMENT-ONLY. It produces the <see cref="IUserContext"/> an authentication package of
/// <c>12.Security</c> would produce — a real service calls <c>AddOidcAuthentication(configuration)</c>
/// instead — so everything downstream is the production path: <c>AddSharedKernelRequestContext()</c> turns it
/// into the one <see cref="IRequestContext"/>, <c>UseSharedKernelRequestContext()</c> puts it (and the request's
/// <c>X-Correlation-Id</c>) in scope for the request, and the bus carries both to the consumer. Headers stand in
/// for a token so the sample needs no identity provider, and so a test can act as two tenants in one process.
/// </para>
/// </remarks>
public static class DemoIdentity
{
    /// <summary>The header carrying the tenant this request acts for.</summary>
    public const string TenantHeader = "X-Demo-Tenant";

    /// <summary>The header carrying the subject id this request acts as.</summary>
    public const string ActorHeader = "X-Demo-Actor";

    /// <summary>Registers a scoped <see cref="IUserContext"/> read from <see cref="TenantHeader"/> and <see cref="ActorHeader"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddDemoIdentity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext>(sp => FromHeaders(sp.GetRequiredService<IHttpContextAccessor>().HttpContext));
        return services;
    }

    private static IUserContext FromHeaders(HttpContext? http)
    {
        string? actor = Header(http, ActorHeader);
        if (actor is null)
        {
            return AnonymousUserContext.Instance;
        }

        return new UserContext(ActorKind.User, actor)
        {
            TenantId = TenantId.TryParse(Header(http, TenantHeader), out var tenantId) ? tenantId : null,
        };
    }

    private static string? Header(HttpContext? http, string name)
    {
        string? value = http?.Request.Headers[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
