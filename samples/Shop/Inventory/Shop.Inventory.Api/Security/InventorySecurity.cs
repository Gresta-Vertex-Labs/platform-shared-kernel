using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharedKernel.Execution.Tenancy;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.Mtls;
using SharedKernel.Security.Mtls.Validation;
using Shop.Inventory.Api.Stock;

namespace Shop.Inventory.Api.Security;

/// <summary>
/// The services allowed to call Inventory over mutual TLS, by the SHA-256 thumbprint of their client certificate
/// (<c>Inventory:Mtls:Clients:{thumbprint}</c> = client id). The kernel has already checked the chain against the
/// Shop's CA; this decides who the caller is and what it may do.
/// </summary>
public sealed class ShopServiceCertificateValidator(IConfiguration configuration)
    : IMtlsCertificateValidator
{
    public ValueTask<MtlsValidationResult> ValidateAsync(
        X509Certificate2 certificate,
        CancellationToken cancellationToken
    )
    {
        string thumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        string? clientId = configuration[$"Inventory:Mtls:Clients:{thumbprint}"];

        return ValueTask.FromResult(
            clientId is null
                ? MtlsValidationResult.Failure("UnknownClient")
                : MtlsValidationResult.Success(
                    clientId,
                    roles: ["shop-service"],
                    permissions: [InventoryPermissions.Reserve]
                )
        );
    }
}

/// <summary>
/// The tenant from the <c>X-Tenant-Id</c> header, but only for a caller authenticated by its certificate: a Shop
/// service acting for a tenant. The kernel's own Header strategy trusts the header from any caller, so a user without a
/// tenant claim could pick a tenant by sending it; here a user's tenant can only come from the signed claim.
/// </summary>
public sealed class ServiceHeaderTenantResolutionStrategy : ITenantResolutionStrategy
{
    public const string Name = "ServiceHeader";

    public string StrategyName => Name;

    public Task<TenantId?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        bool isService = context.User.Identities.Any(identity =>
            identity.IsAuthenticated
            && identity.AuthenticationType == MtlsAuthenticationDefaults.AuthenticationScheme
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

/// <summary>Where the AppHost put the Shop's development CA (<c>Inventory:Mtls:CaCertificatePath</c>).</summary>
public static class ShopCertificateAuthority
{
    public static X509Certificate2Collection Load(IConfiguration configuration)
    {
        string path =
            configuration["Inventory:Mtls:CaCertificatePath"]
            ?? throw new InvalidOperationException(
                "Inventory:Mtls:CaCertificatePath is not configured."
            );
        return [X509CertificateLoader.LoadCertificateFromFile(path)];
    }
}
