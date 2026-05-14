using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Primitives.Tests.Errors;

public sealed class ErrorTests
{
    [Fact]
    public void None_HasEmptyCodeAndMessage_AndNoneType()
    {
        Assert.Equal(string.Empty, Error.None.Code);
        Assert.Equal(string.Empty, Error.None.Message);
        Assert.Equal(ErrorType.None, Error.None.Type);
    }

    [Fact]
    public void Unexpected_SetsCorrectType()
    {
        var error = Error.Unexpected("e.u", "unexpected failure");
        Assert.Equal(ErrorType.Unexpected, error.Type);
        Assert.Equal("e.u", error.Code);
        Assert.Equal("unexpected failure", error.Message);
    }

    [Fact]
    public void Validation_SetsCorrectType()
    {
        var error = Error.Validation("e.v", "invalid input");
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void NotFound_SetsCorrectType()
    {
        var error = Error.NotFound("e.nf", "resource not found");
        Assert.Equal(ErrorType.NotFound, error.Type);
    }

    [Fact]
    public void Conflict_SetsCorrectType()
    {
        var error = Error.Conflict("e.c", "duplicate entry");
        Assert.Equal(ErrorType.Conflict, error.Type);
    }

    [Fact]
    public void Unauthorized_SetsCorrectType()
    {
        var error = Error.Unauthorized("e.a", "access denied");
        Assert.Equal(ErrorType.Unauthorized, error.Type);
    }

    [Fact]
    public void RecordEquality_SameCodeMessageType_AreEqual()
    {
        var a = Error.Validation("e.v", "invalid");
        var b = Error.Validation("e.v", "invalid");
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentCode_AreNotEqual()
    {
        var a = Error.Validation("e.v1", "invalid");
        var b = Error.Validation("e.v2", "invalid");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void None_IsDistinctFromOtherErrors()
    {
        var other = Error.Unexpected(string.Empty, string.Empty);
        // None has type None; other has type Unexpected — they differ
        Assert.NotEqual(Error.None, other);
    }
}
