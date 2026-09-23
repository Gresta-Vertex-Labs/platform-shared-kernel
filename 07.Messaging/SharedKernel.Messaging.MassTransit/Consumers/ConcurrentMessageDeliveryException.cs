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
/// </remarks>
public sealed class ConcurrentMessageDeliveryException : Exception
{
    /// <summary>Creates the exception for a message id already being consumed elsewhere.</summary>
    /// <param name="messageId">The contended message identifier.</param>
    /// <param name="messageType">The message CLR type being consumed.</param>
    public ConcurrentMessageDeliveryException(Guid messageId, Type messageType)
        : base($"Message '{messageId}' of type '{messageType?.Name}' is already being consumed by another " +
               "delivery. It is left unacknowledged so the broker redelivers it.")
    {
        MessageId = messageId;
        MessageType = messageType;
    }

    /// <summary>Creates the exception with an explicit message.</summary>
    /// <param name="message">The exception message.</param>
    public ConcurrentMessageDeliveryException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an explicit message and inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The cause.</param>
    public ConcurrentMessageDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with no message.</summary>
    public ConcurrentMessageDeliveryException()
    {
    }

    /// <summary>Gets the contended message identifier, or <see langword="null"/> when unspecified.</summary>
    public Guid? MessageId { get; }

    /// <summary>Gets the message CLR type, or <see langword="null"/> when unspecified.</summary>
    public Type? MessageType { get; }
}
