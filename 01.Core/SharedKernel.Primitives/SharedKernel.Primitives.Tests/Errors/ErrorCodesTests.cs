using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Primitives.Tests.Errors;

public sealed class ErrorCodesTests
{
    [Fact]
    public void Validation_Required_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.Validation.Required));

    [Fact]
    public void Validation_OutOfRange_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.Validation.OutOfRange));

    [Fact]
    public void NotFound_Default_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.NotFound.Default));

    [Fact]
    public void Conflict_Default_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.Conflict.Default));

    [Fact]
    public void Unauthorized_Default_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.Unauthorized.Default));

    [Fact]
    public void AllCodes_AreUniqueAcrossCategories()
    {
        var codes = new[]
        {
            ErrorCodes.Validation.Required,
            ErrorCodes.Validation.OutOfRange,
            ErrorCodes.Validation.InvalidFormat,
            ErrorCodes.Validation.MaxLength,
            ErrorCodes.Validation.MinLength,
            ErrorCodes.NotFound.Default,
            ErrorCodes.Conflict.Default,
            ErrorCodes.Conflict.Duplicate,
            ErrorCodes.Unauthorized.Default,
            ErrorCodes.Unauthorized.Expired,
            ErrorCodes.Unexpected.Default,
        };

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }
}
