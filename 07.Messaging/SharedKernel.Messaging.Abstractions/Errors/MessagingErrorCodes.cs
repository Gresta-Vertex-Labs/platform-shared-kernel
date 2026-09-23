namespace SharedKernel.Messaging.Abstractions.Errors;

/// <summary>
/// Stable <c>messaging.*</c> error codes returned by <see cref="MessageBus.IMessageBus"/> and
/// <see cref="EventPublisher.IEventPublisher"/> as <see cref="SharedKernel.Primitives.Errors.Error.Code"/>.
/// </summary>
/// <remarks>
/// <para>
/// These codes are a wire-visible contract: they reach callers, logs, dashboards and — through
/// <c>14.Presentation</c>'s <c>Error.ToProblemDetails()</c> — HTTP responses. Treat a rename as a
/// breaking change; add a new member instead.
/// </para>
/// <para>
/// Introduced by P-560, mirroring <c>08.Storage</c>'s <c>storage.*</c> codes. Before that every
/// dispatch failure — including a broker being unreachable — surfaced as a raw transport exception,
/// so callers could not distinguish "retry later" from "this message will never be accepted"
/// without catching provider-specific exception types the abstraction exists to hide.
/// </para>
/// </remarks>
public static class MessagingErrorCodes
{
    /// <summary>
    /// The broker could not be reached, timed out, or refused the operation for a transient reason.
    /// Retryable: the same call may succeed later with no change to the message. Carried by an
    /// <see cref="SharedKernel.Primitives.Errors.ErrorType.Unavailable"/> error (HTTP 503).
    /// </summary>
    public const string Unavailable = "messaging.unavailable";

    /// <summary>
    /// The destination endpoint for a <c>SendAsync</c> could not be resolved or does not exist.
    /// Not retryable without a routing or configuration change.
    /// </summary>
    public const string EndpointNotFound = "messaging.endpoint_not_found";

    /// <summary>
    /// The message could not be serialized for the wire, or the configured payload transform
    /// (compression/encryption) failed on the publish path.
    /// </summary>
    public const string SerializationFailed = "messaging.serialization_failed";

    /// <summary>
    /// The broker accepted the connection but rejected this specific message — for example an
    /// unroutable publish, a payload above the transport's size limit, or a policy rejection.
    /// </summary>
    public const string PublishRejected = "messaging.publish_rejected";

    /// <summary>
    /// The integration event failed validation before dispatch: <c>EventId</c> or <c>OccurredOn</c>
    /// unset, or the declared event type not matching the runtime type of the argument.
    /// </summary>
    public const string InvalidMessage = "messaging.invalid_message";

    /// <summary>
    /// The event type does not carry a valid <c>[IntegrationEvent]</c> attribute, so no CloudEvents
    /// <c>type</c> or <c>dataversion</c> can be resolved for it.
    /// </summary>
    public const string ContractViolation = "messaging.contract_violation";
}
