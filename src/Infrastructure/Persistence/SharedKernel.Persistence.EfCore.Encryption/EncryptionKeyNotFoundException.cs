using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Thrown when a stored value names an encryption key that the configured key source cannot supply.
/// </summary>
/// <remarks>
/// A key was removed before every value encrypted with it was re-encrypted, or, with an asynchronous-only key
/// provider, this process has not loaded the key yet. In the second case the key is fetched in the background and
/// the next read succeeds; list ids that must be readable from the first request in
/// <see cref="EncryptionOptions.AdditionalDecryptionKeyIds"/>. Before retiring a key, run the maintenance job in
/// <c>VerifyOnly</c> mode and check that no value still uses it.
/// </remarks>
public sealed class EncryptionKeyNotFoundException : SharedKernelException
{
    /// <summary>The error code carried by this exception.</summary>
    public const string ErrorCode = "Persistence.Encryption.KeyNotFound";

    /// <summary>Initialises a new <see cref="EncryptionKeyNotFoundException"/>.</summary>
    /// <param name="keyId">The key id that could not be resolved.</param>
    public EncryptionKeyNotFoundException(string keyId)
        : base(
            $"Encryption key '{keyId}' is not available from the configured key source.",
            Error.Unexpected(ErrorCode, $"Encryption key '{keyId}' is not available from the configured key source."))
    {
        KeyId = keyId;
    }

    /// <summary>The key id that could not be resolved.</summary>
    public string KeyId { get; }
}
