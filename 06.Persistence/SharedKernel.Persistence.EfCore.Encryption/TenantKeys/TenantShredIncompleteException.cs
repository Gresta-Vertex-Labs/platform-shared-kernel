using System.Globalization;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

/// <summary>
/// Thrown by <see cref="ITenantEncryptionKeyManager.ShredTenantAsync"/> when destroying the tenant's data key would not
/// erase all of its encrypted values, because some are still under a root key or stored as plaintext. Nothing was changed.
/// </summary>
/// <remarks>
/// Move the values onto the tenant's key first — the maintenance job in <c>ReEncrypt</c> (and <c>EncryptPlaintext</c>)
/// mode, inside a cross-tenant scope — or delete them, then shred again. To shred anyway and delete the rows afterwards,
/// pass <see cref="TenantShredOptions.AllowIncompleteErasure"/>. The error type is <c>Conflict</c>: the tenant's data is
/// not in a state the operation can complete from.
/// </remarks>
public sealed class TenantShredIncompleteException : SharedKernelException
{
    /// <summary>The error code carried by this exception.</summary>
    public const string ErrorCode = "Persistence.Encryption.TenantShredIncomplete";

    /// <summary>Initialises a new <see cref="TenantShredIncompleteException"/>.</summary>
    /// <param name="rootKeyValues">The tenant's values under a root key.</param>
    /// <param name="plaintextValues">The tenant's values of encrypted columns stored as plaintext.</param>
    public TenantShredIncompleteException(long rootKeyValues, long plaintextValues)
        : base(
            Describe(rootKeyValues, plaintextValues),
            Error.Conflict(ErrorCode, "The tenant's data cannot be fully erased yet: some of it is not under the tenant's key."))
    {
        RootKeyValues = rootKeyValues;
        PlaintextValues = plaintextValues;
    }

    /// <summary>Gets the number of the tenant's values under a root key.</summary>
    public long RootKeyValues { get; }

    /// <summary>Gets the number of the tenant's values of encrypted columns stored as plaintext.</summary>
    public long PlaintextValues { get; }

    private static string Describe(long rootKeyValues, long plaintextValues) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Shredding the tenant's data key would not erase all of its encrypted values: {rootKeyValues} value(s) are under a root key and {plaintextValues} are stored as plaintext. Nothing was changed. Run the maintenance job with ReEncrypt | EncryptPlaintext (inside a cross-tenant scope) to move them onto the tenant's key, or delete them, then shred again; or pass TenantShredOptions.AllowIncompleteErasure and delete those rows afterwards.");
}
