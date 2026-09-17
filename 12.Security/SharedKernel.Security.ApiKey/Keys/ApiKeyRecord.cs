namespace SharedKernel.Security.ApiKey.Keys;

/// <summary>A stored managed API key.</summary>
public sealed class ApiKeyRecord
{
    /// <summary>Creates a record.</summary>
    /// <param name="keyId">The key id from <see cref="GeneratedApiKey.KeyId"/>.</param>
    /// <param name="keyHash">The hash from <see cref="GeneratedApiKey.KeyHash"/>.</param>
    /// <param name="clientId">The client the key belongs to; becomes the caller's subject id.</param>
    /// <exception cref="ArgumentException">An argument is null, empty or whitespace.</exception>
    public ApiKeyRecord(string keyId, string keyHash, string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        KeyId = keyId;
        KeyHash = keyHash;
        ClientId = clientId;
    }

    /// <summary>Gets the key id.</summary>
    public string KeyId { get; }

    /// <summary>Gets the Base64url SHA-256 hash of the key.</summary>
    public string KeyHash { get; }

    /// <summary>Gets the client the key belongs to.</summary>
    public string ClientId { get; }

    /// <summary>Gets the tenant the key is limited to, or <see langword="null"/>.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Gets the roles granted to the key.</summary>
    public IReadOnlyCollection<string> Roles { get; init; } = [];

    /// <summary>Gets the permissions granted to the key.</summary>
    public IReadOnlyCollection<string> Permissions { get; init; } = [];

    /// <summary>Gets when the key stops working, or <see langword="null"/> when it does not expire.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets when the key was revoked, or <see langword="null"/>. A revoked key never works again.</summary>
    public DateTimeOffset? RevokedAt { get; init; }
}
