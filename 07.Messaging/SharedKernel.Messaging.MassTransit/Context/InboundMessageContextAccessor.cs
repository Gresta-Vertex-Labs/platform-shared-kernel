using SharedKernel.Application.Context;
using SharedKernel.Messaging.Abstractions.Context;

namespace SharedKernel.Messaging.MassTransit.Context;

/// <summary>
/// The per-delivery holder behind <see cref="IInboundMessageContextAccessor"/>: the consume filter
/// writes the identity it read off the message, everything else in the same scope reads it.
/// </summary>
/// <remarks>
/// <para>
/// Registered as scoped, so one instance exists per MassTransit delivery scope. That is what makes
/// a mutable field safe here: two messages consumed concurrently resolve two different instances,
/// and nothing outside the delivery can observe either.
/// </para>
/// <para>
/// <see cref="Set"/> is deliberately not on the public interface. A consumer reads the identity; it
/// does not get to change it mid-delivery, which would silently re-attribute writes already made.
/// </para>
/// </remarks>
internal sealed class InboundMessageContextAccessor : IInboundMessageContextAccessor
{
    /// <inheritdoc />
    public IRequestContext? Current { get; private set; }

    /// <summary>Records the identity for this delivery. Called once, by the consume filter.</summary>
    /// <param name="context">The identity rebuilt from the message's headers.</param>
    public void Set(IRequestContext context) => Current = context;
}
