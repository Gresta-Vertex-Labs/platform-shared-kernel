using System.Runtime.Serialization;
using System.Text.Json;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

// Every MassTransit type here is aliased from the global namespace on purpose. This assembly's own
// root namespace is SharedKernel.Messaging.MassTransit, so inside it a bare `MassTransit.X` — and
// even a plain `using MassTransit;` — binds to *this* assembly's namespace tree rather than the
// library's, and the library type silently fails to resolve. The same trap is aliased around in
// MessagingBusBuilder for MassTransit.Configuration.
using MtConnectionException = global::MassTransit.ConnectionException;
using MtRequestTimeoutException = global::MassTransit.RequestTimeoutException;
using MtTransportException = global::MassTransit.TransportException;
using MtTransportUnavailableException = global::MassTransit.TransportUnavailableException;

namespace SharedKernel.Messaging.MassTransit.Internal;

/// <summary>
/// Maps a MassTransit transport exception onto the <c>messaging.*</c> failure contract.
/// </summary>
/// <remarks>
/// <para>
/// The single translation point for <c>IMessageBus</c> and <c>IEventPublisher</c>, so the two
/// dispatch surfaces cannot drift into classifying the same broker fault differently.
/// </para>
/// <para>
/// Deliberately conservative: only faults the caller can act on are converted. Anything
/// unrecognised is rethrown with its stack intact rather than flattened into a
/// <c>messaging.unavailable</c>, because turning an unknown defect into a retryable-looking
/// Result is how a real bug gets retried forever instead of being reported.
/// </para>
/// </remarks>
internal static class MessagingExceptionClassifier
{
    /// <summary>
    /// Returns the <see cref="Error"/> for a recognised transport fault, or
    /// <see langword="null"/> when the exception is not an operational fault and must be rethrown.
    /// </summary>
    /// <param name="exception">The exception thrown by the transport.</param>
    /// <param name="messageTypeName">CLR type name of the message, for the error message.</param>
    /// <param name="operation">The dispatch verb, e.g. <c>"publish"</c> or <c>"send"</c>.</param>
    /// <returns>The mapped error, or <see langword="null"/> when unrecognised.</returns>
    public static Error? TryClassify(Exception exception, string messageTypeName, string operation) => exception switch
    {
        // The payload could not be written to the wire, including a failure inside the configured
        // compression/encryption payload transform. These are the BCL types the serializers
        // actually throw — MassTransit 8.5.x declares no SerializationException of its own, so
        // matching on a MassTransit-namespaced one would never fire.
        SerializationException or JsonException => MessagingErrors.SerializationFailed(messageTypeName),

        // The broker could not be reached, or did not answer in time. Retryable unchanged.
        MtRequestTimeoutException or TimeoutException => MessagingErrors.Unavailable(operation),
        MtConnectionException or MtTransportUnavailableException => MessagingErrors.Unavailable(operation),

        // Reachable, but this specific message was refused — unroutable, oversized, or policy-rejected.
        MtTransportException => MessagingErrors.PublishRejected(messageTypeName),

        _ => null,
    };
}
