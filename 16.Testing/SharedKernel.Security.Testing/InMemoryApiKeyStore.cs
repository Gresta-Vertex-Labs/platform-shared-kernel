using SharedKernel.Execution.Tenancy;
using System.Collections.Concurrent;
using SharedKernel.Security.ApiKey.Keys;

namespace SharedKernel.Testing.Security;

/// <summary>An in-memory <see cref="IApiKeyStore"/> for tests.</summary>
public sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly ConcurrentDictionary<string, ApiKeyRecord> _records = new(StringComparer.Ordinal);

    /// <summary>Adds or replaces a record.</summary>
    /// <param name="record">The record.</param>
    public void Add(ApiKeyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[record.KeyId] = record;
    }

    /// <summary>Stores a generated key for a client and returns the record.</summary>
    /// <param name="key">The generated key.</param>
    /// <param name="clientId">The client the key belongs to.</param>
    /// <param name="tenantId">The tenant the key is limited to, if any.</param>
    /// <param name="permissions">The permissions granted to the key.</param>
    /// <param name="expiresAt">When the key expires, if ever.</param>
    /// <returns>The stored record.</returns>
    public ApiKeyRecord Add(
        GeneratedApiKey key,
        string clientId,
        TenantId? tenantId = null,
        IReadOnlyCollection<string>? permissions = null,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        var record = new ApiKeyRecord(key.KeyId, key.KeyHash, clientId)
        {
            TenantId = tenantId,
            Permissions = permissions ?? [],
            ExpiresAt = expiresAt,
        };
        Add(record);
        return record;
    }

    /// <summary>Revokes a key.</summary>
    /// <param name="keyId">The key id.</param>
    /// <param name="revokedAt">When the key was revoked.</param>
    /// <exception cref="KeyNotFoundException">No key has this id.</exception>
    public void Revoke(string keyId, DateTimeOffset revokedAt)
    {
        ApiKeyRecord record = _records[keyId];
        _records[keyId] = new ApiKeyRecord(record.KeyId, record.KeyHash, record.ClientId)
        {
            TenantId = record.TenantId,
            Roles = record.Roles,
            Permissions = record.Permissions,
            ExpiresAt = record.ExpiresAt,
            RevokedAt = revokedAt,
        };
    }

    /// <inheritdoc/>
    public ValueTask<ApiKeyRecord?> FindAsync(string keyId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        return ValueTask.FromResult(_records.TryGetValue(keyId, out ApiKeyRecord? record) ? record : null);
    }
}
