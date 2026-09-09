using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// A controllable <see cref="IEncryptionKeyProvider"/> test double for
/// <see cref="CachedEncryptionKeyProvider"/> tests — counts calls, can be told to fail its next
/// call, and can be held open (via <see cref="Hold"/>/<see cref="Release"/>) so a test can force
/// genuine concurrent overlap between several in-flight callers rather than relying on them
/// happening to race by chance.
/// </summary>
internal sealed class ControllableEncryptionKeyProvider : IEncryptionKeyProvider
{
    private int _currentKeyCallCount;
    private int _getKeyCallCount;
    private int _canceledCallCount;
    private CryptographicKey _currentKey;
    private Exception? _nextException;
    private TaskCompletionSource? _hold;

    public ControllableEncryptionKeyProvider(CryptographicKey initialKey)
    {
        _currentKey = initialKey;
    }

    public int CurrentKeyCallCount => Volatile.Read(ref _currentKeyCallCount);

    public int GetKeyCallCount => Volatile.Read(ref _getKeyCallCount);

    /// <summary>
    /// How many in-flight <see cref="GetCurrentKeyAsync"/> calls observed their own
    /// <see cref="CancellationToken"/> firing while blocked on <see cref="Hold"/> — i.e. were
    /// genuinely, directly cancelled, as opposed to merely returning to a caller who stopped
    /// waiting on a still-in-flight call (P-511/WO-083's cross-caller-cancellation distinction).
    /// </summary>
    public int CanceledCallCount => Volatile.Read(ref _canceledCallCount);

    public void SetCurrentKey(CryptographicKey key) => _currentKey = key;

    /// <summary>The very next call to either member throws <paramref name="exception"/> instead of returning.</summary>
    public void ThrowOnNextCall(Exception exception) => _nextException = exception;

    /// <summary>
    /// Every subsequent <see cref="GetCurrentKeyAsync"/> call blocks until <see cref="Release"/>
    /// is called, so a test can start N concurrent callers, confirm (implicitly, by the
    /// single-flight contract) they all observe the same in-flight resolution, and only then let
    /// it complete.
    /// </summary>
    public void Hold() => _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _hold?.TrySetResult();

    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _currentKeyCallCount);

        if (_hold is { } hold)
        {
            try
            {
                await hold.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _canceledCallCount);
                throw;
            }
        }

        if (_nextException is { } exception)
        {
            _nextException = null;
            throw exception;
        }

        return _currentKey;
    }

    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _getKeyCallCount);

        if (_nextException is { } exception)
        {
            _nextException = null;
            throw exception;
        }

        return new(_currentKey.Id == keyId ? _currentKey : null);
    }
}
