using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Tests.TestDoubles;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Tests.Envelope;

public sealed class EnvelopeEncryptionServiceTests
{
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("exports/2026-09/report.csv");

    private readonly FakeEnvelopeEncryptionProvider _provider = new();
    private readonly EnvelopeEncryptionService _service;

    public EnvelopeEncryptionServiceTests()
    {
        _service = new EnvelopeEncryptionService(_provider);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100_000)]
    public async Task EncryptAsync_DecryptAsync_RoundTrips(int length)
    {
        byte[] plaintext = RandomNumberGenerator.GetBytes(length);

        EnvelopePayload payload = await _service.EncryptAsync(plaintext, AssociatedData);
        Result<byte[]> decrypted = await _service.DecryptAsync(payload, AssociatedData);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal(plaintext, decrypted.Value);
        Assert.Equal(FakeEnvelopeEncryptionProvider.MasterKeyId, payload.MasterKeyId);
        Assert.Equal(length, payload.Ciphertext.Length);
    }

    [Fact]
    public async Task EncryptAsync_PayloadSurvivesStringRoundTrip()
    {
        EnvelopePayload payload = await _service.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);

        Assert.True(EnvelopePayload.TryParse(payload.ToString(), out EnvelopePayload? parsed));
        Assert.Equal([1, 2, 3], (await _service.DecryptAsync(parsed, AssociatedData)).Value);
    }

    [Fact]
    public async Task EncryptAsync_UsesFreshDataKeyPerValue()
    {
        EnvelopePayload first = await _service.EncryptAsync(new byte[] { 1 }, AssociatedData);
        EnvelopePayload second = await _service.EncryptAsync(new byte[] { 1 }, AssociatedData);

        Assert.NotEqual(first.WrappedKey.ToArray(), second.WrappedKey.ToArray());
        Assert.NotEqual(first.Nonce.ToArray(), second.Nonce.ToArray());
    }

    [Fact]
    public async Task EncryptAsync_DisposesGeneratedDataKey()
    {
        await _service.EncryptAsync(new byte[] { 1 }, AssociatedData);

        EnvelopeDataKey dataKey = Assert.Single(_provider.GeneratedKeys);
        Assert.Throws<ObjectDisposedException>(() => dataKey.PlaintextKey.Length);
    }

    [Fact]
    public async Task DecryptAsync_DifferentAssociatedData_ReturnsDecryptionFailed()
    {
        EnvelopePayload payload = await _service.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);

        Result<byte[]> decrypted = await _service.DecryptAsync(payload, Encoding.UTF8.GetBytes("exports/other.csv"));

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task DecryptAsync_TamperedCiphertext_ReturnsDecryptionFailed()
    {
        EnvelopePayload payload = await _service.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);
        byte[] ciphertext = payload.Ciphertext.ToArray();
        ciphertext[0] ^= 0x01;
        var tampered = new EnvelopePayload(payload.MasterKeyId, payload.WrappedKey, payload.Nonce, ciphertext, payload.Tag);

        ResultAssert.Failure(await _service.DecryptAsync(tampered, AssociatedData), CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task DecryptAsync_WrappedKeySwappedBetweenPayloads_Fails()
    {
        EnvelopePayload first = await _service.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);
        EnvelopePayload second = await _service.EncryptAsync(new byte[] { 4, 5, 6 }, AssociatedData);
        var swapped = new EnvelopePayload(first.MasterKeyId, second.WrappedKey, first.Nonce, first.Ciphertext, first.Tag);

        ResultAssert.Failure(await _service.DecryptAsync(swapped, AssociatedData), CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task DecryptAsync_MasterKeyIdChangedToAliasOfSameKey_FailsBecauseHeaderIsAuthenticated()
    {
        EnvelopePayload payload = await _service.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);
        var relabeled = new EnvelopePayload(
            FakeEnvelopeEncryptionProvider.AliasMasterKeyId,
            payload.WrappedKey,
            payload.Nonce,
            payload.Ciphertext,
            payload.Tag);

        Result<byte[]> decrypted = await _service.DecryptAsync(relabeled, AssociatedData);

        (byte[] wrappedKey, string masterKeyId, _) = _provider.UnwrapCalls.Last();
        Assert.Equal(FakeEnvelopeEncryptionProvider.AliasMasterKeyId, masterKeyId);
        Assert.Equal(payload.WrappedKey.ToArray(), wrappedKey);
        ResultAssert.Failure(decrypted, CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task DecryptAsync_ProviderRejectsUnwrap_ReturnsDataKeyUnwrapFailed()
    {
        EnvelopePayload payload = await _service.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);
        var unknownMaster = new EnvelopePayload("attacker-key", payload.WrappedKey, payload.Nonce, payload.Ciphertext, payload.Tag);

        ResultAssert.Failure(await _service.DecryptAsync(unknownMaster, AssociatedData), CryptographyErrorCodes.DataKeyUnwrapFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task DecryptAsync_WrappedKeyCorrupted_ReturnsDataKeyUnwrapFailed()
    {
        EnvelopePayload payload = await _service.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);
        byte[] wrapped = payload.WrappedKey.ToArray();
        wrapped[^1] ^= 0x01;
        var corrupted = new EnvelopePayload(payload.MasterKeyId, wrapped, payload.Nonce, payload.Ciphertext, payload.Tag);

        ResultAssert.Failure(await _service.DecryptAsync(corrupted, AssociatedData), CryptographyErrorCodes.DataKeyUnwrapFailed, ErrorType.Validation);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(64)]
    public async Task EncryptAsync_ProviderGeneratesDataKeyNot32Bytes_Throws(int length)
    {
        _provider.DataKeyLength = length;

        await Assert.ThrowsAsync<CryptographicException>(async () => await _service.EncryptAsync(new byte[] { 1 }, AssociatedData));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(33)]
    public async Task DecryptAsync_UnwrappedKeyWrongLength_ReturnsDataKeyUnwrapFailed(int length)
    {
        EnvelopePayload payload = await _service.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);
        _provider.TransformUnwrappedKey = _ => new byte[length];

        ResultAssert.Failure(await _service.DecryptAsync(payload, AssociatedData), CryptographyErrorCodes.DataKeyUnwrapFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task Members_PassCancellationTokenToProvider()
    {
        using var cts = new CancellationTokenSource();

        EnvelopePayload payload = await _service.EncryptAsync(new byte[] { 1 }, AssociatedData, cts.Token);
        await _service.DecryptAsync(payload, AssociatedData, cts.Token);

        Assert.Equal(cts.Token, Assert.Single(_provider.GenerateTokens));
        Assert.Equal(cts.Token, Assert.Single(_provider.UnwrapCalls).Token);
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new EnvelopeEncryptionService(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _service.DecryptAsync(null!, AssociatedData));
    }
}
