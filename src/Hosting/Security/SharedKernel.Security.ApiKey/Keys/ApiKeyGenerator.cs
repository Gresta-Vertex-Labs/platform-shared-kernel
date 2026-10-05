using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Security.ApiKey.Options;

namespace SharedKernel.Security.ApiKey.Keys;

/// <summary>Generates managed API keys.</summary>
/// <remarks>
/// A key looks like <c>acme_live_4f7Qm2Lx9TzR8bNc_…</c>: the configured prefix, a 16-character key id, a 32-character
/// secret from a cryptographic random source and a 6-character checksum. Every key is independent, so a client can
/// hold several at once and rotate without downtime: issue a new key, switch the client over, then revoke the old one.
/// </remarks>
public sealed class ApiKeyGenerator
{
    private readonly ISecureRandomGenerator _random;
    private readonly IOptions<ManagedApiKeyOptions> _options;

    /// <summary>Creates a generator.</summary>
    /// <param name="random">The random source.</param>
    /// <param name="options">The managed key options; <see cref="ManagedApiKeyOptions.Prefix"/> is required.</param>
    public ApiKeyGenerator(ISecureRandomGenerator random, IOptions<ManagedApiKeyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(options);
        _random = random;
        _options = options;
    }

    /// <summary>Generates a key with the configured prefix.</summary>
    /// <returns>The key, its id and the hash to store.</returns>
    /// <exception cref="InvalidOperationException">The configured prefix is invalid.</exception>
    public GeneratedApiKey Generate()
    {
        string prefix = _options.Value.Prefix;
        if (!ApiKeyFormat.IsValidPrefix(prefix))
        {
            throw new InvalidOperationException(
                "ManagedApiKeyOptions.Prefix must be 2-32 lowercase letters, digits and single underscores, starting with a letter.");
        }

        string keyId = _random.GetString(ApiKeyFormat.Alphabet, ApiKeyFormat.KeyIdLength);
        string secret = _random.GetString(ApiKeyFormat.Alphabet, ApiKeyFormat.SecretLength);
        string key = ApiKeyFormat.Compose(prefix, keyId, secret);
        return new GeneratedApiKey(key, keyId, ApiKeyFormat.Hash(key));
    }
}
