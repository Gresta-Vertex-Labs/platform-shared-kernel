using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// No-op implementation of <see cref="IEncryptionKeyProvider"/> used when
/// <c>EfCorePersistenceBuilder.WithEncryption()</c> was not called.
/// Should never be invoked because <c>EncryptionOptions.Enabled</c> defaults to <see langword="false"/>,
/// which causes <see cref="EncryptedValueConverter"/> to pass through before reaching key resolution.
/// </summary>
internal sealed class NullEncryptionKeyProvider : IEncryptionKeyProvider
{
    /// <summary>Gets the singleton instance.</summary>
    public static readonly NullEncryptionKeyProvider Instance = new();

    private NullEncryptionKeyProvider() { }

    /// <inheritdoc />
    public CryptographicKey GetCurrentKey() =>
        throw new InvalidOperationException(
            "IEncryptionKeyProvider is not registered. Call AddSharedKernelCryptography() " +
            "and configure WithEncryption() before using field-level encryption.");

    /// <inheritdoc />
    public CryptographicKey? GetKey(string keyId) => null;
}
