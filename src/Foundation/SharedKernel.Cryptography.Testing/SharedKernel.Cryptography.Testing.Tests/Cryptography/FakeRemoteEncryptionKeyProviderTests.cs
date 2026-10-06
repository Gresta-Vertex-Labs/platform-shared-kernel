using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeRemoteEncryptionKeyProvider"/> behaves like a key management service: asynchronous only,
/// never completing synchronously, honoring cancellation, and counting every lookup.
/// </summary>
public sealed class FakeRemoteEncryptionKeyProviderTests
{
    [Fact]
    public void Provider_IsAsynchronousOnly()
    {
        Assert.True(typeof(IEncryptionKeyProvider).IsAssignableFrom(typeof(FakeRemoteEncryptionKeyProvider)));
        Assert.False(typeof(ISynchronousEncryptionKeyProvider).IsAssignableFrom(typeof(FakeRemoteEncryptionKeyProvider)));
    }

    [Fact]
    public async Task Lookups_NeverCompleteSynchronously()
    {
        var provider = new FakeRemoteEncryptionKeyProvider();
        var context = new ManualSynchronizationContext();
        SynchronizationContext? original = SynchronizationContext.Current;
        ValueTask<CryptographicKey> current;
        ValueTask<CryptographicKey?> byId;

        // Continuations are queued on the manual context, so nothing can complete until the queue is run.
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            current = provider.GetCurrentKeyAsync();
            byId = provider.GetKeyAsync("v1");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        Assert.False(current.IsCompleted);
        Assert.False(byId.IsCompleted);

        context.RunPendingWork();

        Assert.True(current.IsCompletedSuccessfully);
        Assert.True(byId.IsCompletedSuccessfully);
        Assert.Equal("v1", (await current).Id);
        Assert.Equal("v1", (await byId)!.Id);
    }

    [Fact]
    public async Task Constructor_SeedsARandom32ByteCurrentKey()
    {
        var provider = new FakeRemoteEncryptionKeyProvider(currentKeyId: "custom");

        CryptographicKey current = await provider.GetCurrentKeyAsync();

        Assert.Equal("custom", current.Id);
        Assert.Equal(32, current.Material.Length);
    }

    [Fact]
    public async Task CallCounts_CountEveryLookup()
    {
        var provider = new FakeRemoteEncryptionKeyProvider();

        await provider.GetCurrentKeyAsync();
        await provider.GetCurrentKeyAsync();
        await provider.GetKeyAsync("v1");
        await provider.GetKeyAsync("missing");
        await provider.GetKeyAsync("v1");

        Assert.Equal(2, provider.CurrentKeyCallCount);
        Assert.Equal(3, provider.KeyCallCount);
    }

    [Fact]
    public async Task AddKey_SetCurrentKey_RemoveKey_RotateKeys()
    {
        var provider = new FakeRemoteEncryptionKeyProvider();
        CryptographicKey v2 = provider.AddKey("v2");

        provider.SetCurrentKey("v2");
        provider.RemoveKey("v1");

        Assert.Same(v2, await provider.GetCurrentKeyAsync());
        Assert.Same(v2, await provider.GetKeyAsync("v2"));
        Assert.Null(await provider.GetKeyAsync("v1"));
    }

    [Fact]
    public void SetCurrentKey_UnknownKeyId_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => new FakeRemoteEncryptionKeyProvider().SetCurrentKey("never-added"));

    [Fact]
    public async Task Lookups_WithACancelledToken_ThrowOperationCanceled()
    {
        var provider = new FakeRemoteEncryptionKeyProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.GetCurrentKeyAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.GetKeyAsync("v1", cts.Token));
    }

    /// <summary>Queues posted continuations until <see cref="RunPendingWork"/>, so completion order is deterministic.</summary>
    private sealed class ManualSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();

        public override void Post(SendOrPostCallback d, object? state) => _pending.Enqueue((d, state));

        public void RunPendingWork()
        {
            while (_pending.TryDequeue(out var work))
            {
                work.Callback(work.State);
            }
        }
    }
}
