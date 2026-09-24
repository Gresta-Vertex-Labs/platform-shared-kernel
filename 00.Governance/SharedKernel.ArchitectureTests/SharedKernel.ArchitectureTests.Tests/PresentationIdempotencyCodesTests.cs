using FluentAssertions;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Presentation.WebApi.Errors;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// P-562 X3: a missing idempotency key has one code, whether 14.Presentation refuses the request at the HTTP boundary
/// or 05.Application's <c>IdempotencyBehavior</c> refuses the command.
/// </summary>
/// <remarks>
/// Neither package may reference the other, so each declares the code. This test pins them together: renaming either
/// fails here instead of giving clients two codes for one failure.
/// </remarks>
public sealed class PresentationIdempotencyCodesTests
{
    [Fact]
    public void MissingKeyCode_IsTheSameAtTheHttpBoundaryAndInThePipeline() =>
        PresentationErrorCodes.IdempotencyKeyRequired.Should().Be(
            IdempotencyErrorCodes.KeyRequired,
            because: "a client must see one code for a missing idempotency key, wherever it is detected");
}
