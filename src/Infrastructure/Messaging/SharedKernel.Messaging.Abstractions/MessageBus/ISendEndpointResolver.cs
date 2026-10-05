namespace SharedKernel.Messaging.Abstractions.MessageBus;

/// <summary>
/// Resolves the point-to-point endpoint address a command type is sent to.
/// </summary>
/// <remarks>
/// Implemented by the transport package against its own naming convention. Returns a
/// <see cref="Uri"/> rather than a bare string (P-560): an endpoint address carries a scheme —
/// <c>queue:</c>, <c>exchange:</c>, or a fully qualified transport URI — and returning a string
/// forced every caller to re-concatenate that scheme, which is how a naming convention silently
/// drifts between the resolver and the code that consumes it.
/// </remarks>
public interface ISendEndpointResolver
{
    /// <summary>Resolves the endpoint address for command type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The command type being sent.</typeparam>
    /// <returns>The endpoint address, scheme included.</returns>
    Uri Resolve<T>() where T : class;
}
