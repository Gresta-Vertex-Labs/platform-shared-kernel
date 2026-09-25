using SharedKernel.Execution.Context;

namespace SharedKernel.Messaging.Abstractions.Context;

/// <summary>
/// Exposes the identity carried by the message currently being consumed on this scope, or
/// <see langword="null"/> when the current scope is not a consume.
/// </summary>
/// <remarks>
/// <para>
/// Registered as <em>scoped</em> by <c>MessagingBusBuilder.WithRequestContextPropagation()</c>.
/// MassTransit creates one dependency-injection scope per message delivery, so the value is set
/// once per delivery and never leaks between concurrently-consumed messages.
/// </para>
/// <para>
/// Most code should never inject this. <c>WithRequestContextPropagation()</c> also registers
/// <c>IRequestContext</c> so that it resolves to the inbound identity inside a consume and to
/// whatever the service had registered before — typically <c>13.ServiceDefaults</c>'s HTTP-backed
/// one — everywhere else. Injecting <c>IRequestContext</c> therefore just works in a handler that
/// runs on both paths. Inject this only to branch on <em>whether</em> the current work arrived over
/// the bus.
/// </para>
/// </remarks>
public interface IInboundMessageContextAccessor
{
    /// <summary>
    /// Gets the identity the message currently being consumed carried, or <see langword="null"/>
    /// outside a consume.
    /// </summary>
    /// <remarks>
    /// Non-<see langword="null"/> for every message consumed once the feature is enabled, including
    /// one that carried no identity headers at all — in that case it is a
    /// <see cref="MessageRequestContext"/> with no tenant and no user, which is meaningfully
    /// different from "not consuming".
    /// </remarks>
    IRequestContext? Current { get; }
}
