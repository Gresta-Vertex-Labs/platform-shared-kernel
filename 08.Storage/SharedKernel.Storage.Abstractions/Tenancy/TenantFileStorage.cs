using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Storage;

/// <summary>The <see cref="ITenantFileStorage"/> over a provider's store; hands out prefixed tenant views.</summary>
internal sealed class TenantFileStorage : ITenantFileStorage
{
    internal const string TenantsFolder = "tenants/";

    private readonly IFileStorage _inner;

    public TenantFileStorage(IFileStorage inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public string StoreName => _inner.StoreName;

    public IFileStorage ForTenant(TenantId tenantId)
    {
        if (tenantId.IsDefault)
        {
            throw new ArgumentException("The tenant identifier must not be default(TenantId).", nameof(tenantId));
        }

        return new ScopedFileStorage(_inner, tenantId);
    }
}
