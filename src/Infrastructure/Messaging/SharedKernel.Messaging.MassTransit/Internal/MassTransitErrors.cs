using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Messaging.MassTransit.Internal;

/// <summary>
/// The canonical <see cref="Error"/> values of this package, each paired with its <see cref="MessagingErrorCodes"/>
/// code and <see cref="ErrorType"/>.
/// </summary>
/// <remarks>
/// Internal on purpose: every producer of these errors is a type in this package, so the factory has no reason to be
/// public; the codes stay public constants a caller can compare against. No message embeds the text of an inner
/// exception or of another <see cref="Error"/>; every message is a sentence ending in a period.
/// </remarks>
internal static class MassTransitErrors
{
    /// <summary>The broker was unreachable or the operation timed out; the caller may retry.</summary>
    /// <param name="operation">The dispatch verb that failed, for example <c>"publish"</c> or <c>"send"</c>.</param>
    /// <returns>An <see cref="ErrorType.Unavailable"/> error carrying <see cref="MessagingErrorCodes.Unavailable"/>.</returns>
    /// <remarks>
    /// <see cref="ErrorType.Unavailable"/>, so the HTTP boundary answers 503 rather than 500. A dispatch that timed out
    /// waiting for the broker returns this error too, not a timeout; whether that message reached the broker is unknown,
    /// so retry only what the consumer can deduplicate.
    /// </remarks>
    public static Error Unavailable(string operation) =>
        Error.Unavailable(
            MessagingErrorCodes.Unavailable,
            $"The message broker is unavailable, so the '{operation}' can be retried later.");

    /// <summary>No endpoint could be resolved for the command type being sent.</summary>
    /// <param name="messageType">The CLR type name of the command.</param>
    /// <param name="endpoint">The endpoint address that could not be resolved.</param>
    /// <returns>An <see cref="ErrorType.NotFound"/> error carrying <see cref="MessagingErrorCodes.EndpointNotFound"/>.</returns>
    public static Error EndpointNotFound(string messageType, string endpoint) =>
        Error.NotFound(
            MessagingErrorCodes.EndpointNotFound,
            $"No endpoint '{endpoint}' is available for command '{messageType}'.");

    /// <summary>The message could not be serialized, or a payload transform failed.</summary>
    /// <param name="messageType">The CLR type name of the message.</param>
    /// <returns>An <see cref="ErrorType.Unexpected"/> error carrying <see cref="MessagingErrorCodes.SerializationFailed"/>.</returns>
    public static Error SerializationFailed(string messageType) =>
        Error.Unexpected(
            MessagingErrorCodes.SerializationFailed,
            $"Message '{messageType}' could not be serialized for the transport.");

    /// <summary>The broker rejected this specific message.</summary>
    /// <param name="messageType">The CLR type name of the message.</param>
    /// <returns>An <see cref="ErrorType.Unexpected"/> error carrying <see cref="MessagingErrorCodes.PublishRejected"/>.</returns>
    public static Error PublishRejected(string messageType) =>
        Error.Unexpected(
            MessagingErrorCodes.PublishRejected,
            $"The broker rejected message '{messageType}'.");

    /// <summary>The integration event failed pre-dispatch validation.</summary>
    /// <param name="messageType">The CLR type name of the event.</param>
    /// <param name="cause">
    /// The <see cref="ArgumentException"/> thrown by <c>EventEnvelope.Wrap</c>. Only its <see cref="ArgumentException.ParamName"/>
    /// is read, to choose one of a closed set of reasons; its message never reaches the error.
    /// </param>
    /// <returns>An <see cref="ErrorType.Validation"/> error carrying <see cref="MessagingErrorCodes.InvalidMessage"/>.</returns>
    public static Error InvalidMessage(string messageType, ArgumentException cause) =>
        Error.Validation(
            MessagingErrorCodes.InvalidMessage,
            $"Integration event '{messageType}' is invalid because {InvalidReason(cause.ParamName)}.");

    /// <summary>The event type carries no valid <c>[IntegrationEvent]</c> attribute.</summary>
    /// <param name="messageType">The CLR type name of the event.</param>
    /// <returns>An <see cref="ErrorType.Validation"/> error carrying <see cref="MessagingErrorCodes.ContractViolation"/>.</returns>
    public static Error ContractViolation(string messageType) =>
        Error.Validation(
            MessagingErrorCodes.ContractViolation,
            $"Integration event '{messageType}' has no valid [IntegrationEvent] attribute, so its CloudEvents type and data version cannot be resolved.");

    /// <summary>Another delivery of the same message id holds the consumer's reservation.</summary>
    /// <param name="messageId">The contended message identifier.</param>
    /// <param name="messageType">The message CLR type being consumed.</param>
    /// <returns>A <see cref="ErrorType.Conflict"/> error carrying <see cref="MessagingErrorCodes.ConcurrentDelivery"/>.</returns>
    public static Error ConcurrentDelivery(Guid messageId, Type messageType) =>
        Error.Conflict(
            MessagingErrorCodes.ConcurrentDelivery,
            $"Message '{messageId}' of type '{messageType.Name}' is already being consumed by another delivery, so it is left unacknowledged for the broker to redeliver.");

    /// <summary>The payload transform of an incoming message could not be reversed.</summary>
    /// <returns>An <see cref="ErrorType.Unexpected"/> error carrying <see cref="MessagingErrorCodes.PayloadTransformMismatch"/>.</returns>
    public static Error PayloadTransformMismatch() =>
        Error.Unexpected(
            MessagingErrorCodes.PayloadTransformMismatch,
            "The payload transform of an incoming message could not be reversed, so make the publisher's and this consumer's PayloadTransformOptions agree on EnableCompression and EnableEncryption.");

    // The closed set of reasons. The parameter names are the ones EventEnvelope.Wrap reports.
    private static string InvalidReason(string? parameterName) => parameterName switch
    {
        "integrationEvent" => "its EventId or OccurredOn is unset, or it was wrapped as a type other than its runtime type",
        "source" => "the configured service name is not a valid CloudEvents source",
        "subject" => "the subject must be null or not blank",
        "tenantId" => "the tenant id must be null or not an empty identifier",
        "correlationId" => "the correlation id must be null or not blank",
        "causationId" => "the causation id must be null or not blank",
        _ => "it could not be wrapped in an event envelope",
    };
}
