using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// No-op implementation of <see cref="ISynchronousSymmetricEncryptionService"/> used when
/// <c>EfCorePersistenceBuilder.WithEncryption()</c> was not called.
/// Should never be invoked because <c>EncryptionOptions.Enabled</c> defaults to <see langword="false"/>,
/// which causes <see cref="EncryptedValueConverter"/> to pass through without calling this service.
/// </summary>
/// <remarks>Every member throws <see cref="InvalidOperationException"/>.</remarks>
internal sealed class NullSymmetricEncryptionService : ISynchronousSymmetricEncryptionService
{
    /// <summary>Gets the singleton instance.</summary>
    public static readonly NullSymmetricEncryptionService Instance = new();

    private const string NotRegisteredMessage =
        "Field-level encryption is not configured. Call EfCorePersistenceBuilder.WithEncryption(...) " +
        "before enabling EncryptionOptions.Enabled.";

    private NullSymmetricEncryptionService() { }

    /// <inheritdoc />
    public EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public Result<byte[]> Decrypt(EncryptedPayload payload, ReadOnlySpan<byte> associatedData) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public string EncryptToString(string plaintext, ReadOnlySpan<byte> associatedData) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public Result<string> DecryptToString(string encoded, ReadOnlySpan<byte> associatedData) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public bool IsEncryptedWithCurrentKey(EncryptedPayload payload) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public Result<EncryptedPayload> ReEncrypt(EncryptedPayload payload, ReadOnlySpan<byte> associatedData) =>
        throw new InvalidOperationException(NotRegisteredMessage);
}
