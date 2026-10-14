using SharedKernel.Core.Exceptions;

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
    /// <summary>Initialises a new <see cref="TenantKeyShreddedException"/>.</summary>
    public TenantKeyShreddedException()
        : base(EncryptionErrors.TenantKeyShredded())
    {
    }
}
