using SharedKernel.Execution.Tenancy;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.ApiKey;
using SharedKernel.Security.ApiKey.Keys;

namespace Shop.Billing.Api.Security;

/// <summary>
/// Billing's API-key clients (<c>Billing:ApiKeys:{keyId}</c>): who they are and what they may do comes from
/// appsettings, the key hash from Key Vault (secret <c>Billing--ApiKeys--{keyId}--Hash</c>, loaded as configuration by
/// 13.ServiceDefaults.Configuration.KeyVault). A key without a tenant is a Shop service acting for many tenants.
/// </summary>
public sealed class ConfigurationApiKeyStore(IConfiguration configuration) : IApiKeyStore
{
    public const string Section = "Billing:ApiKeys";
    public const string HashKey = "Hash";
    public const string ClientIdKey = "ClientId";
    public const string TenantIdKey = "TenantId";
    public const string PermissionsKey = "Permissions";

    public ValueTask<ApiKeyRecord?> FindAsync(string keyId, CancellationToken cancellationToken)
    {
        var client = configuration.GetSection($"{Section}:{keyId}");
        string? hash = client[HashKey];
        string? clientId = client[ClientIdKey];
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(clientId))
        {
            return ValueTask.FromResult<ApiKeyRecord?>(null);
        }

        return ValueTask.FromResult<ApiKeyRecord?>(
            new ApiKeyRecord(keyId, hash, clientId)
            {
                TenantId = TenantId.TryParse(client[TenantIdKey], out var tenant) ? tenant : null,
                Permissions = client.GetSection(PermissionsKey).Get<string[]>() ?? [],
            }
        );
    }
}

/// <summary>
/// The tenant from the <c>X-Tenant-Id</c> header, but only for a caller authenticated by an API key: a Shop service
/// acting for a tenant (the header is set by 11.Communication's propagation). A user's tenant comes only from the signed
/// claim, and a key bound to a tenant is resolved by the Claim strategy first.
/// </summary>
public sealed class ServiceHeaderTenantResolutionStrategy : ITenantResolutionStrategy
{
    public const string Name = "ServiceHeader";

    public string StrategyName => Name;

    public Task<TenantId?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        bool isService = context.User.Identities.Any(identity =>
            identity.IsAuthenticated
            && identity.AuthenticationType == ApiKeyAuthenticationDefaults.AuthenticationScheme
        );
        if (
            !isService
            || !context.Request.Headers.TryGetValue(WellKnownHeaders.TenantId, out var values)
        )
        {
            return Task.FromResult<TenantId?>(null);
        }

        return Task.FromResult(
            TenantId.TryParse(values.ToString(), out var tenant) ? tenant : (TenantId?)null
        );
    }
}
