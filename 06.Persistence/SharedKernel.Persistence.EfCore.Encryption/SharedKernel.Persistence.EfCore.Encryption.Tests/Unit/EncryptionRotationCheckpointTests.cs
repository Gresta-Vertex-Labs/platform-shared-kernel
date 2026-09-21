using FluentAssertions;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

public sealed class EncryptionRotationCheckpointTests
{
    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        // CompletedTargetKeys round-trips through JSON as a freshly-materialized list, so it is compared by
        // content (BeEquivalentTo), not by the record's own auto-generated Equals, which would compare the
        // IReadOnlyList<string> reference and always fail across a real encode/decode.
        var checkpoint = new EncryptionRotationCheckpoint(
            ["Customer::Email", "Customer::Ssn"], "Customer::Note", Convert.ToBase64String(Guid.NewGuid().ToByteArray()));

        var token = checkpoint.Encode();
        var decoded = EncryptionRotationCheckpoint.Decode(token);

        decoded.CompletedTargetKeys.Should().BeEquivalentTo(checkpoint.CompletedTargetKeys, o => o.WithStrictOrdering());
        decoded.InProgressTargetKey.Should().Be(checkpoint.InProgressTargetKey);
        decoded.LastPrimaryKeyText.Should().Be(checkpoint.LastPrimaryKeyText);
    }

    [Fact]
    public void EncodeThenDecode_NoInProgressTarget_RoundTrips()
    {
        // Produced when cancellation lands exactly between two targets — every prior target is fully completed
        // and none is yet in progress.
        var checkpoint = new EncryptionRotationCheckpoint(["Customer::Email"], null, "");

        var decoded = EncryptionRotationCheckpoint.Decode(checkpoint.Encode());

        decoded.CompletedTargetKeys.Should().BeEquivalentTo(checkpoint.CompletedTargetKeys);
        decoded.InProgressTargetKey.Should().BeNull();
        decoded.LastPrimaryKeyText.Should().BeEmpty();
    }

    [Fact]
    public void EncodeThenDecode_EmptyCompletedSet_RoundTrips()
    {
        // The very first checkpoint of a fresh run: nothing completed yet, the first target is already mid-page.
        var checkpoint = new EncryptionRotationCheckpoint([], "Customer::Email", "");

        var decoded = EncryptionRotationCheckpoint.Decode(checkpoint.Encode());

        decoded.CompletedTargetKeys.Should().BeEmpty();
        decoded.InProgressTargetKey.Should().Be("Customer::Email");
    }

    [Theory]
    [InlineData("not-base64!!!")]
    [InlineData("")]
    public void Decode_InvalidToken_ThrowsArgumentException(string token)
    {
        var act = () => EncryptionRotationCheckpoint.Decode(token);
        act.Should().Throw<ArgumentException>();
    }
}
