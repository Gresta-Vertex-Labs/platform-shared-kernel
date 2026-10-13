using FluentAssertions;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Messaging.MassTransit.Internal;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Messaging.MassTransit.Tests.ObservabilityTests;

/// <summary>
/// <see cref="MassTransitErrors"/>: each factory pairs its <c>messaging.*</c> code with the documented
/// <see cref="ErrorType"/>, every message is a sentence ending in a period, and no inner text reaches an error.
/// </summary>
public sealed class MassTransitErrorsTests
{
    public static TheoryData<string, Error, string, ErrorType> Factories => new()
    {
        { nameof(MassTransitErrors.Unavailable), MassTransitErrors.Unavailable("publish"), MessagingErrorCodes.Unavailable, ErrorType.Unavailable },
        { nameof(MassTransitErrors.EndpointNotFound), MassTransitErrors.EndpointNotFound("Cmd", "queue:x"), MessagingErrorCodes.EndpointNotFound, ErrorType.NotFound },
        { nameof(MassTransitErrors.SerializationFailed), MassTransitErrors.SerializationFailed("Msg"), MessagingErrorCodes.SerializationFailed, ErrorType.Unexpected },
        { nameof(MassTransitErrors.PublishRejected), MassTransitErrors.PublishRejected("Msg"), MessagingErrorCodes.PublishRejected, ErrorType.Unexpected },
        { nameof(MassTransitErrors.InvalidMessage), MassTransitErrors.InvalidMessage("Evt", new ArgumentException("x", "integrationEvent")), MessagingErrorCodes.InvalidMessage, ErrorType.Validation },
        { nameof(MassTransitErrors.ContractViolation), MassTransitErrors.ContractViolation("Evt"), MessagingErrorCodes.ContractViolation, ErrorType.Validation },
        { nameof(MassTransitErrors.ConcurrentDelivery), MassTransitErrors.ConcurrentDelivery(Guid.NewGuid(), typeof(string)), MessagingErrorCodes.ConcurrentDelivery, ErrorType.Conflict },
        { nameof(MassTransitErrors.PayloadTransformMismatch), MassTransitErrors.PayloadTransformMismatch(), MessagingErrorCodes.PayloadTransformMismatch, ErrorType.Unexpected },
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void EveryFactory_PairsItsCodeWithTheDocumentedType_AndWritesASentence(string factory, Error error, string code, ErrorType type)
    {
        error.Code.Should().Be(code, factory);
        error.Type.Should().Be(type, factory);
        error.Message.Should().EndWith(".", factory);
        error.Message.Should().NotContain("rule ", factory, "no internal rule number is quoted");
    }

    [Fact]
    public void EveryFactory_UsesADistinctMessagingCode_AndEveryPublicCodeHasAFactory()
    {
        var codes = Factories.Select(row => (string)row[2]).ToArray();

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => c.StartsWith("messaging.", StringComparison.Ordinal));
        codes.Should().BeEquivalentTo(
            typeof(MessagingErrorCodes).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!),
            "every public code is produced by exactly one factory");
    }

    [Fact]
    public void Unavailable_IsRetryable503Material_NotATimeout()
    {
        MassTransitErrors.Unavailable("send").Type.Should().Be(ErrorType.Unavailable);
    }

    [Theory]
    [InlineData("integrationEvent")]
    [InlineData("source")]
    [InlineData("subject")]
    [InlineData("tenantId")]
    [InlineData("correlationId")]
    [InlineData("causationId")]
    [InlineData("somethingElse")]
    [InlineData(null)]
    public void InvalidMessage_ReasonIsClosed_TheArgumentExceptionTextNeverReachesTheError(string? parameterName)
    {
        const string sentinel = "argument-text-sentinel-3b9d";

        var error = MassTransitErrors.InvalidMessage("Evt", new ArgumentException(sentinel, parameterName));

        error.Message.Should().NotContain(sentinel);
        error.Message.Should().StartWith("Integration event 'Evt' is invalid because ").And.EndWith(".");
        error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void ConcurrentDelivery_AndPayloadMismatch_CarryNoInnerText()
    {
        MassTransitErrors.PayloadTransformMismatch().Message.Should().NotContain("Exception");
        MassTransitErrors.ConcurrentDelivery(Guid.Empty, typeof(string)).Message.Should().Contain("'String'");
    }
}
