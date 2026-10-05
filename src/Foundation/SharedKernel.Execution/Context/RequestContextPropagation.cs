using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Execution.Context;

/// <summary>
/// Writes an <see cref="IRequestContext"/> onto an outbound call as headers, and rebuilds it from those headers on
/// the receiving side — the one mapping every transport (HTTP, gRPC, messages, workflows) shares.
/// </summary>
/// <remarks>
/// <para>
/// The headers are <see cref="WellKnownHeaders.CorrelationId"/>, <see cref="WellKnownHeaders.TenantId"/>,
/// <see cref="WellKnownHeaders.ActorId"/>, <see cref="WellKnownHeaders.ActorKind"/> and
/// <see cref="WellKnownHeaders.ClientId"/>. Each transport supplies only how a header is set or read, so every
/// hop agrees on names and formats by construction.
/// </para>
/// <para>
/// Permissions are never written: a receiver treats the identity as attribution only
/// (<see cref="PropagatedRequestContext"/>).
/// </para>
/// </remarks>
public static class RequestContextPropagation
{
    /// <summary>
    /// Writes the correlation id and <paramref name="context"/>'s tenant, actor and client onto an outbound carrier.
    /// </summary>
    /// <typeparam name="TCarrier">The carrier type (request headers, metadata, a header dictionary).</typeparam>
    /// <param name="context">The caller, or <see langword="null"/> when there is none.</param>
    /// <param name="carrier">The carrier to write to.</param>
    /// <param name="setHeader">
    /// Sets one header on the carrier. Called once per header this method writes; it decides whether a value the
    /// caller already set wins (every platform transport keeps the caller's value).
    /// </param>
    /// <param name="correlationId">
    /// The correlation id to write. Defaults to <see cref="CorrelationIds.Current"/> for <paramref name="context"/>;
    /// when neither exists, no correlation header is written.
    /// </param>
    /// <remarks>
    /// Writes nothing it does not know: no tenant header without a tenant, no actor id without a subject, no client
    /// header without a client id. The actor kind is written whenever there is a caller, because
    /// <see cref="ActorKind.Anonymous"/> is a meaningful answer, not a missing one.
    /// </remarks>
    public static void WriteHeaders<TCarrier>(
        IRequestContext? context,
        TCarrier carrier,
        Action<TCarrier, string, string> setHeader,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(setHeader);

        correlationId ??= CorrelationIds.Current(context);
        if (correlationId is not null)
            setHeader(carrier, WellKnownHeaders.CorrelationId, correlationId);

        if (context is null)
            return;

        if (context.TenantId is { } tenantId)
            setHeader(carrier, WellKnownHeaders.TenantId, tenantId.ToString());

        if (context.UserId is { Length: > 0 } userId)
            setHeader(carrier, WellKnownHeaders.ActorId, userId);

        setHeader(carrier, WellKnownHeaders.ActorKind, context.ActorKind.ToString());

        if (context.ClientId is { Length: > 0 } clientId)
            setHeader(carrier, WellKnownHeaders.ClientId, clientId);
    }

    /// <summary>Rebuilds the sending caller from an inbound carrier's headers.</summary>
    /// <typeparam name="TCarrier">The carrier type.</typeparam>
    /// <param name="carrier">The carrier to read from.</param>
    /// <param name="getHeader">Reads one header from the carrier, or returns <see langword="null"/> when absent.</param>
    /// <param name="createCorrelationId">
    /// When <see langword="true"/> (the default), a missing or invalid correlation id is replaced by
    /// <see cref="CorrelationIds.New"/>, so the receiving work always has one; otherwise it stays
    /// <see langword="null"/>.
    /// </param>
    /// <returns>The rebuilt caller. It never grants a permission.</returns>
    /// <remarks>
    /// Never throws on a malformed header: an unparsable tenant becomes "no tenant" (which fails closed in
    /// persistence), an unrecognised actor kind becomes <see cref="ActorKind.Anonymous"/>, and an invalid correlation
    /// id is discarded.
    /// </remarks>
    public static PropagatedRequestContext ReadHeaders<TCarrier>(
        TCarrier carrier,
        Func<TCarrier, string, string?> getHeader,
        bool createCorrelationId = true)
    {
        ArgumentNullException.ThrowIfNull(getHeader);

        TenantId? tenantId = TenantId.TryParse(getHeader(carrier, WellKnownHeaders.TenantId), out TenantId tenant)
            ? tenant
            : null;

        string? correlationId = getHeader(carrier, WellKnownHeaders.CorrelationId);
        if (!CorrelationIds.IsValid(correlationId))
            correlationId = createCorrelationId ? CorrelationIds.New() : null;

        return new PropagatedRequestContext(
            tenantId,
            NullIfEmpty(getHeader(carrier, WellKnownHeaders.ActorId)),
            ParseActorKind(getHeader(carrier, WellKnownHeaders.ActorKind)),
            NullIfEmpty(getHeader(carrier, WellKnownHeaders.ClientId)),
            correlationId);
    }

    /// <summary>
    /// Parses an actor-kind header, defaulting to <see cref="ActorKind.Anonymous"/> for an absent or unrecognised
    /// value.
    /// </summary>
    /// <param name="value">The header value.</param>
    /// <returns>The actor kind.</returns>
    /// <remarks>
    /// Case-sensitive, member names only: <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> would otherwise
    /// accept numeric text such as <c>"7"</c> and return an undefined member, which would reach an audit record as a
    /// kind nothing can render. Anonymous is the safe default because it is the least-privileged member.
    /// </remarks>
    public static ActorKind ParseActorKind(string? value)
    {
        if (string.IsNullOrEmpty(value) || !char.IsAsciiLetter(value[0]))
            return ActorKind.Anonymous;

        return Enum.TryParse(value, ignoreCase: false, out ActorKind actorKind) && Enum.IsDefined(actorKind)
            ? actorKind
            : ActorKind.Anonymous;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
