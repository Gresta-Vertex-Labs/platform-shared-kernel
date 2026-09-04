using System.Text;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// Covers <see cref="AesGcmEncryptionService"/>'s async surface (P-446/WO-068:
/// <c>EncryptAsync</c>/<c>DecryptAsync</c>/<c>EncryptToStringAsync</c>/<c>DecryptToStringAsync</c>)
/// and the sync-to-async bridging behavior of the retained sync members.
/// </summary>
public sealed class AesGcmEncryptionServiceAsyncTests
{
    [Fact]
    public async Task EncryptAsync_ThenDecryptAsync_RoundTripsPlaintext()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("secret payload");

        EncryptedPayload payload = await service.EncryptAsync(plaintext);
        Result<byte[]> result = await service.DecryptAsync(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public async Task EncryptAsync_GeneratesDifferentNonceEachCall()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("same plaintext");

        EncryptedPayload first = await service.EncryptAsync(plaintext);
        EncryptedPayload second = await service.EncryptAsync(plaintext);

        Assert.NotEqual(first.Nonce, second.Nonce);
    }

    [Fact]
    public async Task DecryptAsync_WithFlippedCiphertextByte_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = await service.EncryptAsync(Encoding.UTF8.GetBytes("secret"));

        byte[] tamperedCiphertext = [.. payload.Ciphertext];
        tamperedCiphertext[0] ^= 0xFF;
        var tampered = new EncryptedPayload(payload.KeyId, payload.Nonce, tamperedCiphertext, payload.Tag);

        Result<byte[]> result = await service.DecryptAsync(tampered);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public async Task DecryptAsync_WithUnknownKeyId_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = await service.EncryptAsync(Encoding.UTF8.GetBytes("secret"));

