using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Envelope;

/// <summary>
/// A fresh data key from <see cref="IEnvelopeEncryptionProvider.GenerateDataKeyAsync"/>: its plaintext, for
/// immediate use, and its wrapped form, for storage.
/// </summary>
/// <remarks>
/// Dispose the instance as soon as the plaintext key has been used; disposing zeroes it. Never store or log
/// <see cref="PlaintextKey"/>: only <see cref="WrappedKey"/> and <see cref="MasterKeyId"/> are safe to keep.
/// </remarks>
public sealed class EnvelopeDataKey : IDisposable
{
    private readonly byte[] _plaintextKey;
    private readonly byte[] _wrappedKey;
    private bool _disposed;

    /// <summary>Creates a data key. The buffers are copied.</summary>
    /// <param name="plaintextKey">The plaintext data key. Must not be empty.</param>
    /// <param name="wrappedKey">The data key wrapped by the master key. Must not be empty.</param>
    /// <param name="masterKeyId">The id of the master key that wrapped it.</param>
    /// <exception cref="ArgumentException">An argument is empty.</exception>
    public EnvelopeDataKey(ReadOnlySpan<byte> plaintextKey, ReadOnlySpan<byte> wrappedKey, string masterKeyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterKeyId);

        if (plaintextKey.IsEmpty)
        {
            throw new ArgumentException("The plaintext key must not be empty.", nameof(plaintextKey));
        }

        if (wrappedKey.IsEmpty)
        {
            throw new ArgumentException("The wrapped key must not be empty.", nameof(wrappedKey));
        }

        _plaintextKey = plaintextKey.ToArray();
        _wrappedKey = wrappedKey.ToArray();
        MasterKeyId = masterKeyId;
    }

    /// <summary>The plaintext data key.</summary>
    /// <exception cref="ObjectDisposedException">The instance has been disposed.</exception>
    public ReadOnlySpan<byte> PlaintextKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _plaintextKey;
        }
    }

    /// <summary>The wrapped data key, safe to store.</summary>
    public ReadOnlySpan<byte> WrappedKey => _wrappedKey;

    /// <summary>The id of the master key that wrapped the data key.</summary>
    public string MasterKeyId { get; }

    /// <summary>Zeroes the plaintext key.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_plaintextKey);
            _disposed = true;
        }
    }
}
