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

    private const string NotRegisteredMessage =
        "ISymmetricEncryptionService is not registered. Call AddSharedKernelCryptography() " +
        "before using field-level encryption.";

    /// <inheritdoc />
    public EncryptedPayload Encrypt(byte[] plaintext) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public ValueTask<EncryptedPayload> EncryptAsync(byte[] plaintext, CancellationToken ct = default) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public Result<byte[]> Decrypt(EncryptedPayload payload) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public ValueTask<Result<byte[]>> DecryptAsync(EncryptedPayload payload, CancellationToken ct = default) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public string EncryptToString(string plaintext) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public ValueTask<string> EncryptToStringAsync(string plaintext, CancellationToken ct = default) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public Result<string> DecryptToString(string encoded) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public ValueTask<Result<string>> DecryptToStringAsync(string encoded, CancellationToken ct = default) =>
        throw new InvalidOperationException(NotRegisteredMessage);
}
