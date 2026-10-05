using SharedKernel.Execution.Context;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Messaging.MassTransit.HeaderPropagation;

/// <summary>
/// Built-in <see cref="IMessageHeaderPropagator"/> that writes the ambient caller's correlation id onto every
/// outgoing message as the <see cref="WellKnownHeaders.CorrelationId"/> header.
/// </summary>
/// <remarks>
/// <para>
/// The value is <see cref="CorrelationIds.Current"/> for <see cref="IRequestContextAccessor.Current"/> — the
/// <c>X-Correlation-Id</c> the inbound adapter accepted or created — never <c>Activity.Current</c>'s trace id,
/// which a consumer's own trace replaces (defect 4, P-566). When the id is a GUID it also becomes MassTransit's
/// transport correlation id.
/// </para>
/// <para>
/// This is one of the named, documented exceptions to "never implement <see cref="IMessageHeaderPropagator"/>
/// inside <c>SharedKernel.*</c> packages": the value comes from the platform's own ambient context, which every
/// inbound adapter sets, not from a service-specific seam.
/// </para>
/// <para>
/// Register via <c>MessagingBusBuilder.WithAmbientCorrelationPropagation()</c>. A no-op when the operation has no
/// correlation id.
/// </para>
/// </remarks>
public sealed class AmbientCorrelationHeaderPropagator : IMessageHeaderPropagator
{
    private readonly IRequestContextAccessor? _accessor;

    /// <summary>Initialises the propagator.</summary>
    /// <param name="accessor">
    /// Reads the ambient request context. When <see langword="null"/>, <see cref="RequestContextScope.Current"/> is
    /// read directly.
    /// </param>
    public AmbientCorrelationHeaderPropagator(IRequestContextAccessor? accessor = null)
    {
        _accessor = accessor;
    }

    /// <summary>
    /// Sets the <see cref="WellKnownHeaders.CorrelationId"/> header from the ambient caller's correlation id.
    /// </summary>
    /// <param name="context">The <see cref="PublishContext"/> for the outgoing message.</param>
    public void Propagate(PublishContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (CorrelationIds.Current(_accessor?.Current ?? RequestContextScope.Current) is { } correlationId)
            context.WithHeader(WellKnownHeaders.CorrelationId, correlationId);
    }
}
