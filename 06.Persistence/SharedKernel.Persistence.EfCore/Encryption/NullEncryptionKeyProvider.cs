using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// No-op implementation of <see cref="IEncryptionKeyProvider"/> used when
/// <c>EfCorePersistenceBuilder.WithEncryption()</c> was not called.
/// Should never be invoked because <c>EncryptionOptions.Enabled</c> defaults to <see langword="false"/>,
/// which causes <see cref="EncryptedValueConverter"/> to pass through before reaching key resolution.
/// </summary>
/// <remarks>
/// <strong>D-110/P-448 (breaking, cascading from <c>01.Core</c>'s P-446):</strong> migrated to the
/// asynchronous <see cref="IEncryptionKeyProvider"/> shape. <see cref="GetCurrentKeyAsync"/> still
/// throws the same <see cref="InvalidOperationException"/> — SYNCHRONOUSLY, before constructing any
/// <see cref="ValueTask{TResult}"/>, not via a faulted one — behavior-preserving relative to the
/// previous synchronous <c>GetCurrentKey()</c>. <see cref="GetKeyAsync"/> still returns an
/// already-completed <see langword="null"/> result, unchanged.
/// </remarks>
internal sealed class NullEncryptionKeyProvider : IEncryptionKeyProvider
{
    /// <summary>Gets the singleton instance.</summary>
    public static readonly NullEncryptionKeyProvider Instance = new();

    private NullEncryptionKeyProvider() { }

    /// <inheritdoc />
    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException(
            "IEncryptionKeyProvider is not registered. Call AddSharedKernelCryptography() " +
            "and configure WithEncryption() before using field-level encryption.");

    /// <inheritdoc />
    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
        new((CryptographicKey?)null);
}
