namespace SharedKernel.Persistence.EfCore.Auditing.Chain;

/// <summary>
/// An out-of-database HMAC key used to compute an audit chain's <c>RecordHash</c>.
/// </summary>
/// <param name="Id">The key's identifier, recorded verbatim on every <c>AuditRecord.KeyId</c> written under it.</param>
/// <param name="Material">The raw key bytes. At least 32 bytes — see <c>IHmacSigner</c>.</param>
public readonly record struct AuditChainKey(string Id, byte[] Material);
