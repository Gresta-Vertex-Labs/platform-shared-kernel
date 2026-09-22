using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.EfCore.Auditing.Format;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// The default <see cref="IAuditRecordAuthenticator"/>: HMAC-SHA256 over <c>01.Core</c>'s
/// <see cref="IHmacSigner"/>, keyed by the <see cref="AuditLedgerOptions.Keys"/> keyring.
/// </summary>
/// <remarks>
/// Rotation: add the new key with a higher <see cref="AuditKeyOptions.Order"/>, point
/// <see cref="AuditLedgerOptions.CurrentKeyId"/> at it and keep the old key in the keyring for as long as
/// records sealed under it must verify. After a suspected compromise of the old key, also run
/// <see cref="IAuditLedgerMaintenance.SealAllChainsAsync"/>.
/// </remarks>
internal sealed class KeyringAuditRecordAuthenticator : IAuditRecordAuthenticator
{
    private readonly IHmacSigner _signer;
    private readonly Dictionary<string, (AuditKeyDescriptor Descriptor, byte[] Material)> _keys;
    private readonly AuditKeyDescriptor _current;

    /// <summary>Initialises the keyring from validated options.</summary>
    /// <param name="options">The ledger options.</param>
    /// <param name="signer">The HMAC-SHA256 primitive.</param>
    public KeyringAuditRecordAuthenticator(IOptions<AuditLedgerOptions> options, IHmacSigner signer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(signer);

        var value = options.Value;
        _signer = signer;
        _keys = new Dictionary<string, (AuditKeyDescriptor, byte[])>(StringComparer.Ordinal);
        foreach (var (id, key) in value.Keys)
        {
            if (key?.Material is null)
                continue;
            _keys[id] = (new AuditKeyDescriptor(id, key.Order), Convert.FromBase64String(key.Material));
        }

        if (value.CurrentKeyId is null || !_keys.TryGetValue(value.CurrentKeyId, out var current))
        {
            throw new InvalidOperationException(
                $"{nameof(AuditLedgerOptions)}.{nameof(AuditLedgerOptions.CurrentKeyId)} does not name a configured sealing key.");
        }

        _current = current.Descriptor;
    }

    /// <inheritdoc />
    public string Algorithm => AuditV3Format.HmacSha256;

    /// <inheritdoc />
    public ValueTask<AuditKeyDescriptor> GetCurrentKeyAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_current);

    /// <inheritdoc />
    public ValueTask<AuditKeyDescriptor?> FindKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_keys.TryGetValue(keyId, out var key) ? key.Descriptor : (AuditKeyDescriptor?)null);

    /// <inheritdoc />
    public ValueTask<byte[]> ComputeMacAsync(string keyId, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_signer.Sign(message.Span, Material(keyId)));

    /// <inheritdoc />
    public ValueTask<bool> VerifyMacAsync(string keyId, ReadOnlyMemory<byte> message, ReadOnlyMemory<byte> mac, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_signer.Verify(message.Span, mac.Span, Material(keyId)));

    private byte[] Material(string keyId) =>
        _keys.TryGetValue(keyId, out var key)
            ? key.Material
            : throw new KeyNotFoundException($"Audit sealing key '{keyId}' is not in the configured keyring.");
}