        var withUnknownKey = new EncryptedPayload("retired-key", payload.Nonce, payload.Ciphertext, payload.Tag);
        Result<byte[]> result = await service.DecryptAsync(withUnknownKey);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.UnknownKeyId, result.Error.Code);
    }

    [Fact]
    public async Task EncryptToStringAsync_ThenDecryptToStringAsync_RoundTripsPlaintext()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);

        string encoded = await service.EncryptToStringAsync("hello world");
        Result<string> result = await service.DecryptToStringAsync(encoded);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello world", result.Value);
    }

    [Fact]
    public async Task DecryptToStringAsync_WithMalformedInput_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);

        Result<string> result = await service.DecryptToStringAsync("not-valid-base64-payload-!!!");

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.MalformedPayload, result.Error.Code);
    }

    [Fact]
    public async Task Decrypt_AndDecryptAsync_OnTheSamePayload_ProduceByteIdenticalPlaintext()
    {
        // Encrypt() and EncryptAsync() cannot be compared byte-for-byte against each other — a
        // fresh random nonce is generated on every call by design, so ciphertext legitimately
        // differs call to call regardless of sync/async. What IS directly comparable, and what
        // AesGcmEncryptionService's shared EncryptCore/DecryptCore design guarantees, is that
        // decrypting the SAME EncryptedPayload via the sync and the async member produces
        // byte-identical plaintext.
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = service.Encrypt(Encoding.UTF8.GetBytes("compare me"));

        Result<byte[]> syncResult = service.Decrypt(payload);
        Result<byte[]> asyncResult = await service.DecryptAsync(payload);

        Assert.True(syncResult.IsSuccess);
        Assert.True(asyncResult.IsSuccess);
        Assert.Equal(syncResult.Value, asyncResult.Value);
    }

    [Fact]
    public async Task Encrypt_ThenDecryptAsync_And_EncryptAsync_ThenDecrypt_BothRoundTripCorrectly()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("cross sync/async round trip");

        EncryptedPayload viaSync = service.Encrypt(plaintext);
        Result<byte[]> decryptedViaAsync = await service.DecryptAsync(viaSync);

        EncryptedPayload viaAsync = await service.EncryptAsync(plaintext);
        Result<byte[]> decryptedViaSync = service.Decrypt(viaAsync);

        Assert.True(decryptedViaAsync.IsSuccess);
        Assert.Equal(plaintext, decryptedViaAsync.Value);
        Assert.True(decryptedViaSync.IsSuccess);
        Assert.Equal(plaintext, decryptedViaSync.Value);
    }

    [Fact]
    public async Task EncryptAsync_HonorsCancellation_WhenKeyProviderResolutionIsPending()
    {
        var keyProvider = new ControllableEncryptionKeyProvider(new CryptographicKey("v1", new byte[32]));
        keyProvider.Hold(); // never releases — forces the awaited ValueTask to stay pending
        var service = new AesGcmEncryptionService(keyProvider);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.EncryptAsync(Encoding.UTF8.GetBytes("payload"), cts.Token).AsTask());
    }

    [Fact]
    public async Task Encrypt_SyncBridge_DoesNotStarveThreadPoolUnderConstrainedWorkerThreads()
    {
        // What this test proves, and what it does not:
        //
        // It does NOT (and cannot, purely in-process) measure "zero thread-pool interaction" in
        // the abstract. That property is true by construction: Encrypt()'s
        // `.GetAwaiter().GetResult()` call observes an already-completed ValueTask when the
        // registered IEncryptionKeyProvider resolves synchronously (as InMemoryEncryptionKeyProvider
        // does here) — an already-completed ValueTask's GetResult() reads a field, it never
        // schedules or waits on a continuation, so there is nothing for the thread pool to starve.
        //
        // What it DOES prove: it is a regression guard against the actual failure mode "genuinely
        // non-blocking" is meant to rule out — a future change accidentally turning the bridge
        // into a real sync-over-async wait (e.g. an implementation that yields internally even
        // when logically synchronous). Under a thread pool constrained to a single minimum worker
        // thread, running far more concurrent Encrypt() calls than that would deadlock or badly
        // stall if the bridge ever became a genuine blocking wait on a scheduled continuation,
        // because .NET's thread pool injects additional threads only slowly once starved. A truly
        // synchronous bridge has no dependency on thread-pool growth at all and completes well
        // inside the tight timeout below regardless of the constrained minimum.
        ThreadPool.GetMinThreads(out int originalWorkerMin, out int originalIoMin);
        try
        {
            ThreadPool.SetMinThreads(1, 1);

            var keyProvider = new InMemoryEncryptionKeyProvider();
            var service = new AesGcmEncryptionService(keyProvider);
            const int concurrency = 200;
            byte[] plaintext = Encoding.UTF8.GetBytes("payload");

            Task<EncryptedPayload>[] tasks = [.. Enumerable.Range(0, concurrency)
                .Select(_ => Task.Run(() => service.Encrypt(plaintext)))];

            EncryptedPayload[] results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(concurrency, results.Length);
        }
        finally
        {
            ThreadPool.SetMinThreads(originalWorkerMin, originalIoMin);
        }
    }

    [Fact]
    public async Task Decrypt_SyncBridge_DoesNotStarveThreadPoolUnderConstrainedWorkerThreads()
    {
        // See the extensive remarks on Encrypt_SyncBridge_DoesNotStarveThreadPoolUnderConstrainedWorkerThreads
        // above — identical reasoning, applied to the Decrypt() bridge.
        ThreadPool.GetMinThreads(out int originalWorkerMin, out int originalIoMin);
        try
        {
            var keyProvider = new InMemoryEncryptionKeyProvider();
            var service = new AesGcmEncryptionService(keyProvider);
            EncryptedPayload payload = service.Encrypt(Encoding.UTF8.GetBytes("payload"));

            ThreadPool.SetMinThreads(1, 1);

            const int concurrency = 200;
            Task<Result<byte[]>>[] tasks = [.. Enumerable.Range(0, concurrency)
                .Select(_ => Task.Run(() => service.Decrypt(payload)))];

            Result<byte[]>[] results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.All(results, r => Assert.True(r.IsSuccess));
        }
        finally
        {
            ThreadPool.SetMinThreads(originalWorkerMin, originalIoMin);
        }
    }
}
