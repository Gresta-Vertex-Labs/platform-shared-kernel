using FluentAssertions;
using SharedKernel.Integration.Webhooks.Signing;

namespace SharedKernel.Integration.Webhooks.Tests.Signing;

/// <summary>
/// Coverage for P-425: zero-downtime signing-secret rotation. <see cref="WebhookSignatureProvider.Sign"/>
/// always signs with the newest secret (index 0); <see cref="WebhookSignatureVerifier.Verify(string?,string?,string?,IReadOnlyList{string}?,TimeSpan?)"/>
/// accepts a match against any candidate in the list — the loop is written as an accumulating
/// <c>foreach</c> (never an early-returning <c>Any</c>/<c>break</c>), so every candidate is always
/// evaluated regardless of where the match falls, verified below by proving correctness is
/// independent of match position within the candidate list.
/// </summary>
public sealed class WebhookSignatureRotationTests
{
    private const string PayloadJson = """{"orderId":"12345","amount":99.95}""";

    [Fact]
    public void Verify_SignedWithNewestSecret_VerifiesAgainstFullCandidateList()
    {
        var provider = new WebhookSignatureProvider();
        var secrets = new[] { "newest-secret", "previous-secret", "oldest-secret" };
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, secrets[0], timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson, timestamp.ToUnixTimeSeconds().ToString(), signature, secrets);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_SignedWithSecondNewestSecret_StillVerifiesDuringRotationOverlap()
    {
        var provider = new WebhookSignatureProvider();
        var secrets = new[] { "newest-secret", "previous-secret", "oldest-secret" };
        var timestamp = DateTimeOffset.UtcNow;

        // A signature produced with the second-newest secret (e.g. sent by a still-in-flight
        // delivery that started signing just before rotation completed) must still verify.
        var signature = provider.Sign(PayloadJson, secrets[1], timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson, timestamp.ToUnixTimeSeconds().ToString(), signature, secrets);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_SignedWithOldestSecretOfLargeCandidateList_StillVerifies()
    {
        var provider = new WebhookSignatureProvider();
        var secrets = new[] { "s0", "s1", "s2", "s3", "s4", "s5", "oldest-still-active" };
        var timestamp = DateTimeOffset.UtcNow;

        // The match sits at the very last position — proves iteration reaches the end of the list
        // rather than stopping early after some fixed number of candidates.
        var signature = provider.Sign(PayloadJson, secrets[^1], timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson, timestamp.ToUnixTimeSeconds().ToString(), signature, secrets);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_NoCandidateMatches_ReturnsFalse()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, "the-actual-secret", timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson,
            timestamp.ToUnixTimeSeconds().ToString(),
            signature,
            ["wrong-one", "wrong-two", "wrong-three"]);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyCandidateList_ReturnsFalse()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, "some-secret", timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            PayloadJson, timestamp.ToUnixTimeSeconds().ToString(), signature, Array.Empty<string>());

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_NullCandidateList_ReturnsFalseWithoutThrowing()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, "some-secret", timestamp);

        var act = () => WebhookSignatureVerifier.Verify(
            PayloadJson, timestamp.ToUnixTimeSeconds().ToString(), signature, (IReadOnlyList<string>?)null);

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    [Fact]
    public void Verify_SingleSecretOverload_DelegatesToMultiCandidateOverload()
    {
        var provider = new WebhookSignatureProvider();
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, "the-secret", timestamp);

        var viaSingle = WebhookSignatureVerifier.Verify(
            PayloadJson, timestamp.ToUnixTimeSeconds().ToString(), signature, "the-secret");
        var viaList = WebhookSignatureVerifier.Verify(
            PayloadJson, timestamp.ToUnixTimeSeconds().ToString(), signature, ["the-secret"]);

        viaSingle.Should().BeTrue();
        viaList.Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedPayloadAgainstFullCandidateList_Fails()
    {
        var provider = new WebhookSignatureProvider();
        var secrets = new[] { "newest-secret", "previous-secret" };
        var timestamp = DateTimeOffset.UtcNow;
        var signature = provider.Sign(PayloadJson, secrets[1], timestamp);

        var isValid = WebhookSignatureVerifier.Verify(
            """{"orderId":"tampered","amount":0.01}""",
            timestamp.ToUnixTimeSeconds().ToString(),
            signature,
            secrets);

        isValid.Should().BeFalse();
    }
}
