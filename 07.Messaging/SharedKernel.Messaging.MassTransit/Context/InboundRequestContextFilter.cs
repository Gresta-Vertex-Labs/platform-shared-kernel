using SharedKernel.Execution.Tenancy;
using MassTransit;
using SharedKernel.Execution.Context;
using SharedKernel.Messaging.Abstractions.Context;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Messaging.MassTransit.Context;

/// <summary>
/// Consume filter that rebuilds the publishing caller's identity from the message's transport
/// headers and publishes it to the delivery scope, so the consumer's <c>IRequestContext</c>
/// resolves to whoever caused the message rather than to nobody.
/// </summary>
/// <typeparam name="TMessage">The consumed message type.</typeparam>
/// <remarks>
/// <para>
/// Runs ahead of every other consume filter, including idempotency: a tenant-partitioned
/// idempotency store needs the tenant before it reserves the message id.
/// </para>
/// <para>
/// Never throws on a malformed header. A message whose tenant header is not a GUID is consumed
/// with no tenant, which fails closed in persistence, rather than being sent round the retry loop
/// to a poison queue — the payload is fine, only the attribution is not, and a message that can
/// never succeed should not be retried.
/// </para>
/// </remarks>
internal sealed class InboundRequestContextFilter<TMessage> : IFilter<ConsumeContext<TMessage>>
    where TMessage : class
{
    private readonly InboundMessageContextAccessor _accessor;

    /// <summary>Initialises the filter with the delivery scope's identity holder.</summary>
    /// <param name="accessor">The scoped holder this filter writes to.</param>
    public InboundRequestContextFilter(InboundMessageContextAccessor accessor)
    {
        _accessor = accessor;
    }

    /// <inheritdoc />
    public void Probe(ProbeContext context)
        => context.CreateFilterScope("inbound-request-context");

    /// <inheritdoc />
    public Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        _accessor.Set(new MessageRequestContext(
            ReadTenantId(context),
            context.Headers.Get<string>(MessageContextHeaders.ActorId),
            ReadActorKind(context),
            context.Headers.Get<string>(MessageContextHeaders.ClientId)));

        return next.Send(context);
    }

    private static TenantId? ReadTenantId(ConsumeContext context)
        => TenantId.TryParse(context.Headers.Get<string>(WellKnownHeaders.TenantId), out var tenantId)
            ? tenantId
            : null;

    /// <summary>
    /// Reads the actor kind, defaulting to <see cref="ActorKind.Anonymous"/> for an absent or
    /// unrecognised value.
    /// </summary>
    /// <remarks>
    /// Parsed case-sensitively and rejecting numeric text (<c>ignoreCase: false</c> plus the
    /// <see cref="Enum.IsDefined{TEnum}(TEnum)"/> check): <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
    /// otherwise accepts <c>"7"</c> and hands back an undefined enum value, which would reach an
    /// audit record as a kind nothing can render. Anonymous is the safe default because it is the
    /// least-privileged member.
    /// </remarks>
    private static ActorKind ReadActorKind(ConsumeContext context)
        => Enum.TryParse<ActorKind>(
               context.Headers.Get<string>(MessageContextHeaders.ActorKind),
               ignoreCase: false,
               out var actorKind)
           && Enum.IsDefined(actorKind)
            ? actorKind
            : ActorKind.Anonymous;
}
