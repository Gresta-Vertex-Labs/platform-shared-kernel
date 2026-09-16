using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Thrown by <see cref="EncryptedValueConverter"/> when the key id recorded in a stored
/// ciphertext is not present in <c>EncryptionOptions.Keys</c>.
/// </summary>
/// <remarks>
/// This exception indicates that an encryption key version was removed from configuration before
/// all rows encrypted with that version were rotated. To resolve: add the missing key version
/// back to <c>EncryptionOptions.Keys</c> and complete the key rotation via
/// <see cref="IEncryptionRotationJob"/>.
/// </remarks>
public sealed class EncryptionKeyNotFoundException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="EncryptionKeyNotFoundException"/> for the specified key version.
    /// </summary>
    /// <param name="version">The key version string that was not found in <c>EncryptionOptions.Keys</c>.</param>
    public EncryptionKeyNotFoundException(string version)
        : base(
            $"Encryption key version '{version}' was not found in EncryptionOptions.Keys. " +
            $"Ensure the key is still present before all rows encrypted with it have been rotated.",
            Error.NotFound(
                "Persistence.Encryption.KeyNotFound",
                $"Encryption key version '{version}' was not found in EncryptionOptions.Keys."))
    {
        Version = version;
    }

    /// <summary>Gets the key version string that was not found.</summary>
    public string Version { get; }
}
