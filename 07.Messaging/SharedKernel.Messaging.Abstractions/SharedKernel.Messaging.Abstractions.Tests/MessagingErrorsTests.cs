using FluentAssertions;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Messaging.Abstractions.Tests;

/// <summary>
/// <see cref="MessagingErrors"/> factory tests: each member pairs its <c>messaging.*</c> code with
/// the documented <see cref="ErrorType"/>, which decides the HTTP status at the boundary.
/// </summary>
public sealed class MessagingErrorsTests
{
    [Fact]
    public void Unavailable_ReturnsUnavailable_NotUnexpected()
    {
        // P-562: a broker outage is retryable, so it must reach the HTTP boundary as 503, not 500.
        var error = MessagingErrors.Unavailable("publish");

        error.Type.Should().Be(ErrorType.Unavailable);
        error.Code.Should().Be(MessagingErrorCodes.Unavailable);
        error.Code.Should().Be("messaging.unavailable", "the code string is a wire contract and did not change");
        error.Message.Should().Contain("publish").And.Contain("retried");
    }

    [Fact]
    public void EveryFactory_KeepsItsDocumentedErrorType()
    {
        MessagingErrors.EndpointNotFound("Cmd", "queue:x").Type.Should().Be(ErrorType.NotFound);
        MessagingErrors.SerializationFailed("Msg").Type.Should().Be(ErrorType.Unexpected);
        MessagingErrors.PublishRejected("Msg").Type.Should().Be(ErrorType.Unexpected);
        MessagingErrors.InvalidMessage("Evt", "reason").Type.Should().Be(ErrorType.Validation);
        MessagingErrors.ContractViolation("Evt").Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void EveryFactory_UsesADistinctMessagingCode()
    {
        Error[] errors =
        [
            MessagingErrors.Unavailable("send"),
            MessagingErrors.EndpointNotFound("Cmd", "queue:x"),
            MessagingErrors.SerializationFailed("Msg"),
            MessagingErrors.PublishRejected("Msg"),
            MessagingErrors.InvalidMessage("Evt", "reason"),
            MessagingErrors.ContractViolation("Evt"),
        ];

        errors.Select(e => e.Code).Should().OnlyContain(c => c.StartsWith("messaging.", StringComparison.Ordinal));
        errors.Select(e => e.Code).Should().OnlyHaveUniqueItems();
    }
}
