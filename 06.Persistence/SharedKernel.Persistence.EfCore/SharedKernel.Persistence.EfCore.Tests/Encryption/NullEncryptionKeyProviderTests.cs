using FluentAssertions;
using SharedKernel.Persistence.EfCore.Encryption;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-127: <see cref="NullEncryptionKeyProvider"/> async-contract unit tests (P-448/D-110) — the
/// no-op provider used when <c>EfCorePersistenceBuilder.WithEncryption()</c> was never called.
/// </summary>
public sealed class NullEncryptionKeyProviderTests
{
    [Fact]
    public void GetCurrentKeyAsync_Throws_InvalidOperationException_Synchronously()
    {
        // D-110: NullEncryptionKeyProvider.GetCurrentKeyAsync must still throw synchronously — BEFORE
        // constructing any ValueTask — not via a faulted ValueTask, behavior-preserving relative to
        // the previous synchronous GetCurrentKey().
        var act = () => NullEncryptionKeyProvider.Instance.GetCurrentKeyAsync();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddSharedKernelCryptography*WithEncryption*");
    }

    [Fact]
    public async Task GetCurrentKeyAsync_Awaited_Also_Throws_InvalidOperationException()
    {
        // Confirm the synchronous throw is also observable through the normal await path (it never
        // gets wrapped/rethrown as anything else, e.g. a faulted-ValueTask AggregateException).
        var act = async () => await NullEncryptionKeyProvider.Instance.GetCurrentKeyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetKeyAsync_Returns_Null_Not_Throw_ForAnyKeyId()
    {
        // GetKeyAsync must still return an already-completed null result, unchanged — it must never
        // throw, regardless of which keyId is requested.
        var result = await NullEncryptionKeyProvider.Instance.GetKeyAsync("v1");

        result.Should().BeNull();
    }

    [Fact]
    public void GetKeyAsync_Completes_Synchronously()
    {
        // D-110/P-448: no genuine I/O — GetKeyAsync must return an already-completed ValueTask
        // (IsCompletedSuccessfully == true), proving AesGcmEncryptionService's internal
        // .GetAwaiter().GetResult() bridge never blocks a thread on real I/O for this no-op provider.
        var keyTask = NullEncryptionKeyProvider.Instance.GetKeyAsync("any-version");

        keyTask.IsCompletedSuccessfully.Should().BeTrue(
            "the no-op provider resolves synchronously — it never performs I/O");
    }

    [Fact]
    public void Instance_Is_A_Singleton()
    {
        // The provider is stateless — Instance should be the same reference every time.
        NullEncryptionKeyProvider.Instance.Should().BeSameAs(NullEncryptionKeyProvider.Instance);
    }
}
