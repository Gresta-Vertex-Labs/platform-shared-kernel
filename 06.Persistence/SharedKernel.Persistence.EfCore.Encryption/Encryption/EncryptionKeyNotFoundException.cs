using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Thrown by <see cref="EncryptionInterceptor"/> when the key id recorded in a stored ciphertext cannot be
/// resolved from the registered <c>ISynchronousEncryptionKeyProvider</c> (or, for blind-index/rotation purposes,
/// <c>IEncryptionKeyProvider</c>).
/// </summary>
/// <remarks>
/// This exception indicates that an encryption key was removed, or never warmed, before all rows encrypted with it
/// were rotated. To resolve: make the key available again from the registered key provider (add it back to
/// <c>EncryptionOptions.KeyRingRetiredKeyIds</c> when bridging an asynchronous provider) and complete the key
/// rotation via <c>IEncryptionRotationJob</c>.
/// </remarks>
public sealed class EncryptionKeyNotFoundException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="EncryptionKeyNotFoundException"/> for the specified key id.
    /// </summary>
    /// <param name="version">The key id that could not be resolved.</param>
    public EncryptionKeyNotFoundException(string version)
        : base(
            $"Encryption key '{version}' is not available from the registered key provider. Ensure the key is " +
            "still present before all rows encrypted with it have been rotated, or that it has been warmed into " +
            "EncryptionOptions.KeyRingRetiredKeyIds when bridging an asynchronous key provider.",
            // Unexpected, not NotFound: the caller did nothing wrong and cannot resolve this by retrying with
            // different input — an available key becoming unavailable is an operational/configuration fault
            // (a key removed too early, a key-ring bridge not warmed with the right retired id), never a
            // legitimate 404-shaped "this resource does not exist" outcome.
            Error.Unexpected(
                "Persistence.Encryption.KeyNotFound",
                $"Encryption key '{version}' is not available from the registered key provider."))
    {
        Version = version;
    }

    /// <summary>Gets the key version string that was not found.</summary>
    public string Version { get; }
}
