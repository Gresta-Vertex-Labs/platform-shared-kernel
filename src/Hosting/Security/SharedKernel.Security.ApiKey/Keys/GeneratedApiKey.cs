namespace SharedKernel.Security.ApiKey.Keys;

/// <summary>A newly generated API key.</summary>
/// <remarks>
/// Show <see cref="Key"/> to the client once and never store it. Store <see cref="KeyId"/> and <see cref="KeyHash"/>
/// in an <see cref="ApiKeyRecord"/>.
/// </remarks>
public sealed class GeneratedApiKey
{
    internal GeneratedApiKey(string key, string keyId, string keyHash)
    {
        Key = key;
        KeyId = keyId;
        KeyHash = keyHash;
    }

    /// <summary>Gets the full key the client sends. A secret.</summary>
    public string Key { get; }

    /// <summary>Gets the non-secret key id, the lookup key in the store.</summary>
    public string KeyId { get; }

    /// <summary>Gets the Base64url SHA-256 hash of <see cref="Key"/> to store.</summary>
    public string KeyHash { get; }

    /// <summary>Returns a description without the key.</summary>
    /// <returns>The key id.</returns>
    public override string ToString() => $"GeneratedApiKey {{ KeyId = {KeyId} }}";
}
