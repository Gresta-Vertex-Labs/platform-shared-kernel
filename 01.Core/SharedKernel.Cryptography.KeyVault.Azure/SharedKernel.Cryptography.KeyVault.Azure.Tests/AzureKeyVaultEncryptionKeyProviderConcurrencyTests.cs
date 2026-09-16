using SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;
using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

public sealed class AzureKeyVaultEncryptionKeyProviderConcurrencyTests
{
    private readonly EncryptionFixture _fixture = new();

    [Fact]
    public async Task GetCurrentKeyAsync_TwentyConcurrentCallersOnColdCache_ReadVaultOnce()
    {
        string version = await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Vault.Gate = gate;

        Task<CryptographicKey>[] callers =
        [
            .. Enumerable.Range(0, 20).Select(_ => Task.Run(async () => await provider.GetCurrentKeyAsync())),
        ];
        await EncryptionFixture.WaitUntilAsync(() => _fixture.Vault.ListCalls >= 1);
        await Task.Delay(50);
        gate.SetResult();
        CryptographicKey[] keys = await Task.WhenAll(callers);

        Assert.All(keys, key => Assert.Equal(version, key.Id));
        Assert.Equal(1, _fixture.Vault.ListCalls);
        Assert.Equal(1, _fixture.Vault.GetSecretCalls);
        Assert.Equal(1, _fixture.Vault.UnwrapCalls);
        Assert.Equal(1, _fixture.Vault.GetKeyCalls);
        Assert.Equal(1, _fixture.Vault.ListKeyVersionsCalls);
    }

    [Fact]
    public async Task GetKeyAsync_TwentyConcurrentCallersForSameVersion_UnwrapOnce()
    {
        string version = await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        await provider.GetKeyAsync(Guid.NewGuid().ToString("N"));
        _fixture.Vault.ResetCounters();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Vault.Gate = gate;

        Task<CryptographicKey?>[] callers =
        [
            .. Enumerable.Range(0, 20).Select(_ => Task.Run(async () => await provider.GetKeyAsync(version))),
        ];
        await EncryptionFixture.WaitUntilAsync(() => _fixture.Vault.GetSecretCalls >= 1);
        await Task.Delay(50);
        gate.SetResult();
        CryptographicKey?[] keys = await Task.WhenAll(callers);

        Assert.All(keys, key => Assert.Equal(version, key?.Id));
        Assert.Equal(0, _fixture.Vault.ListCalls);
        Assert.Equal(1, _fixture.Vault.GetSecretCalls);
        Assert.Equal(1, _fixture.Vault.UnwrapCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_OneOfTwoWaitersCancelled_OtherStillGetsKey()
    {
        string version = await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Vault.Gate = gate;
        using var cancellation = new CancellationTokenSource();

        Task<CryptographicKey> cancelled = provider.GetCurrentKeyAsync(cancellation.Token).AsTask();
        Task<CryptographicKey> survivor = provider.GetCurrentKeyAsync().AsTask();
        await EncryptionFixture.WaitUntilAsync(() => _fixture.Vault.ListCalls >= 1);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(survivor.IsCompleted);

        gate.SetResult();
        CryptographicKey key = await survivor;

        Assert.Equal(version, key.Id);
        Assert.Equal(1, _fixture.Vault.ListCalls);
        Assert.Equal(1, _fixture.Vault.GetSecretCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_OnlyWaiterCancelled_NextCallerStartsFreshLoad()
    {
        string version = await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.ResetCounters();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Vault.Gate = gate;
        using var cancellation = new CancellationTokenSource();

        Task<CryptographicKey> cancelled = provider.GetCurrentKeyAsync(cancellation.Token).AsTask();
        await EncryptionFixture.WaitUntilAsync(() => _fixture.Vault.ListCalls >= 1);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        gate.SetResult();
        CryptographicKey key = await provider.GetCurrentKeyAsync();

        Assert.Equal(version, key.Id);
        Assert.Equal(2, _fixture.Vault.ListCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_VaultFailure_IsNotCached()
    {
        await _fixture.CreateProvider().RotateDataKeyAsync();
        AzureKeyVaultEncryptionKeyProvider provider = _fixture.CreateProvider();
        _fixture.Vault.UnwrapException = new global::Azure.RequestFailedException(503, "Service unavailable");

        await Assert.ThrowsAsync<global::Azure.RequestFailedException>(async () => await provider.GetCurrentKeyAsync());

        _fixture.Vault.UnwrapException = null;
        CryptographicKey key = await provider.GetCurrentKeyAsync();

        Assert.Equal(32, key.Material.Length);
    }
}
