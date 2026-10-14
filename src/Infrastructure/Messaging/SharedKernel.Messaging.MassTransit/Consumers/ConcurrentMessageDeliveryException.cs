using SharedKernel.Core.Exceptions;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Messaging.MassTransit.Internal;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// Thrown by <c>IdempotentConsumerBehavior&lt;TMessage&gt;</c> when another delivery of the same
/// message id is already running the consumer.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberate, benign signal rather than a defect: it leaves the message unacknowledged
/// so the broker redelivers it. That matters because the in-flight attempt might still fail — if
/// this delivery were acknowledged instead, a failure over there would lose the message entirely.
/// </para>
/// <para>
/// Expect these under normal operation whenever a consumer runs long enough for the broker's
/// visibility timeout to elapse, or when competing consumers race on the same id. A sustained rate
/// usually means consumers are slower than the redelivery interval, not that deduplication is
/// broken.
/// </para>
/// <para>
/// Do not add this type to a <c>ConsumerDefinitionBase.NonRetryableExceptions</c> list — retry is
/// precisely the desired behaviour.
/// </para>
/// <para>
/// A <see cref="SharedKernelException"/>: <see cref="SharedKernelException.Error"/> carries
/// <see cref="MessagingErrorCodes.ConcurrentDelivery"/> with <see cref="ErrorType.Conflict"/>
/// (HTTP 409, gRPC <c>Aborted</c> through the edge maps).
/// </para>
/// </remarks>
public sealed class ConcurrentMessageDeliveryException : SharedKernelException
{
    /// <summary>Creates the exception for a message id already being consumed elsewhere.</summary>
    /// <param name="messageId">The contended message identifier.</param>
    /// <param name="messageType">The message CLR type being consumed.</param>
    public ConcurrentMessageDeliveryException(Guid messageId, Type messageType)
        : base(MassTransitErrors.ConcurrentDelivery(messageId, messageType))
    {
        MessageId = messageId;
        MessageType = messageType;
    }

    /// <summary>Creates the exception from an explicit <see cref="Primitives.Errors.Error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    public ConcurrentMessageDeliveryException(Error error)
        : base(error)
    {
    }

    /// <summary>Creates the exception from an explicit <see cref="Primitives.Errors.Error"/> and its cause.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The cause.</param>
    public ConcurrentMessageDeliveryException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }

    /// <summary>Gets the contended message identifier, or <see langword="null"/> when unspecified.</summary>
    public Guid? MessageId { get; }

    /// <summary>Gets the message CLR type, or <see langword="null"/> when unspecified.</summary>
    public Type? MessageType { get; }
}
