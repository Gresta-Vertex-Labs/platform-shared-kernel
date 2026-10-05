using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Primitives.Tests.Errors;

/// <summary>
/// Pins every <see cref="ErrorType"/> member's numeric value. The values are a wire contract: the
/// enum is serialized as its integer by default, and <c>17.Workflows</c> persists it in Temporal
/// failure details, so renumbering a member reinterprets data that is already stored.
/// </summary>
public sealed class ErrorTypeTests
{
    [Theory]
    [InlineData(ErrorType.None, 0)]
    [InlineData(ErrorType.Unexpected, 1)]
    [InlineData(ErrorType.Validation, 2)]
    [InlineData(ErrorType.NotFound, 3)]
    [InlineData(ErrorType.Conflict, 4)]
    [InlineData(ErrorType.Unauthorized, 5)]
    [InlineData(ErrorType.BusinessRule, 6)]
    [InlineData(ErrorType.Forbidden, 7)]
    [InlineData(ErrorType.Unavailable, 8)]
    [InlineData(ErrorType.Timeout, 9)]
    public void Member_HasItsPinnedNumericValue(ErrorType type, int expected)
        => Assert.Equal(expected, (int)type);

    [Fact]
    public void EveryMember_IsPinned()
    {
        // A new member must be appended with the next free value and pinned above; this fails until
        // it is, so no member can ship with a value nobody decided on.
        int[] pinned = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        Assert.Equal(pinned, Enum.GetValues<ErrorType>().Select(type => (int)type).Order());
    }
}
