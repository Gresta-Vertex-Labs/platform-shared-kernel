using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeEnvelopeEncryptionProvider"/> against <c>IEnvelopeEncryptionProvider</c>'s contract: fresh
/// 32-byte data keys wrapped under an in-memory master key, and authenticated unwrapping that fails the way a real
/// key service does.
/// </summary>
public sealed class FakeEnvelopeEncryptionProviderTests
{
    [Fact]
    public async Task GenerateDataKeyAsync_ReturnsA32ByteKeyWrappedUnderTheMasterKey()
    {
        var provider = new FakeEnvelopeEncryptionProvider("master/v7");

        using EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();

        Assert.Equal("master/v7", dataKey.MasterKeyId);
        Assert.Equal("master/v7", provider.MasterKeyId);
        Assert.Equal(32, dataKey.PlaintextKey.Length);
        Assert.False(dataKey.WrappedKey.IsEmpty);
        Assert.False(dataKey.WrappedKey.SequenceEqual(dataKey.PlaintextKey));
    }

    [Fact]
    public async Task GenerateDataKeyAsync_ReturnsADifferentKeyEveryCall()
    {
        var provider = new FakeEnvelopeEncryptionProvider();

        using EnvelopeDataKey first = await provider.GenerateDataKeyAsync();
        using EnvelopeDataKey second = await provider.GenerateDataKeyAsync();

        Assert.False(first.PlaintextKey.SequenceEqual(second.PlaintextKey));
        Assert.Equal(2, provider.GenerateCallCount);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_RecoversThePlaintextKey()
    {
        var provider = new FakeEnvelopeEncryptionProvider();
        using EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();
        byte[] plaintext = dataKey.PlaintextKey.ToArray();
        byte[] wrapped = dataKey.WrappedKey.ToArray();

        var result = await provider.UnwrapDataKeyAsync(wrapped, dataKey.MasterKeyId);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
        Assert.Equal(1, provider.UnwrapCallCount);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_WrongMasterKeyId_Fails()
    {
        var provider = new FakeEnvelopeEncryptionProvider("master/v1");
        using EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();
        byte[] wrapped = dataKey.WrappedKey.ToArray();

        var result = await provider.UnwrapDataKeyAsync(wrapped, "master/v2");

        AssertUnwrapFailure(result);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_TamperedWrappedKey_Fails()
    {
        var provider = new FakeEnvelopeEncryptionProvider();
        using EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();
        byte[] wrapped = dataKey.WrappedKey.ToArray();
        wrapped[^1] ^= 0x01;

        var result = await provider.UnwrapDataKeyAsync(wrapped, dataKey.MasterKeyId);

        AssertUnwrapFailure(result);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_KeyWrappedByAnotherProviderInstance_Fails()
    {
        var provider = new FakeEnvelopeEncryptionProvider();
        var other = new FakeEnvelopeEncryptionProvider();
        using EnvelopeDataKey dataKey = await other.GenerateDataKeyAsync();
        byte[] wrapped = dataKey.WrappedKey.ToArray();

        var result = await provider.UnwrapDataKeyAsync(wrapped, dataKey.MasterKeyId);

        AssertUnwrapFailure(result);
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_TooShortWrappedKey_Fails()
    {
        var provider = new FakeEnvelopeEncryptionProvider();

        var result = await provider.UnwrapDataKeyAsync(new byte[8], provider.MasterKeyId);

        AssertUnwrapFailure(result);
    }

    [Fact]
    public async Task SimulateUnwrapFailure_FailsEveryUnwrap()
    {
        var provider = new FakeEnvelopeEncryptionProvider();
        using EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();
        byte[] wrapped = dataKey.WrappedKey.ToArray();
        provider.SimulateUnwrapFailure = true;

        var result = await provider.UnwrapDataKeyAsync(wrapped, dataKey.MasterKeyId);

        AssertUnwrapFailure(result);
        Assert.Equal(1, provider.UnwrapCallCount);
    }

    [Fact]
    public async Task EnvelopeEncryptionService_OverTheFake_RoundTripsThroughTheWireFormat()
    {
        var provider = new FakeEnvelopeEncryptionProvider();
        var service = new EnvelopeEncryptionService(provider);
        byte[] aad = "record-42"u8.ToArray();

        EnvelopePayload payload = await service.EncryptAsync("secret"u8.ToArray(), aad);
        Assert.True(EnvelopePayload.TryParse(payload.ToString(), out EnvelopePayload? parsed));
        var decrypted = await service.DecryptAsync(parsed!, aad);
        var wrongAad = await service.DecryptAsync(parsed!, "record-43"u8.ToArray());

        Assert.Equal(provider.MasterKeyId, payload.MasterKeyId);
        Assert.True(decrypted.IsSuccess);
        Assert.Equal("secret"u8.ToArray(), decrypted.Value);
        Assert.True(wrongAad.IsFailure);
    }

    [Fact]
    public async Task EnvelopeEncryptionService_WhenUnwrapFails_ReturnsAFailure()
    {
        var provider = new FakeEnvelopeEncryptionProvider();
        var service = new EnvelopeEncryptionService(provider);
        EnvelopePayload payload = await service.EncryptAsync("secret"u8.ToArray(), ReadOnlyMemory<byte>.Empty);
        provider.SimulateUnwrapFailure = true;

        var result = await service.DecryptAsync(payload, ReadOnlyMemory<byte>.Empty);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankMasterKeyId_Throws(string masterKeyId) =>
        Assert.Throws<ArgumentException>(() => new FakeEnvelopeEncryptionProvider(masterKeyId));

    private static void AssertUnwrapFailure(SharedKernel.Primitives.Results.Result<byte[]> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DataKeyUnwrapFailed, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }
}
