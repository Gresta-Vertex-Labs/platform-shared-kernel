using Microsoft.Extensions.Options;
using SharedKernel.Cryptography;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Validation;

namespace SharedKernel.Security.ApiKey.Keys;

internal sealed class ManagedApiKeyValidator(IApiKeyStore store, IClock clock, IOptions<ManagedApiKeyOptions> options) : IApiKeyValidator
{
    public async ValueTask<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presentedKey);

        if (!ApiKeyFormat.TryParse(presentedKey, out string? prefix, out string? keyId))
        {
            return ApiKeyValidationResult.Failure("Malformed");
        }

        if (!string.Equals(prefix, options.Value.Prefix, StringComparison.Ordinal))
        {
            return ApiKeyValidationResult.Failure("WrongPrefix", keyId);
        }

        ApiKeyRecord? record = await store.FindAsync(keyId, cancellationToken).ConfigureAwait(false);
        string presentedHash = ApiKeyFormat.Hash(presentedKey);

        // Compare even when the key id is unknown, so the response time does not reveal which ids exist.
        bool hashMatches = FixedTimeComparison.AreEqual(presentedHash, record?.KeyHash ?? string.Empty);
        if (record is null || !hashMatches || !string.Equals(record.KeyId, keyId, StringComparison.Ordinal))
        {
            return ApiKeyValidationResult.Failure("UnknownKey", keyId);
        }

        DateTimeOffset now = clock.UtcNow;
        if (record.RevokedAt is { } revokedAt && revokedAt <= now)
        {
            return ApiKeyValidationResult.Failure("Revoked", keyId);
        }

        if (record.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            return ApiKeyValidationResult.Failure("Expired", keyId);
        }

        // A record with an empty tenant id has no tenant; Success rejects Guid.Empty.
        Guid? tenantId = record.TenantId == Guid.Empty ? null : record.TenantId;
        return ApiKeyValidationResult.Success(record.ClientId, tenantId, record.Roles, record.Permissions, record.KeyId);
    }
}
