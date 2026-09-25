using MassTransit;
using SharedKernel.Execution.Context;

namespace SharedKernel.Messaging.MassTransit.Context;

/// <summary>
/// Consume filter that rebuilds the publishing caller — tenant, actor, client and correlation id — from the
/// message's transport headers, publishes it to the delivery scope, and runs the consumer inside a
/// <see cref="RequestContextScope"/> carrying it.
/// </summary>
/// <typeparam name="TMessage">The consumed message type.</typeparam>
/// <remarks>
/// <para>
/// <b>Why the scope matters.</b> The consumer's <c>IRequestContext</c> resolves to the rebuilt caller through the
/// delivery scope, but code with no DI scope of its own — an outbound REST or gRPC client, a workflow dispatch, a
/// log enricher — reads the ambient context through <see cref="IRequestContextAccessor"/>. Without the scope, a
/// REST call made from a consumer left with no tenant and a fresh correlation id (defect 3, P-566).
/// </para>
/// <para>
/// Runs ahead of every other consume filter, including idempotency: a tenant-partitioned
/// idempotency store needs the tenant before it reserves the message id.
/// </para>
/// <para>
/// Never throws on a malformed header. A message whose tenant header is not a GUID is consumed with no tenant,
/// which fails closed in persistence, rather than being sent round the retry loop to a poison queue — the payload
/// is fine, only the attribution is not, and a message that can never succeed should not be retried. A missing or
/// invalid correlation id is replaced by a new one, so the consumer's work is always correlated.
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
    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        var caller = RequestContextPropagation.ReadHeaders(
            context.Headers,
            static (headers, name) => headers.Get<string>(name));

        _accessor.Set(caller);

        using (RequestContextScope.Begin(caller))
        {
            await next.Send(context).ConfigureAwait(false);
        }
    }
}
