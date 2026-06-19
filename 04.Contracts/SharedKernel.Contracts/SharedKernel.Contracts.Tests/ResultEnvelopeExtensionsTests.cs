using SharedKernel.Contracts.Mapping;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using EnvelopeNs = SharedKernel.Contracts.Envelope;

namespace SharedKernel.Contracts.Tests;

/// <summary>
/// Unit tests for <see cref="ResultEnvelopeExtensions"/> — all four pure mapping methods.
/// </summary>
public sealed class ResultEnvelopeExtensionsTests
{
    private static readonly Error SampleError = Error.NotFound("mapping.test", "Resource not found during mapping test");

    // ─── Generic variants: Result<T> → Envelope<T> ───────────────────────────

    [Fact]
    public void ToEnvelopeT_WhenResultIsSuccess_ReturnsSuccessEnvelopeWithValue()
    {
        var result = Result<string>.Success("hello");

        EnvelopeNs.Envelope<string> envelope = result.ToEnvelope();

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value.Should().Be("hello");
        envelope.Error.Should().BeNull();
    }

    [Fact]
    public void ToEnvelopeT_WhenResultIsFailure_ReturnsFailureEnvelopeWithError()
    {
        var result = Result<string>.Failure(SampleError);

        EnvelopeNs.Envelope<string> envelope = result.ToEnvelope();

        envelope.IsSuccess.Should().BeFalse();
        envelope.Value.Should().BeNull();
        envelope.Error.Should().Be(SampleError);
    }

    // ─── Generic variants: Envelope<T> → Result<T> ───────────────────────────

    [Fact]
    public void ToResultT_WhenEnvelopeIsSuccess_ReturnsSuccessResultWithValue()
    {
        var envelope = EnvelopeNs.Envelope<string>.Ok("world");

        Result<string> result = envelope.ToResult();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("world");
    }

    [Fact]
    public void ToResultT_WhenEnvelopeIsFailure_ReturnsFailureResultWithError()
    {
        var envelope = EnvelopeNs.Envelope<string>.Fail(SampleError);

        Result<string> result = envelope.ToResult();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(SampleError);
    }

    // ─── Double round-trip: Result<T> → Envelope<T> → Result<T> ──────────────

    [Fact]
    public void DoubleRoundTrip_SuccessResult_PreservesValueIdentity()
    {
        var original = Result<string>.Success("round-trip-value");

        Result<string> roundTripped = original.ToEnvelope().ToResult();

        roundTripped.IsSuccess.Should().BeTrue();
        roundTripped.Value.Should().Be("round-trip-value");
    }

    [Fact]
    public void DoubleRoundTrip_FailureResult_PreservesErrorIdentity()
    {
        var original = Result<string>.Failure(SampleError);

        Result<string> roundTripped = original.ToEnvelope().ToResult();

        roundTripped.IsSuccess.Should().BeFalse();
        roundTripped.Error.Should().Be(SampleError);
    }

    // ─── Non-generic variants: Result → Envelope ─────────────────────────────

    [Fact]
    public void ToEnvelope_WhenResultIsSuccess_ReturnsSuccessEnvelope()
    {
        var result = Result.Success();

        EnvelopeNs.Envelope envelope = result.ToEnvelope();

        envelope.IsSuccess.Should().BeTrue();
        envelope.Error.Should().BeNull();
    }

    [Fact]
    public void ToEnvelope_WhenResultIsFailure_ReturnsFailureEnvelopeWithError()
    {
        var result = Result.Failure(SampleError);

        EnvelopeNs.Envelope envelope = result.ToEnvelope();

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error.Should().Be(SampleError);
    }

    // ─── Non-generic variants: Envelope → Result ─────────────────────────────

    [Fact]
    public void ToResult_WhenEnvelopeIsSuccess_ReturnsSuccessResult()
    {
        var envelope = EnvelopeNs.Envelope.Ok();

        Result result = envelope.ToResult();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ToResult_WhenEnvelopeIsFailure_ReturnsFailureResultWithError()
    {
        var envelope = EnvelopeNs.Envelope.Fail(SampleError);

        Result result = envelope.ToResult();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(SampleError);
    }
}
