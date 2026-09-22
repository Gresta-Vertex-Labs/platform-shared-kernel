using SharedKernel.Application.Context;
using SharedKernel.Messaging.Abstractions.Context;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;

namespace SharedKernel.Messaging.MassTransit.HeaderPropagation;

/// <summary>
/// Built-in <see cref="IMessageHeaderPropagator"/> that writes the current caller's tenant and
/// actor onto every outgoing message, so the consumer can rebuild them.
/// </summary>
/// <remarks>
/// <para>
/// The publish half of <c>MessagingBusBuilder.WithInboundRequestContext()</c>, which registers it.
/// It is the third named exception to "never implement <see cref="IMessageHeaderPropagator"/>
/// inside <c>SharedKernel.*</c>", and safe for the same reason as the other two: the
/// service-specific value comes from a seam the consuming service owns —
/// <c>IRequestContext</c> — not from anything invented here.
/// </para>
/// <para>
/// Published from inside a consumer, this carries the <em>original</em> caller onward rather than
/// re-stamping the message as anonymous, because the registered <c>IRequestContext</c> is itself
/// message-aware. A chain of consumers therefore keeps attributing work to the human or service
/// that started it.
/// </para>
/// <para>
/// Writes nothing it does not know: an anonymous caller produces no headers at all, rather than
/// headers with empty values that a consumer would have to distinguish from absent ones.
/// </para>
/// </remarks>
public sealed class RequestContextHeaderPropagator : IMessageHeaderPropagator
{
    private readonly IRequestContext? _requestContext;

    /// <summary>
    /// Initialises the propagator.
    /// </summary>
    /// <param name="requestContext">
    /// The current caller, or <see langword="null"/> when the service registers no
    /// <c>IRequestContext</c> — in which case <see cref="Propagate"/> is a provable no-op.
    /// </param>
    public RequestContextHeaderPropagator(IRequestContext? requestContext = null)
    {
        _requestContext = requestContext;
    }

    /// <summary>
    /// Copies the caller's tenant, subject, actor kind and client id onto
    /// <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The <see cref="PublishContext"/> for the outgoing message.</param>
    /// <remarks>
    /// Runs before the caller's own configure callback, so an explicit
    /// <see cref="PublishContext.WithTenantId"/> on a specific publish still wins — which is how a
    /// background job publishes on behalf of a tenant it is not itself scoped to.
    /// </remarks>
    public void Propagate(PublishContext context)
    {
        if (_requestContext is null)
            return;

        if (_requestContext.TenantId is { } tenantId)
            context.WithTenantId(tenantId);

        if (_requestContext.UserId is { Length: > 0 } userId)
            context.WithHeader(MessageContextHeaders.ActorId, userId);

        // Always written when there is any identity at all: a consumer that sees an actor id but
        // no kind would have to guess, and "Anonymous" is a meaningful answer, not a missing one.
        context.WithHeader(MessageContextHeaders.ActorKind, _requestContext.ActorKind.ToString());

        if (_requestContext.ClientId is { Length: > 0 } clientId)
            context.WithHeader(MessageContextHeaders.ClientId, clientId);
    }
}
