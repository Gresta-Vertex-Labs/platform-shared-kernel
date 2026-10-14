using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// A stored encrypted value or a tenant data key could not be used: the payload is malformed, decryption failed
/// (the value was altered, or copied from another row, column or tenant), the envelope provider returned an invalid
/// data key, or it could not unwrap the stored one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SharedKernelException.Error"/> carries a fixed, client-safe message. Neither it nor the exception
/// <see cref="Exception.Message"/> ever holds a stored value, a key id of the provider, a URL or the provider's own text;
/// the exception message may name the entity property and the remedy.
/// </para>
/// <list type="table">
/// <listheader><term>Code</term><description>Meaning (<see cref="ErrorType"/>)</description></listheader>
/// <item><term><see cref="PersistenceErrorCodes.Encryption.MalformedPayload"/></term><description>The stored value is not an encrypted payload: written unencrypted, truncated or altered (<see cref="ErrorType.Unexpected"/>).</description></item>
/// <item><term><see cref="PersistenceErrorCodes.Encryption.DecryptionFailed"/></term><description>The payload is well-formed but does not authenticate (<see cref="ErrorType.Unexpected"/>).</description></item>
/// <item><term><see cref="PersistenceErrorCodes.Encryption.DataKeyInvalid"/></term><description>The envelope provider generated a data key that is too short (<see cref="ErrorType.Unexpected"/>).</description></item>
/// <item><term><see cref="PersistenceErrorCodes.Encryption.DataKeyUnwrapFailed"/></term><description>The provider could not unwrap the stored data key: <see cref="ErrorType.Unavailable"/> or <see cref="ErrorType.Timeout"/> when the provider said so, else <see cref="ErrorType.Unexpected"/>.</description></item>
/// </list>
/// A key that is merely not available is <see cref="EncryptionKeyNotFoundException"/>; a shredded tenant is
/// <see cref="TenantKeys.TenantKeyShreddedException"/>.
/// </remarks>
public sealed class FieldEncryptionException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="FieldEncryptionException"/>.</summary>
    /// <param name="error">The classified error.</param>
    public FieldEncryptionException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises a new <see cref="FieldEncryptionException"/> that wraps an underlying failure.</summary>
    /// <param name="error">The classified error.</param>
    /// <param name="innerException">The underlying failure.</param>
    public FieldEncryptionException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }

    internal FieldEncryptionException(string message, Error error)
        : base(message, error)
    {
    }
}
