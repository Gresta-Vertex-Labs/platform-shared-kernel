using System.Collections.Concurrent;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.Tests.TestDoubles;

/// <summary>An <see cref="IEncryptionKeyProvider"/> whose answers are supplied per test, counting calls and recording tokens.</summary>
internal sealed class ScriptedKeyProvider : IEncryptionKeyProvider
{
    private int _currentKeyCalls;
    private int _keyCalls;

    public Func<CancellationToken, ValueTask<CryptographicKey>> OnGetCurrentKey { get; set; } =
        _ => throw new InvalidOperationException("GetCurrentKeyAsync was not scripted.");

    public Func<string, CancellationToken, ValueTask<CryptographicKey?>> OnGetKey { get; set; } =
        (_, _) => throw new InvalidOperationException("GetKeyAsync was not scripted.");

    public ConcurrentQueue<CancellationToken> CurrentKeyTokens { get; } = new();

    public ConcurrentQueue<CancellationToken> KeyTokens { get; } = new();

    public int CurrentKeyCalls => Volatile.Read(ref _currentKeyCalls);

    public int KeyCalls => Volatile.Read(ref _keyCalls);

    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _currentKeyCalls);
        CurrentKeyTokens.Enqueue(cancellationToken);
        return OnGetCurrentKey(cancellationToken);
    }

    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _keyCalls);
        KeyTokens.Enqueue(cancellationToken);
        return OnGetKey(keyId, cancellationToken);
    }
}

/// <summary>Delegates to another provider and records every cancellation token it receives.</summary>
internal sealed class TokenRecordingKeyProvider(IEncryptionKeyProvider inner) : IEncryptionKeyProvider
{
    public ConcurrentQueue<CancellationToken> Tokens { get; } = new();

    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
    {
        Tokens.Enqueue(cancellationToken);
        return inner.GetCurrentKeyAsync(cancellationToken);
    }

    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        Tokens.Enqueue(cancellationToken);
        return inner.GetKeyAsync(keyId, cancellationToken);
    }
}
