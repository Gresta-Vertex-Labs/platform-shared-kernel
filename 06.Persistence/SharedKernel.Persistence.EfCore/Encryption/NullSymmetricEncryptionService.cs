using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// No-op implementation of <see cref="ISymmetricEncryptionService"/> used when
/// <c>EfCorePersistenceBuilder.WithEncryption()</c> was not called.
/// Should never be invoked because <c>EncryptionOptions.Enabled</c> defaults to <see langword="false"/>,
/// which causes <see cref="EncryptedValueConverter"/> to pass through without calling this service.
/// </summary>
internal sealed class NullSymmetricEncryptionService : ISymmetricEncryptionService
{
    /// <summary>Gets the singleton instance.</summary>
    public static readonly NullSymmetricEncryptionService Instance = new();

    private NullSymmetricEncryptionService() { }

    /// <inheritdoc />
    public EncryptedPayload Encrypt(byte[] plaintext) =>
        throw new InvalidOperationException(
            "ISymmetricEncryptionService is not registered. Call AddSharedKernelCryptography() " +
            "before using field-level encryption.");

    /// <inheritdoc />
    public Result<byte[]> Decrypt(EncryptedPayload payload) =>
        throw new InvalidOperationException(
            "ISymmetricEncryptionService is not registered. Call AddSharedKernelCryptography() " +
            "before using field-level encryption.");

    /// <inheritdoc />
    public string EncryptToString(string plaintext) =>
        throw new InvalidOperationException(
            "ISymmetricEncryptionService is not registered. Call AddSharedKernelCryptography() " +
            "before using field-level encryption.");

    /// <inheritdoc />
    public Result<string> DecryptToString(string encoded) =>
        throw new InvalidOperationException(
            "ISymmetricEncryptionService is not registered. Call AddSharedKernelCryptography() " +
            "before using field-level encryption.");
}
