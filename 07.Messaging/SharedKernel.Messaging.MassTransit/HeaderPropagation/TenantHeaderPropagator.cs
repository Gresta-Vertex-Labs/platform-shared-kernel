using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.TenantContext;

namespace SharedKernel.Messaging.MassTransit.HeaderPropagation;

/// <summary>
/// Built-in <see cref="IMessageHeaderPropagator"/> that bridges the ambient tenant identity resolved
/// by an <see cref="ITenantContextAccessor"/> onto every outgoing message's
/// <see cref="PublishContext.TenantId"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the second of two named, documented exceptions to "never implement
/// <see cref="IMessageHeaderPropagator"/> inside <c>SharedKernel.*</c> packages" — safe here because
/// the seam that supplies the actual service-specific value (<see cref="ITenantContextAccessor"/>)
/// is itself bridged by the consuming service, not by this class (P-345/WO-054).
/// </para>
/// <para>
/// Register via <c>MessagingBusBuilder.WithTenantContext&lt;TAccessor&gt;()</c>, which registers both
/// <see cref="ITenantContextAccessor"/>'s implementation and this propagator in one call —
/// the consuming service only ever writes the accessor implementation, never a propagator by hand.
/// </para>
/// <para>
/// <see cref="ITenantContextAccessor"/> is injected as an <em>optional</em> constructor dependency
/// (a nullable parameter with a default value, resolved by the DI container the same way
/// <c>IServiceProvider.GetService&lt;T&gt;()</c> resolves an unregistered service — returning
/// <see langword="null"/> instead of throwing, unlike <c>GetRequiredService&lt;T&gt;()</c>). When
/// <see cref="ITenantContextAccessor"/> is not registered in DI, <see cref="Propagate"/> is a
/// provable no-op.
/// </para>
/// </remarks>
public sealed class TenantHeaderPropagator : IMessageHeaderPropagator
{
    private readonly ITenantContextAccessor? _tenantContextAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="TenantHeaderPropagator"/>.
    /// </summary>
    /// <param name="tenantContextAccessor">
    /// The registered <see cref="ITenantContextAccessor"/>, or <see langword="null"/> when the
    /// consuming service has not registered one via <c>MessagingBusBuilder.WithTenantContext&lt;TAccessor&gt;()</c>.
    /// </param>
    public TenantHeaderPropagator(ITenantContextAccessor? tenantContextAccessor = null)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    /// <summary>
    /// Sets <see cref="PublishContext.TenantId"/> from <see cref="ITenantContextAccessor.TenantId"/>
    /// when an accessor is registered and it resolves a non-null tenant identity. A provable no-op
    /// when no <see cref="ITenantContextAccessor"/> is registered, or when it resolves <see langword="null"/>.
    /// </summary>
    /// <param name="context">The <see cref="PublishContext"/> for the outgoing message.</param>
    public void Propagate(PublishContext context)
    {
        if (_tenantContextAccessor?.TenantId is { } tenantId)
            context.WithTenantId(tenantId);
    }
}
