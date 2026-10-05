using SharedKernel.Execution.Context;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;

namespace SharedKernel.Messaging.MassTransit.HeaderPropagation;

/// <summary>
/// Built-in <see cref="IMessageHeaderPropagator"/> that copies the tenant of the ambient request context
/// (<see cref="IRequestContextAccessor.Current"/>) onto every outgoing message's
/// <see cref="PublishContext.TenantId"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the second of the named, documented exceptions to "never implement
/// <see cref="IMessageHeaderPropagator"/> inside <c>SharedKernel.*</c> packages" — safe here because
/// the value comes from the platform's own ambient context, which every inbound adapter sets, not from a
/// service-specific seam.
/// </para>
/// <para>
/// Register via <c>MessagingBusBuilder.WithTenantContext()</c>. When no ambient context is open, or it has no
/// tenant, <see cref="Propagate"/> is a provable no-op.
/// </para>
/// </remarks>
public sealed class TenantHeaderPropagator : IMessageHeaderPropagator
{
    private readonly IRequestContextAccessor _accessor;

    /// <summary>
    /// Initializes a new instance of <see cref="TenantHeaderPropagator"/>.
    /// </summary>
    /// <param name="accessor">Reads the ambient request context.</param>
    public TenantHeaderPropagator(IRequestContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>
    /// Sets <see cref="PublishContext.TenantId"/> from the ambient request context's tenant. A no-op when no
    /// context is open or it has no tenant.
    /// </summary>
    /// <param name="context">The <see cref="PublishContext"/> for the outgoing message.</param>
    public void Propagate(PublishContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_accessor.Current?.TenantId is { } tenantId)
            context.WithTenantId(tenantId);
    }
}
