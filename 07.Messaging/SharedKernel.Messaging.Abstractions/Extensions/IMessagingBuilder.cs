using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Messaging.Abstractions.Extensions;

/// <summary>
/// The transport-neutral shape of the builder <c>AddSharedKernelMessaging()</c> returns, so a
/// future transport package can offer its own fluent surface without this package knowing about it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Consuming services do not name this type.</strong> Chain off the concrete
/// <c>MessagingBusBuilder</c> that <c>SharedKernel.Messaging.MassTransit</c> returns — every
/// configuration method lives there, and typing a variable as <see cref="IMessagingBuilder"/> hides
/// all of them.
/// </para>
/// <para>
/// It exists so that <c>07.Messaging.Abstractions</c> can describe "there is a builder" without
/// taking a transport dependency, which is the same reason it declares
/// <see cref="Abstractions.MessageBus.IMessageBus"/> rather than a MassTransit type.
/// </para>
/// </remarks>
public interface IMessagingBuilder
{
    /// <summary>
    /// Gets the service collection being configured, for registrations the fluent API does not
    /// cover.
    /// </summary>
    /// <remarks>
    /// An escape hatch, not the intended path: anything registered here bypasses the builder's own
    /// ordering and validation. Reach for it when integrating something the platform does not model
    /// yet — and consider whether the platform should model it.
    /// </remarks>
    IServiceCollection Services { get; }
}
