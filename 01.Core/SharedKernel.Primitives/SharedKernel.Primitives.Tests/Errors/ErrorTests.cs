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

    // T-19: Error.BusinessRule returns an Error with Type == ErrorType.BusinessRule
    [Fact]
    public void BusinessRule_SetsCorrectType()
    {
        var error = Error.BusinessRule("domain.rule.violated", "Order cannot be cancelled after shipping.");
        Assert.Equal(ErrorType.BusinessRule, error.Type);
        Assert.Equal("domain.rule.violated", error.Code);
        Assert.Equal("Order cannot be cancelled after shipping.", error.Message);
    }

    // T-20: Error.BusinessRule is distinct from Error.Unexpected and Error.Validation by ErrorType
    [Fact]
    public void BusinessRule_IsDistinctFromUnexpected_ByErrorType()
    {
        var businessRule = Error.BusinessRule("br.code", "rule violated");
        var unexpected = Error.Unexpected("br.code", "rule violated");

        Assert.NotEqual(businessRule.Type, unexpected.Type);
        Assert.Equal(ErrorType.BusinessRule, businessRule.Type);
        Assert.Equal(ErrorType.Unexpected, unexpected.Type);
    }

    [Fact]
    public void BusinessRule_IsDistinctFromValidation_ByErrorType()
    {
        var businessRule = Error.BusinessRule("br.code", "rule violated");
        var validation = Error.Validation("br.code", "rule violated");

        Assert.NotEqual(businessRule.Type, validation.Type);
        Assert.Equal(ErrorType.BusinessRule, businessRule.Type);
        Assert.Equal(ErrorType.Validation, validation.Type);
    }

    [Fact]
    public void BusinessRule_RecordEquality_SameCodeMessageType_AreEqual()
    {
        var a = Error.BusinessRule("domain.rule.violated", "msg");
        var b = Error.BusinessRule("domain.rule.violated", "msg");
        Assert.Equal(a, b);
    }
}
