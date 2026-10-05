using FluentAssertions;
using SharedKernel.Integration.Webhooks.Signing;

namespace SharedKernel.Integration.Webhooks.Tests.Signing;

public sealed class WebhookSignatureTests
{
    private const string Secret = "super-secret-key";
    private const string PayloadJson = """{"orderId":"12345","amount":99.95}""";

    [Fact]
    public void Sign_ThenVerify_RoundTripSucceeds()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, Secret, timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson,
            timestamp.ToUnixTimeSeconds().ToString(),
            signature,
            Secret);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedPayload_Fails()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, Secret, timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            """{"orderId":"99999","amount":1.00}""",
            timestamp.ToUnixTimeSeconds().ToString(),
            signature,
            Secret);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedTimestampHeader_Fails()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, Secret, timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson,
            timestamp.AddSeconds(1).ToUnixTimeSeconds().ToString(),
            signature,
            Secret);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedSignatureHeader_Fails()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, Secret, timestamp);
        var tampered = signature[..^2] + (signature[^2..] == "00" ? "11" : "00");

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson,
            timestamp.ToUnixTimeSeconds().ToString(),
            tampered,
            Secret);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongSecret_Fails()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, Secret, timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson,
            timestamp.ToUnixTimeSeconds().ToString(),
            signature,
            "a-different-secret");

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_TimestampOutsideDefaultTolerance_Fails()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var signature = provider.Sign(PayloadJson, Secret, timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson,
            timestamp.ToUnixTimeSeconds().ToString(),
            signature,
            Secret);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_TimestampOutsideCustomTolerance_Fails()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow.AddSeconds(-90);
        var signature = provider.Sign(PayloadJson, Secret, timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson,
            timestamp.ToUnixTimeSeconds().ToString(),
            signature,
            Secret,
            TimeSpan.FromSeconds(60));

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_TimestampWithinCustomTolerance_Succeeds()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-9);
        var signature = provider.Sign(PayloadJson, Secret, timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson,
            timestamp.ToUnixTimeSeconds().ToString(),
            signature,
            Secret,
            TimeSpan.FromMinutes(10));

        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "1700000000", "abc", "secret")]
    [InlineData("payload", null, "abc", "secret")]
    [InlineData("payload", "1700000000", null, "secret")]
    [InlineData("payload", "1700000000", "abc", null)]
    [InlineData("payload", "not-a-number", "abc", "secret")]
    [InlineData("payload", "1700000000", "not-valid-hex!!", "secret")]
    [InlineData("payload", "99999999999999999999999999", "abc", "secret")]
    [InlineData("", "1700000000", "abc", "secret")]
    [InlineData("payload", "", "abc", "secret")]
    [InlineData("payload", "1700000000", "", "secret")]
    [InlineData("payload", "1700000000", "abc", "")]
    public void Verify_MalformedInput_NeverThrowsAndReturnsFalse(
        string? payloadJson, string? timestampHeaderValue, string? signatureHeaderValue, string? secret)
    {
        var act = () => WebhookSignatureVerifier.Verify(payloadJson, timestampHeaderValue, signatureHeaderValue, secret);

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    [Fact]
    public void Verify_NegativeTolerance_ReturnsFalse()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, Secret, timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson,
            timestamp.ToUnixTimeSeconds().ToString(),
            signature,
            Secret,
            TimeSpan.FromSeconds(-1));

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Sign_ProducesLowercaseHexDigest()
    {
        var provider = new WebhookSignatureProvider();
        var signature = provider.Sign(PayloadJson, Secret, DateTimeOffset.UtcNow);

        signature.Should().MatchRegex("^[0-9a-f]+$");
    }
}
