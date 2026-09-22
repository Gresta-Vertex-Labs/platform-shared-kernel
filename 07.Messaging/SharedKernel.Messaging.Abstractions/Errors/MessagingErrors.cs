using SharedKernel.Primitives.Errors;

namespace SharedKernel.Messaging.Abstractions.Errors;

/// <summary>
/// The single construction path for every <c>messaging.*</c> <see cref="Error"/>.
/// </summary>
/// <remarks>
/// Call these factories rather than building an <see cref="Error"/> inline, so a code and its
/// <see cref="ErrorType"/> stay paired everywhere they are produced. Mirrors
/// <c>08.Storage</c>'s <c>StorageErrors</c> (P-559) and is enforced by the same convention.
/// </remarks>
public static class MessagingErrors
{
    /// <summary>The broker was unreachable or the operation timed out; the caller may retry.</summary>
    /// <param name="operation">The dispatch verb that failed, e.g. <c>"publish"</c> or <c>"send"</c>.</param>
    /// <returns>An <see cref="ErrorType.Unexpected"/> error carrying <see cref="MessagingErrorCodes.Unavailable"/>.</returns>
    public static Error Unavailable(string operation) =>
        Error.Unexpected(
            MessagingErrorCodes.Unavailable,
            $"The message broker is unavailable; the {operation} can be retried later.");

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
    /// <param name="reason">What was wrong with it.</param>
    /// <returns>An <see cref="ErrorType.Validation"/> error carrying <see cref="MessagingErrorCodes.InvalidMessage"/>.</returns>
    public static Error InvalidMessage(string messageType, string reason) =>
        Error.Validation(
            MessagingErrorCodes.InvalidMessage,
            $"Integration event '{messageType}' is invalid: {reason}");

    /// <summary>The event type carries no valid <c>[IntegrationEvent]</c> attribute.</summary>
    /// <param name="messageType">The CLR type name of the event.</param>
    /// <returns>An <see cref="ErrorType.Validation"/> error carrying <see cref="MessagingErrorCodes.ContractViolation"/>.</returns>
    public static Error ContractViolation(string messageType) =>
        Error.Validation(
            MessagingErrorCodes.ContractViolation,
            $"Integration event '{messageType}' has no valid [IntegrationEvent] attribute, so its " +
            "CloudEvents type and data version cannot be resolved.");
}
