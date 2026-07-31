using SharedKernel.Contracts.Envelopes;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Contracts;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Contracts;

public sealed class EnvelopeAssertionsTests
{
    [Fact]
    public void ShouldBeSuccess_NonGeneric_SuccessEnvelope_DoesNotThrow() =>
        Envelope.Ok().ShouldBeSuccess();

    [Fact]
    public void ShouldBeSuccess_NonGeneric_FailureEnvelope_Throws()
    {
        var envelope = Envelope.Fail(Error.Unexpected("x", "y"));
        Assert.Throws<InvalidOperationException>(envelope.ShouldBeSuccess);
    }

    [Fact]
    public void ShouldBeFailure_NonGeneric_FailureEnvelope_DoesNotThrow() =>
        Envelope.Fail(Error.Unexpected("x", "y")).ShouldBeFailure();

    [Fact]
    public void ShouldBeFailure_NonGeneric_SuccessEnvelope_Throws() =>
        Assert.Throws<InvalidOperationException>(Envelope.Ok().ShouldBeFailure);

    [Fact]
    public void ShouldBeSuccess_Generic_ReturnsValue()
    {
        var envelope = Envelope<int>.Ok(42);
        var value = envelope.ShouldBeSuccess();

        Assert.Equal(42, value);
    }

    [Fact]
    public void ShouldBeSuccess_Generic_FailureEnvelope_Throws()
    {
        var envelope = Envelope<int>.Fail(Error.Unexpected("x", "y"));
        Assert.Throws<InvalidOperationException>(() => envelope.ShouldBeSuccess());
    }

    [Fact]
    public void ShouldBeFailure_Generic_WithExpectedType_Matches_DoesNotThrow()
    {
        var envelope = Envelope<int>.Fail(Error.NotFound("x", "y"));
        envelope.ShouldBeFailure(ErrorType.NotFound);
    }

    [Fact]
    public void ShouldBeFailure_Generic_WithExpectedType_Mismatch_Throws()
    {
        var envelope = Envelope<int>.Fail(Error.NotFound("x", "y"));
        Assert.Throws<InvalidOperationException>(() => envelope.ShouldBeFailure(ErrorType.Conflict));
    }

    [Fact]
    public void ShouldBeFailure_Generic_SuccessEnvelope_Throws()
    {
        var envelope = Envelope<int>.Ok(1);
        Assert.Throws<InvalidOperationException>(() => envelope.ShouldBeFailure());
    }

    [Fact]
    public void ShouldHaveError_MatchingCode_DoesNotThrow()
    {
        var envelope = Envelope<int>.Fail(Error.Validation("code.x", "y"));
        envelope.ShouldHaveError("code.x");
    }

    [Fact]
    public void ShouldHaveError_MismatchedCode_Throws()
    {
        var envelope = Envelope<int>.Fail(Error.Validation("code.x", "y"));
        Assert.Throws<InvalidOperationException>(() => envelope.ShouldHaveError("code.other"));
    }

    [Fact]
    public void ShouldHaveError_SuccessEnvelope_Throws()
    {
        var envelope = Envelope<int>.Ok(1);
        Assert.Throws<InvalidOperationException>(() => envelope.ShouldHaveError("code.x"));
    }
}
