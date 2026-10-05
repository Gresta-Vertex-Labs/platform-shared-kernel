using System.Text.Json;
using FluentAssertions;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Messaging.MassTransit.Internal;
using SharedKernel.Primitives.Errors;
using MtConnectionException = global::MassTransit.ConnectionException;
using MtRequestTimeoutException = global::MassTransit.RequestTimeoutException;
using MtTransportException = global::MassTransit.TransportException;
using MtTransportUnavailableException = global::MassTransit.TransportUnavailableException;

namespace SharedKernel.Messaging.MassTransit.Tests.MockTests;

/// <summary>
/// <see cref="MessagingExceptionClassifier"/>: the single point where a transport exception becomes
/// a <c>messaging.*</c> error, so the <see cref="ErrorType"/> asserted here is the one every
/// <c>IMessageBus</c>/<c>IEventPublisher</c> caller sees.
/// </summary>
public sealed class MessagingExceptionClassifierTests
{
    public static TheoryData<Exception> OutageExceptions() => new()
    {
        new TimeoutException("The broker did not answer in time."),
        new MtRequestTimeoutException("request-1"),
        new MtConnectionException("The broker refused the connection."),
        new MtTransportUnavailableException("The transport is unavailable.", new IOException("Connection refused.")),
    };

    [Theory]
    [MemberData(nameof(OutageExceptions))]
    public void BrokerOutage_ClassifiesAsUnavailable_NotUnexpected(Exception exception)
    {
        // P-562: an unreachable or silent broker is retryable, so it reaches the HTTP boundary as
        // 503 rather than 500 — with the messaging.unavailable code unchanged.
        Error? error = MessagingExceptionClassifier.TryClassify(exception, "OrderPlaced", "publish");

        error.Should().NotBeNull();
        error!.Type.Should().Be(ErrorType.Unavailable);
        error.Code.Should().Be(MessagingErrorCodes.Unavailable);
    }

    [Fact]
    public void TransportRejection_StaysUnexpected()
    {
        Error? error = MessagingExceptionClassifier.TryClassify(
            new MtTransportException(new Uri("rabbitmq://localhost/"), "The message was refused."),
            "OrderPlaced",
            "publish");

        error.Should().NotBeNull();
        error!.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be(MessagingErrorCodes.PublishRejected);
    }

    [Fact]
    public void SerializationFault_StaysUnexpected()
    {
        Error? error = MessagingExceptionClassifier.TryClassify(new JsonException("bad payload"), "OrderPlaced", "publish");

        error.Should().NotBeNull();
        error!.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be(MessagingErrorCodes.SerializationFailed);
    }

    [Fact]
    public void UnrecognisedException_IsNotClassified()
        => MessagingExceptionClassifier.TryClassify(new InvalidOperationException("a bug"), "OrderPlaced", "publish")
            .Should().BeNull("an unknown defect is rethrown, never laundered into a retryable-looking Result");
}
