using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.TestSupport;
using SharedKernel.Cryptography.Random;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;
using static SharedKernel.Cryptography.KeyVault.Azure.Tests.TestSupport.AzureKeyVaultCallCountingFakes;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

/// <summary>
/// P-511/WO-083 coverage: proves the cross-caller-cancellation fix at the three cache sites this
/// package owns — <see cref="AzureKeyVaultEncryptionKeyProvider"/>'s <c>_resolvedKeysByCacheKey</c>
/// (T-77) and <c>_plaintextKeysByTag</c> (T-77), and <see cref="AzureKeyVaultAsymmetricKeyProvider"/>'s
/// <c>_resolvedKeysByAzureKeyName</c> (T-78) — via the holdable/cancellable fakes in
/// <c>TestSupport/AzureKeyVaultCallCountingFakes.cs</c>. Mirrors
/// <c>SharedKernel.Cryptography.Tests.Symmetric.CachedEncryptionKeyProviderTests</c>' identical
/// staggered-cancellation proof for <c>CachedEncryptionKeyProvider</c> (T-76).
/// </summary>
public sealed class AzureKeyVaultCancellationSafetyTests
{
    private static readonly Uri VaultUri = new("https://my-vault.vault.azure.net/");

    private static AzureKeyVaultCryptographyOptions CreateOptions() => new()
    {
        VaultUri = VaultUri,
        CurrentKeyId = "primary",
        KeyNames = new Dictionary<string, string>
        {
            ["primary"] = "tenant-data-key",
            ["rsa-signing"] = "rsa-signing-key",
        },
        Credential = new FakeTokenCredential(),
    };

    private static AzureKeyVaultEncryptionKeyProvider CreateEncryptionProvider(
        SharedVaultState state,
        bool holdableKeyClient = false,
        bool holdableSecretClient = false)
    {
        var factory = new CallCountingCryptographyClientFactory(state);
        return new AzureKeyVaultEncryptionKeyProvider(
            MsOptions.Create(CreateOptions()),
            new CryptoRandomGenerator(),
            holdableKeyClient ? new HoldableFakeKeyClient(state) : new CallCountingFakeKeyClient(state),
            holdableSecretClient ? new HoldableFakeSecretClient(state) : new CallCountingFakeSecretClient(state),
            factory.Create);
    }

    private static AzureKeyVaultAsymmetricKeyProvider CreateAsymmetricProvider(SharedVaultState state) =>
        new(MsOptions.Create(CreateOptions()), new HoldableFakeKeyClient(state));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the allotted timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    // ---- Site 1: AzureKeyVaultEncryptionKeyProvider._resolvedKeysByCacheKey (via MintNewVersionAsync -> GenerateDataKeyAsync -> ResolveAsync) ----

