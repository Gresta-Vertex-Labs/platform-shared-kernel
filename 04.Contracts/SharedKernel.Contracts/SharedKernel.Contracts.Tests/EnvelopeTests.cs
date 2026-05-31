using System.Text.Json;
using SharedKernel.Contracts.Serialization;
using SharedKernel.Primitives.Errors;
using EnvelopeNs = SharedKernel.Contracts.Envelope;

namespace SharedKernel.Contracts.Tests;

public sealed class EnvelopeTests
{
    private static readonly Error SampleError = Error.Validation("test.error", "Something failed");

    // ─── Envelope (void) ──────────────────────────────────────────────────────

    [Fact]
    public void Ok_SetsIsSuccessTrue_AndNullError()
    {
        var envelope = EnvelopeNs.Envelope.Ok();

        envelope.IsSuccess.Should().BeTrue();
        envelope.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_SetsIsSuccessFalse_AndError()
    {
        var envelope = EnvelopeNs.Envelope.Fail(SampleError);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error.Should().Be(SampleError);
    }

    [Fact]
    public void Fail_WithErrorNone_ThrowsArgumentException()
    {
        var act = () => EnvelopeNs.Envelope.Fail(Error.None);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("error");
    }

    [Fact]
    public void ImplicitOperator_FromError_ProducesFailedEnvelope()
    {
        EnvelopeNs.Envelope envelope = SampleError;

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error.Should().Be(SampleError);
    }

    [Fact]
    public void TwoFailEnvelopes_WithSameError_AreEqual()
    {
        var a = EnvelopeNs.Envelope.Fail(SampleError);
        var b = EnvelopeNs.Envelope.Fail(SampleError);

        a.Should().Be(b);
    }

    [Fact]
    public void OkEnvelope_NotEqualToFailEnvelope()
    {
        var ok = EnvelopeNs.Envelope.Ok();
        var fail = EnvelopeNs.Envelope.Fail(SampleError);

        ok.Should().NotBe(fail);
    }

    [Fact]
    public void Envelope_SerjDeserj_RoundTrips()
    {
        var ok = EnvelopeNs.Envelope.Ok();
        var fail = EnvelopeNs.Envelope.Fail(SampleError);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);

        var okJson = JsonSerializer.Serialize(ok, options);
        okJson.Should().Contain("\"isSuccess\":true");

        var failJson = JsonSerializer.Serialize(fail, options);
        failJson.Should().Contain("\"isSuccess\":false");
    }
}

public sealed class EnvelopeTTests
{
    private static readonly Error SampleError = Error.NotFound("not.found", "Resource not found");

    // ─── Envelope<T> ─────────────────────────────────────────────────────────

    [Fact]
    public void Ok_SetsIsSuccessTrue_ValueSet_NullError()
    {
        var envelope = EnvelopeNs.Envelope<string>.Ok("hello");

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value.Should().Be("hello");
        envelope.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_SetsIsSuccessFalse_ValueDefault_ErrorSet()
    {
        var envelope = EnvelopeNs.Envelope<string>.Fail(SampleError);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Value.Should().BeNull();
        envelope.Error.Should().Be(SampleError);
    }

    [Fact]
    public void Ok_WithNullValue_ThrowsArgumentNullException()
    {
        var act = () => EnvelopeNs.Envelope<string>.Ok(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("value");
    }

    [Fact]
    public void Fail_WithErrorNone_ThrowsArgumentException()
    {
        var act = () => EnvelopeNs.Envelope<string>.Fail(Error.None);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("error");
    }

    [Fact]
    public void ImplicitOperator_FromValue_ProducesSuccessfulEnvelope()
    {
        EnvelopeNs.Envelope<string> envelope = "world";

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value.Should().Be("world");
    }

    [Fact]
    public void ImplicitOperator_FromError_ProducesFailedEnvelope()
    {
        EnvelopeNs.Envelope<string> envelope = SampleError;

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error.Should().Be(SampleError);
        envelope.Value.Should().BeNull();
    }

    [Fact]
    public void TwoOkEnvelopes_WithSameValue_AreEqual()
    {
        var a = EnvelopeNs.Envelope<string>.Ok("test");
        var b = EnvelopeNs.Envelope<string>.Ok("test");

        a.Should().Be(b);
    }

    [Fact]
    public void TwoFailEnvelopes_WithSameError_AreEqual()
    {
        var a = EnvelopeNs.Envelope<string>.Fail(SampleError);
        var b = EnvelopeNs.Envelope<string>.Fail(SampleError);

        a.Should().Be(b);
    }

    [Fact]
    public void EnvelopeT_SerjDeserj_RoundTrips()
    {
        var ok = EnvelopeNs.Envelope<string>.Ok("round-trip");
        var fail = EnvelopeNs.Envelope<string>.Fail(SampleError);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);

        var okJson = JsonSerializer.Serialize(ok, options);
        okJson.Should().Contain("\"isSuccess\":true");

        var failJson = JsonSerializer.Serialize(fail, options);
        failJson.Should().Contain("\"isSuccess\":false");
    }
}
