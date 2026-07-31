using System.Text.Json;
using SharedKernel.Contracts.Envelopes;
using SharedKernel.Contracts.Serialization;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Contracts.Tests;

/// <summary>
/// Regression coverage for WO-052/P-328: <see cref="Envelope"/>/<see cref="Envelope{T}"/> now live in
/// namespace <c>SharedKernel.Contracts.Envelopes</c> (plural). A plain
/// <c>using SharedKernel.Contracts.Envelopes;</c> followed by an unqualified <see cref="Envelope"/>
/// reference — exactly as used throughout this file — must compile with no using-alias workaround.
/// This entire test class is itself the regression proof: it previously required
/// <c>using EnvelopeNs = SharedKernel.Contracts.Envelope;</c> to avoid the namespace/type-name collision.
/// </summary>
public sealed class EnvelopeTests
{
    private static readonly Error SampleError = Error.Validation("test.error", "Something failed");

    // ─── Envelope (void) ──────────────────────────────────────────────────────

    [Fact]
    public void Ok_SetsIsSuccessTrue_AndNullError()
    {
        var envelope = Envelope.Ok();

        envelope.IsSuccess.Should().BeTrue();
        envelope.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_SetsIsSuccessFalse_AndError()
    {
        var envelope = Envelope.Fail(SampleError);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error.Should().Be(SampleError);
    }

    [Fact]
    public void Fail_WithErrorNone_ThrowsArgumentException()
    {
        var act = () => Envelope.Fail(Error.None);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("error");
    }

    [Fact]
    public void ImplicitOperator_FromError_ProducesFailedEnvelope()
    {
        Envelope envelope = SampleError;

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error.Should().Be(SampleError);
    }

    [Fact]
    public void TwoFailEnvelopes_WithSameError_AreEqual()
    {
        var a = Envelope.Fail(SampleError);
        var b = Envelope.Fail(SampleError);

        a.Should().Be(b);
    }

    [Fact]
    public void OkEnvelope_NotEqualToFailEnvelope()
    {
        var ok = Envelope.Ok();
        var fail = Envelope.Fail(SampleError);

        ok.Should().NotBe(fail);
    }

    [Fact]
    public void Envelope_SerjDeserj_RoundTrips()
    {
        var ok = Envelope.Ok();
        var fail = Envelope.Fail(SampleError);

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
        var envelope = Envelope<string>.Ok("hello");

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value.Should().Be("hello");
        envelope.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_SetsIsSuccessFalse_ValueDefault_ErrorSet()
    {
        var envelope = Envelope<string>.Fail(SampleError);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Value.Should().BeNull();
        envelope.Error.Should().Be(SampleError);
    }

    [Fact]
    public void Ok_WithNullValue_ThrowsArgumentNullException()
    {
        var act = () => Envelope<string>.Ok(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("value");
    }

    [Fact]
    public void Fail_WithErrorNone_ThrowsArgumentException()
    {
        var act = () => Envelope<string>.Fail(Error.None);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("error");
    }

    [Fact]
    public void ImplicitOperator_FromValue_ProducesSuccessfulEnvelope()
    {
        Envelope<string> envelope = "world";

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value.Should().Be("world");
    }

    [Fact]
    public void ImplicitOperator_FromError_ProducesFailedEnvelope()
    {
        Envelope<string> envelope = SampleError;

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error.Should().Be(SampleError);
        envelope.Value.Should().BeNull();
    }

    [Fact]
    public void TwoOkEnvelopes_WithSameValue_AreEqual()
    {
        var a = Envelope<string>.Ok("test");
        var b = Envelope<string>.Ok("test");

        a.Should().Be(b);
    }

    [Fact]
    public void TwoFailEnvelopes_WithSameError_AreEqual()
    {
        var a = Envelope<string>.Fail(SampleError);
        var b = Envelope<string>.Fail(SampleError);

        a.Should().Be(b);
    }

    [Fact]
    public void EnvelopeT_SerjDeserj_RoundTrips()
    {
        var ok = Envelope<string>.Ok("round-trip");
        var fail = Envelope<string>.Fail(SampleError);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);

        var okJson = JsonSerializer.Serialize(ok, options);
        okJson.Should().Contain("\"isSuccess\":true");

        var failJson = JsonSerializer.Serialize(fail, options);
        failJson.Should().Contain("\"isSuccess\":false");
    }
}
