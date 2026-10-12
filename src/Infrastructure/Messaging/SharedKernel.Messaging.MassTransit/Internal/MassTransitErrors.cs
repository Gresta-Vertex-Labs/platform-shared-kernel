using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Messaging.MassTransit.Internal;

/// <summary>
/// The canonical <see cref="Error"/> values of the consume side, paired with their
/// <see cref="MessagingErrorCodes"/> and <see cref="ErrorType"/>.
/// </summary>
/// <remarks>
/// Internal on purpose: every producer of these errors is a type in this package, so the factory has no
/// reason to be public; the codes stay public constants a caller can compare against. No message embeds
/// the text of an inner exception or of another <see cref="Error"/>.
/// </remarks>
internal static class MassTransitErrors
{
    /// <summary>Another delivery of the same message id holds the consumer's reservation.</summary>
    /// <param name="messageId">The contended message identifier.</param>
    /// <param name="messageType">The message CLR type being consumed.</param>
    /// <returns>A <see cref="ErrorType.Conflict"/> error carrying <see cref="MessagingErrorCodes.ConcurrentDelivery"/>.</returns>
    public static Error ConcurrentDelivery(Guid messageId, Type messageType) =>
        Error.Conflict(
            MessagingErrorCodes.ConcurrentDelivery,
            $"Message '{messageId}' of type '{messageType.Name}' is already being consumed by another delivery. " +
            "It is left unacknowledged so the broker redelivers it.");

    /// <summary>The payload transform of an incoming message could not be reversed.</summary>
    /// <returns>An <see cref="ErrorType.Unexpected"/> error carrying <see cref="MessagingErrorCodes.PayloadTransformMismatch"/>.</returns>
    public static Error PayloadTransformMismatch() =>
        Error.Unexpected(
            MessagingErrorCodes.PayloadTransformMismatch,
            "The payload transform of an incoming message could not be reversed. " +
            "Make the publisher's and this consumer's PayloadTransformOptions agree on EnableCompression and EnableEncryption.");
}
