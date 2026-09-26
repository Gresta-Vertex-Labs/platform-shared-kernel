using SharedKernel.Messaging.Abstractions.EventPublisher;

namespace SharedKernel.Messaging.Abstractions.HeaderPropagation;

/// <summary>
/// Reads values from ambient scope and populates outgoing message headers via
/// <see cref="PublishContext.WithHeader"/> at publish time.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface in the consuming service's composition root to propagate
/// cross-cutting headers such as tenant ID, feature flag state, or additional correlation
/// identifiers to every outbound message without manual population at every call site.
/// </para>
/// <para>
/// <strong>Precedence rule:</strong> Propagators run <em>before</em> the explicit
/// <see cref="Action{T}"/> configure callback supplied to
/// <c>IMessageBus.PublishAsync</c> or <c>IEventPublisher.PublishAsync</c>.
/// When a caller supplies an explicit configure callback <em>and</em> a propagator sets
/// the same header key, the explicit callback wins. Per-call explicit overrides always
/// take precedence over propagated ambient values.
/// </para>
/// <para>
/// Propagators are registered as <strong>scoped</strong> services via
/// <c>MessagingBusBuilder.WithHeaderPropagator&lt;T&gt;()</c>. Multiple propagators
/// are applied in registration order. Do not implement propagators inside
/// <c>SharedKernel.*</c> packages — they require access to service-specific ambient
/// context (e.g., <c>IHttpContextAccessor</c>, tenant resolution, feature flag state)
/// that does not exist in SharedKernel.
/// </para>
/// <para>
/// <strong>Example:</strong>
/// </para>
/// <code>
/// public sealed class FeatureFlagHeaderPropagator : IMessageHeaderPropagator
/// {
///     private readonly IFeatureFlagSnapshot _flags;
///     public FeatureFlagHeaderPropagator(IFeatureFlagSnapshot flags)
///         => _flags = flags;
///
///     public void Propagate(PublishContext context)
///         => context.WithHeader("x-sk-flags", _flags.ToHeaderValue());
/// }
/// </code>
/// </remarks>
public interface IMessageHeaderPropagator
{
    /// <summary>
    /// Reads ambient values and writes headers into <paramref name="context"/>.
    /// </summary>
    /// <param name="context">
    /// The <see cref="PublishContext"/> for the outgoing message.
    /// Call <see cref="PublishContext.WithHeader"/> to add or overwrite headers.
    /// </param>
    void Propagate(PublishContext context);
}
