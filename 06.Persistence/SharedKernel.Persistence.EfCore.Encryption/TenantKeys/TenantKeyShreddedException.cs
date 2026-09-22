using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

/// <summary>
/// Thrown when a value belongs to a tenant whose data key was shredded with
/// <see cref="ITenantEncryptionKeyManager.ShredTenantAsync"/>, or when new data is written for such a tenant.
/// </summary>
/// <remarks>
/// Shredding is permanent: the value can never be decrypted again, by anyone. Reaching it usually means rows of an
/// erased tenant were kept (for referential integrity, say) and are still being read; delete them, or exclude them
/// from the query. The error type is <c>NotFound</c> because the data no longer exists in readable form. It is
/// deliberately distinct from <see cref="EncryptionKeyNotFoundException"/>, which reports a key that is merely
/// missing and can be restored.
/// </remarks>
public sealed class TenantKeyShreddedException : SharedKernelException
{
    /// <summary>The error code carried by this exception.</summary>
    public const string ErrorCode = "Persistence.Encryption.TenantKeyShredded";

    /// <summary>Initialises a new <see cref="TenantKeyShreddedException"/>.</summary>
    public TenantKeyShreddedException()
        : base(
            "The tenant's data key was shredded; its encrypted values can no longer be read or written.",
            Error.NotFound(ErrorCode, "The tenant's data was erased and can no longer be read or written."))
    {
    }
}
