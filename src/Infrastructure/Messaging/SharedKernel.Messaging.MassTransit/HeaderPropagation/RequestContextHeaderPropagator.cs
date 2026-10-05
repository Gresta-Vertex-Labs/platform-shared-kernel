using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Messaging.MassTransit.HeaderPropagation;

/// <summary>
/// Built-in <see cref="IMessageHeaderPropagator"/> that writes the current caller — correlation id, tenant, actor
/// and client — onto every outgoing message, so the consumer can rebuild it.
/// </summary>
/// <remarks>
/// <para>
/// The publish half of <c>MessagingBusBuilder.WithInboundRequestContext()</c>, which registers it. Uses the same
/// header mapping as every other transport (<see cref="RequestContextPropagation"/>), so a message and an HTTP call
/// carry the caller under identical names. It is one of the named exceptions to "never implement
/// <see cref="IMessageHeaderPropagator"/> inside <c>SharedKernel.*</c>", safe because the value comes from the
/// platform's own caller contract, not from anything invented here.
/// </para>
/// <para>
/// The caller is the ambient context (<see cref="IRequestContextAccessor.Current"/>) when one is open, otherwise the
/// scope's <c>IRequestContext</c>. Published from inside a consumer, this carries the <em>original</em> caller and
/// correlation id onward rather than re-stamping the message, because the consume filter made that caller ambient.
/// A chain of consumers therefore keeps attributing work to the human or service that started it.
/// </para>
/// <para>
/// Writes nothing it does not know: no tenant header without a tenant, no actor id without a subject.
/// </para>
/// </remarks>
public sealed class RequestContextHeaderPropagator : IMessageHeaderPropagator
{
    private readonly IRequestContextAccessor? _accessor;
    private readonly IRequestContext? _requestContext;

    /// <summary>Initialises the propagator.</summary>
    /// <param name="accessor">Reads the ambient request context, or <see langword="null"/>.</param>
    /// <param name="requestContext">
    /// The scope's caller, used when no ambient context is open, or <see langword="null"/> when the service
    /// registers none — in which case <see cref="Propagate"/> writes only an ambient correlation id, if any.
    /// </param>
    public RequestContextHeaderPropagator(
        IRequestContextAccessor? accessor = null,
        IRequestContext? requestContext = null)
    {
        _accessor = accessor;
        _requestContext = requestContext;
    }

    /// <summary>
    /// Copies the caller's correlation id, tenant, subject, actor kind and client id onto <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The <see cref="PublishContext"/> for the outgoing message.</param>
    /// <remarks>
    /// Runs before the caller's own configure callback, so an explicit
    /// <see cref="PublishContext.WithTenantId"/> on a specific publish still wins — which is how a
    /// background job publishes on behalf of a tenant it is not itself scoped to.
    /// </remarks>
    public void Propagate(PublishContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var caller = _accessor?.Current ?? RequestContextScope.Current ?? _requestContext;

        RequestContextPropagation.WriteHeaders(caller, context, static (publish, name, value) =>
        {
            if (name == WellKnownHeaders.TenantId)
            {
                if (TenantId.TryParse(value, out var tenantId))
                    publish.WithTenantId(tenantId);
            }
            else
            {
                publish.WithHeader(name, value);
            }
        });
    }
}