    [Fact]
    public async Task ResolvedKeysByCacheKey_OneCallerCancels_NeverCancelsOrFaultsAnotherConcurrentCaller()
    {
        var state = new SharedVaultState(VaultUri) { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        AzureKeyVaultEncryptionKeyProvider provider = CreateEncryptionProvider(state, holdableKeyClient: true);

        using var callerACts = new CancellationTokenSource();
        Task callerA = provider.MintNewVersionAsync(callerACts.Token).AsTask();
        Task callerB = provider.MintNewVersionAsync().AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        callerACts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callerA);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(callerB.IsCompleted);
        Assert.Equal(0, state.CanceledGetKeyCallCount);

        state.Hold!.TrySetResult();

        await callerB.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.GetKeyCallCount);
        Assert.Equal(0, state.CanceledGetKeyCallCount);
    }

    [Fact]
    public async Task ResolvedKeysByCacheKey_LastCallerCancels_GenuinelyAbandonsInnerCallAndEvictsSlot()
    {
        var state = new SharedVaultState(VaultUri) { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        AzureKeyVaultEncryptionKeyProvider provider = CreateEncryptionProvider(state, holdableKeyClient: true);

        using var soleCallerCts = new CancellationTokenSource();
        Task soleCaller = provider.MintNewVersionAsync(soleCallerCts.Token).AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        soleCallerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => soleCaller);
        await WaitUntilAsync(() => state.CanceledGetKeyCallCount == 1, TimeSpan.FromSeconds(10));

        Task nextCaller = provider.MintNewVersionAsync().AsTask();
        await WaitUntilAsync(() => state.GetKeyCallCount == 2, TimeSpan.FromSeconds(10));

        state.Hold!.TrySetResult();
        await nextCaller.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ---- Site 2: AzureKeyVaultEncryptionKeyProvider._plaintextKeysByTag (via GetKeyAsync -> GetOrAddMemoizedPlaintextKeyAsync) ----

    [Fact]
    public async Task PlaintextKeysByTag_OneCallerCancels_NeverCancelsOrFaultsAnotherConcurrentCaller()
    {
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider minter = CreateEncryptionProvider(state);
        string tag = await minter.MintNewVersionAsync();

        // A separate provider instance (fresh, unseeded caches) so GetKeyAsync genuinely resolves
        // the tag's plaintext key via Key Vault Secrets instead of hitting the minter's own local
        // memoization shortcut.
        state.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AzureKeyVaultEncryptionKeyProvider decryptor = CreateEncryptionProvider(state, holdableSecretClient: true);

        int getSecretCallsBefore = state.GetSecretCallCount;

        using var callerACts = new CancellationTokenSource();
        Task callerA = decryptor.GetKeyAsync(tag, callerACts.Token).AsTask();
        Task callerB = decryptor.GetKeyAsync(tag).AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        callerACts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callerA);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(callerB.IsCompleted);
        Assert.Equal(0, state.CanceledGetSecretCallCount);

        state.Hold!.TrySetResult();

        await callerB.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(getSecretCallsBefore + 1, state.GetSecretCallCount);
        Assert.Equal(0, state.CanceledGetSecretCallCount);
    }

    [Fact]
    public async Task PlaintextKeysByTag_LastCallerCancels_GenuinelyAbandonsInnerCallAndEvictsSlot()
    {
        var state = new SharedVaultState(VaultUri);
        AzureKeyVaultEncryptionKeyProvider minter = CreateEncryptionProvider(state);
        string tag = await minter.MintNewVersionAsync();

        state.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AzureKeyVaultEncryptionKeyProvider decryptor = CreateEncryptionProvider(state, holdableSecretClient: true);
        int getSecretCallsBefore = state.GetSecretCallCount;

        using var soleCallerCts = new CancellationTokenSource();
        Task soleCaller = decryptor.GetKeyAsync(tag, soleCallerCts.Token).AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        soleCallerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => soleCaller);
        await WaitUntilAsync(() => state.CanceledGetSecretCallCount == 1, TimeSpan.FromSeconds(10));

        Task nextCaller = decryptor.GetKeyAsync(tag).AsTask();
        await WaitUntilAsync(() => state.GetSecretCallCount == getSecretCallsBefore + 2, TimeSpan.FromSeconds(10));

        state.Hold!.TrySetResult();
        await nextCaller.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ---- Site 3 (fourth overall): AzureKeyVaultAsymmetricKeyProvider._resolvedKeysByAzureKeyName ----

    [Fact]
    public async Task ResolvedKeysByAzureKeyName_OneCallerCancels_NeverCancelsOrFaultsAnotherConcurrentCaller()
    {
        var state = new SharedVaultState(VaultUri) { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        AzureKeyVaultAsymmetricKeyProvider provider = CreateAsymmetricProvider(state);

        using var callerACts = new CancellationTokenSource();
        Task callerA = provider.GetRsaKeyAsync("rsa-signing", callerACts.Token).AsTask();
        Task callerB = provider.GetRsaKeyAsync("rsa-signing").AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        callerACts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callerA);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(callerB.IsCompleted);
        Assert.Equal(0, state.CanceledGetKeyCallCount);

        state.Hold!.TrySetResult();

        await callerB.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.GetKeyCallCount);
        Assert.Equal(0, state.CanceledGetKeyCallCount);
    }

    [Fact]
    public async Task ResolvedKeysByAzureKeyName_LastCallerCancels_GenuinelyAbandonsInnerCallAndEvictsSlot()
    {
        var state = new SharedVaultState(VaultUri) { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        AzureKeyVaultAsymmetricKeyProvider provider = CreateAsymmetricProvider(state);

        using var soleCallerCts = new CancellationTokenSource();
        Task soleCaller = provider.GetRsaKeyAsync("rsa-signing", soleCallerCts.Token).AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        soleCallerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => soleCaller);
        await WaitUntilAsync(() => state.CanceledGetKeyCallCount == 1, TimeSpan.FromSeconds(10));

        Task nextCaller = provider.GetRsaKeyAsync("rsa-signing").AsTask();
        await WaitUntilAsync(() => state.GetKeyCallCount == 2, TimeSpan.FromSeconds(10));

        state.Hold!.TrySetResult();
        await nextCaller.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
