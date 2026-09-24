using SharedKernel.Application;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Tenants;

/// <summary>What <see cref="EraseTenant"/> did.</summary>
public sealed record TenantErased(Guid TenantId, bool IsComplete, long BlindIndexValuesCleared);

/// <summary>
/// GDPR/KVKK erasure of a whole tenant: destroys its data key, so every value encrypted under it becomes unreadable,
/// by anyone, forever. Back office only.
/// </summary>
[RequirePermission(Permissions.Admin)]
public sealed record EraseTenant(Guid TenantId) : ICommand<TenantErased>;

public sealed class EraseTenantHandler(ICrossTenantScope crossTenant, ITenantEncryptionKeyManager keys)
    : ICommandHandler<EraseTenant, TenantErased>
{
    public async Task<Result<TenantErased>> Handle(EraseTenant command, CancellationToken cancellationToken)
    {
        // Shredding reaches the tenant's rows in every table, so it runs in a cross-tenant scope whose reason is
        // logged with the caller, on its own maintenance connection and transaction.
        using (crossTenant.Enter($"tenant erasure request for {command.TenantId}"))
        {
            var result = await keys.ShredTenantAsync(command.TenantId, cancellationToken: cancellationToken);
            return Result<TenantErased>.Success(new TenantErased(result.TenantId, result.IsComplete, result.BlindIndexValuesCleared));
        }
    }
}
