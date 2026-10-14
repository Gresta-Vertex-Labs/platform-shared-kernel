using FluentAssertions;
using MassTransit;
using SharedKernel.Compression;
using SharedKernel.Compression.Options;
using SharedKernel.Core.Exceptions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Internal;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Cryptography;
using MtSystemTextJsonMessageSerializerFactory = MassTransit.Configuration.SystemTextJsonMessageSerializerFactory;
using MtEmptyHeaders = MassTransit.Serialization.DictionarySendHeaders;

namespace SharedKernel.Messaging.MassTransit.Tests.ErrorSafetyTests;

/// <summary>
/// SK.07.MessagingErrorSafety T-04: the two consume-side exceptions are sealed <see cref="SharedKernelException"/>s with
/// pinned codes and error types. The HTTP and gRPC status assertions belong to 14.Presentation; the inputs its edge maps
/// switch on (<c>is SharedKernelException</c>, <c>Error.Type</c>) are pinned here.
/// </summary>
public sealed class ClassifiedExceptionTests
{
    [Fact]
    public void ConcurrentMessageDeliveryException_IsAConflictSharedKernelException()
    {
        var messageId = Guid.NewGuid();

        var exception = new ConcurrentMessageDeliveryException(messageId, typeof(ExceptionTestMessage));

        exception.Should().BeAssignableTo<SharedKernelException>().And.BeAssignableTo<IHasError>();
        typeof(ConcurrentMessageDeliveryException).IsSealed.Should().BeTrue();
        exception.Error.Code.Should().Be(MessagingErrorCodes.ConcurrentDelivery);
        exception.Error.Code.Should().Be("messaging.concurrent_delivery");
        exception.Error.Type.Should().Be(ErrorType.Conflict);
        exception.Message.Should().Be(exception.Error.Message);
        exception.Message.Should().EndWith(".").And.Contain(messageId.ToString("D")).And.Contain(nameof(ExceptionTestMessage));
        exception.MessageId.Should().Be(messageId);
        exception.MessageType.Should().Be(typeof(ExceptionTestMessage));
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void PayloadTransformMismatchException_IsAnUnexpectedSharedKernelException()
    {
        var inner = new FormatException("sentinel-inner-text-6c21");

        var exception = new PayloadTransformMismatchException(MassTransitErrors.PayloadTransformMismatch(), inner);

        exception.Should().BeAssignableTo<SharedKernelException>().And.BeAssignableTo<IHasError>();
        typeof(PayloadTransformMismatchException).IsSealed.Should().BeTrue();
        exception.Error.Code.Should().Be(MessagingErrorCodes.PayloadTransformMismatch);
        exception.Error.Code.Should().Be("messaging.payload_transform_mismatch");
        exception.Error.Type.Should().Be(ErrorType.Unexpected);
        exception.Message.Should().Be(exception.Error.Message);
        exception.Message.Should().EndWith(".").And.NotContain("sentinel-inner-text-6c21");
        exception.Error.Message.Should().NotContain("sentinel-inner-text-6c21");
    }

    [Fact]
    public void ErrorAndInnerConstructors_KeepTheSameInnerInstanceAndItsStack()
    {
        Exception inner;
        try
        {
            throw new InvalidOperationException("cause");
        }
        catch (InvalidOperationException ex)
        {
            inner = ex;
        }

        var stackBefore = inner.StackTrace;
        var error = MassTransitErrors.ConcurrentDelivery(Guid.NewGuid(), typeof(ExceptionTestMessage));

        new ConcurrentMessageDeliveryException(error, inner).InnerException.Should().BeSameAs(inner);
        new PayloadTransformMismatchException(MassTransitErrors.PayloadTransformMismatch(), inner)
            .InnerException.Should().BeSameAs(inner);
        inner.StackTrace.Should().Be(stackBefore);
        new ConcurrentMessageDeliveryException(error).Error.Should().BeSameAs(error);
        new PayloadTransformMismatchException(error).Error.Should().BeSameAs(error);
    }

    [Fact]
    public void EdgeMapperInputs_AreTheOnesTheThreeEdgeSwitchesTest()
    {
        // 14.Presentation maps `SharedKernelException { Error.Type }`: 409 / Aborted for Conflict, 500 / Internal for Unexpected.
        Exception[] thrown =
        [
            new ConcurrentMessageDeliveryException(Guid.NewGuid(), typeof(ExceptionTestMessage)),
            new PayloadTransformMismatchException(MassTransitErrors.PayloadTransformMismatch()),
        ];

        thrown.Select(e => e is SharedKernelException { Error.Type: ErrorType.Conflict or ErrorType.Unexpected })
            .Should().AllBeEquivalentTo(true);
        ((SharedKernelException)thrown[0]).Error.Type.Should().Be(ErrorType.Conflict);
        ((SharedKernelException)thrown[1]).Error.Type.Should().Be(ErrorType.Unexpected);
    }

    [Fact]
    public void Deserializer_ReverseFailure_ThrowsTheClassifiedExceptionKeepingTheOriginalAsInner()
    {
        var encryption = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider());
        var deserializer = new PayloadTransformMessageDeserializer(
            new MtSystemTextJsonMessageSerializerFactory().CreateDeserializer(),
            new PayloadTransformOptions { EnableEncryption = true },
            compressor: null,
            encryptionService: encryption);
        var headers = new MtEmptyHeaders();
        headers.Set(PayloadTransformHeaders.MessageTypeAad, "Some.Message.Type");

        var act = () => deserializer.Deserialize(
            new BytesMessageBody("{\"plain\":true}"u8.ToArray()), headers, new Uri("loopback://localhost/test"));

        var thrown = act.Should().Throw<PayloadTransformMismatchException>().Which;
        thrown.Error.Code.Should().Be(MessagingErrorCodes.PayloadTransformMismatch);
        thrown.InnerException.Should().BeOfType<FormatException>();
        thrown.Message.Should().NotContain(thrown.InnerException!.Message);
    }

    [Fact]
    public void Deserializer_DeserializeFailureAfterReversal_ThrowsTheClassifiedExceptionKeepingTheOriginalAsInner()
    {
        var compressor = new BrotliPayloadCompressor(Microsoft.Extensions.Options.Options.Create(new CompressionOptions()));
        var deserializer = new PayloadTransformMessageDeserializer(
            new MtSystemTextJsonMessageSerializerFactory().CreateDeserializer(),
            new PayloadTransformOptions { EnableCompression = true },
            compressor,
            encryptionService: null);
        var compressedGarbage = compressor.Compress("not an envelope"u8.ToArray());

        var act = () => deserializer.Deserialize(
            new BytesMessageBody(compressedGarbage), new MtEmptyHeaders(), new Uri("loopback://localhost/test"));

        var thrown = act.Should().Throw<PayloadTransformMismatchException>().Which;
        thrown.Error.Type.Should().Be(ErrorType.Unexpected);
        thrown.InnerException.Should().NotBeNull();
        thrown.Message.Should().NotContain(thrown.InnerException!.Message);
    }
}

public sealed record ExceptionTestMessage(string Text);
