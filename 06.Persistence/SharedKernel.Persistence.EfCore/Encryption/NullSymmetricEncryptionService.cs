using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// No-op implementation of <see cref="ISymmetricEncryptionService"/> used when
/// <c>EfCorePersistenceBuilder.WithEncryption()</c> was not called.
/// Should never be invoked because <c>EncryptionOptions.Enabled</c> defaults to <see langword="false"/>,
/// which causes <see cref="EncryptedValueConverter"/> to pass through without calling this service.
/// </summary>
/// <remarks>
/// <b>P-491/WO-081:</b> every member below gained a required <c>associatedData</c> parameter when
/// <c>01.Core</c>'s <see cref="ISymmetricEncryptionService"/> made AAD mandatory — this type never
/// reaches its cryptographic core (every member throws unconditionally), so the new parameter is
/// accepted and ignored, mirroring the pre-existing behavior exactly.
/// </remarks>
internal sealed class NullSymmetricEncryptionService : ISymmetricEncryptionService
{
    /// <summary>Gets the singleton instance.</summary>
    public static readonly NullSymmetricEncryptionService Instance = new();

    private NullSymmetricEncryptionService() { }

    private const string NotRegisteredMessage =
        "ISymmetricEncryptionService is not registered. Call AddSharedKernelCryptography() " +
        "before using field-level encryption.";

    /// <inheritdoc />
    public EncryptedPayload Encrypt(byte[] plaintext, byte[] associatedData) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public ValueTask<EncryptedPayload> EncryptAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct = default) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public Result<byte[]> Decrypt(EncryptedPayload payload, byte[] associatedData) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public ValueTask<Result<byte[]>> DecryptAsync(EncryptedPayload payload, byte[] associatedData, CancellationToken ct = default) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public string EncryptToString(string plaintext, byte[] associatedData) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public ValueTask<string> EncryptToStringAsync(string plaintext, byte[] associatedData, CancellationToken ct = default) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public Result<string> DecryptToString(string encoded, byte[] associatedData) =>
        throw new InvalidOperationException(NotRegisteredMessage);

    /// <inheritdoc />
    public ValueTask<Result<string>> DecryptToStringAsync(string encoded, byte[] associatedData, CancellationToken ct = default) =>
        throw new InvalidOperationException(NotRegisteredMessage);
}
