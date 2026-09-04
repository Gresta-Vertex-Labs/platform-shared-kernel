using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// Covers <see cref="IEnvelopeEncryptionProvider"/>'s contract shape (P-446/WO-068) via
/// <see cref="InMemoryEnvelopeEncryptionProvider"/> — this package ships no production
/// implementation of its own (mirrors <see cref="IEncryptionKeyProvider"/>'s "consumer
/// implements" shape), so the round-trip is proven against a test double.
/// </summary>
public sealed class EnvelopeEncryptionProviderContractTests
{
    [Fact]
    public async Task GenerateDataKeyAsync_ThenUnwrapDataKeyAsync_RoundTripsToTheSamePlaintextKey()
    {
        var provider = new InMemoryEnvelopeEncryptionProvider();

        EnvelopeDataKey generated = await provider.GenerateDataKeyAsync();
        Result<byte[]> unwrapped = await provider.UnwrapDataKeyAsync(generated.WrappedKey, generated.MasterKeyId);

        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(generated.PlaintextKey, unwrapped.Value);
    }

    [Fact]
    public async Task GenerateDataKeyAsync_WrappedKeyDiffersFromPlaintextKey()
    {
        var provider = new InMemoryEnvelopeEncryptionProvider();

        EnvelopeDataKey generated = await provider.GenerateDataKeyAsync();

        Assert.NotEqual(generated.PlaintextKey, generated.WrappedKey);
    }

    [Fact]
    public async Task GenerateDataKeyAsync_ProducesDifferentPlaintextKeyEachCall()
    {
        var provider = new InMemoryEnvelopeEncryptionProvider();

        EnvelopeDataKey first = await provider.GenerateDataKeyAsync();
        EnvelopeDataKey second = await provider.GenerateDataKeyAsync();

        Assert.NotEqual(first.PlaintextKey, second.PlaintextKey);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_WithWrongMasterKeyId_Fails()
    {
        var provider = new InMemoryEnvelopeEncryptionProvider();
        EnvelopeDataKey generated = await provider.GenerateDataKeyAsync();

        Result<byte[]> result = await provider.UnwrapDataKeyAsync(generated.WrappedKey, "some-other-master-key");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_WithTamperedWrappedKey_Fails()
    {
        var provider = new InMemoryEnvelopeEncryptionProvider();
        EnvelopeDataKey generated = await provider.GenerateDataKeyAsync();

        byte[] tampered = [.. generated.WrappedKey];
        tampered[0] ^= 0xFF;

        Result<byte[]> result = await provider.UnwrapDataKeyAsync(tampered, generated.MasterKeyId);

        Assert.True(result.IsFailure);
    }
}
