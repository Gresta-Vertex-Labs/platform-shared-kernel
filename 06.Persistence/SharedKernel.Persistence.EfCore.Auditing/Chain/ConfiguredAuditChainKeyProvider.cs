using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.EfCore.Auditing.Chain;

/// <summary>
/// Default <see cref="IAuditChainKeyProvider"/> reading the chain's single HMAC key straight from
/// <see cref="AuditChainOptions"/>.
/// </summary>
/// <remarks>
/// Registered by <c>.WithAuditTrail(IConfiguration)</c> via <c>TryAddSingleton</c> — a
/// consumer wanting a KMS-backed provider registers its own <see cref="IAuditChainKeyProvider"/>
/// first. See that interface's remarks for why key rotation is out of scope this phase — this
/// implementation therefore only ever knows about the one currently-configured key.
/// </remarks>
public sealed class ConfiguredAuditChainKeyProvider : IAuditChainKeyProvider
{
    private readonly AuditChainKey _key;

    /// <summary>Initialises a new <see cref="ConfiguredAuditChainKeyProvider"/> from bound options.</summary>
    public ConfiguredAuditChainKeyProvider(IOptions<AuditChainOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;

        if (string.IsNullOrWhiteSpace(value.HmacKeyBase64))
        {
            throw new InvalidOperationException(
                $"{nameof(AuditChainOptions)}.{nameof(AuditChainOptions.HmacKeyBase64)} is not " +
                "configured. Set it (typically from a secret store) or register a custom " +
                $"{nameof(IAuditChainKeyProvider)} before calling '.WithAuditTrail()'.");
        }

        _key = new AuditChainKey(value.KeyId, Convert.FromBase64String(value.HmacKeyBase64));
    }

    /// <inheritdoc />
    public AuditChainKey GetCurrentKey() => _key;

    /// <inheritdoc />
    public bool TryGetKey(string keyId, out AuditChainKey key)
    {
        if (string.Equals(keyId, _key.Id, StringComparison.Ordinal))
        {
            key = _key;
            return true;
        }

        key = default;
        return false;
    }
}
