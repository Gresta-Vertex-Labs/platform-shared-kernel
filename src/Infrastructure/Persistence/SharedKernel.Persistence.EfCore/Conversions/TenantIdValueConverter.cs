using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Persistence.EfCore.Conversions;

/// <summary>
/// EF Core value converter mapping <see cref="TenantId"/> to and from its <c>uuid</c> column
/// (<see cref="Guid"/>). Registered by convention for every <see cref="TenantId"/> and
/// <see cref="Nullable{T}">TenantId?</see> property of a <c>SharedKernelDbContext</c>, so no entity configuration
/// names it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>To-provider direction:</strong> <see cref="TenantId.Value"/>. An unset
/// <c>default(TenantId)</c> writes <see cref="Guid.Empty"/>; the save pipeline stamps or rejects such a value
/// before it reaches the database.
/// </para>
/// <para>
/// <strong>From-provider direction:</strong> a stored <see cref="Guid.Empty"/> materializes as
/// <c>default(TenantId)</c> rather than throwing, so a row written outside the platform surfaces as "no tenant"
/// (which the tenant filter and write guard never match) instead of failing the whole query.
/// </para>
/// </remarks>
internal sealed class TenantIdValueConverter : ValueConverter<TenantId, Guid>
{
    /// <summary>Initialises a new instance of the converter.</summary>
    public TenantIdValueConverter()
        : base(
            tenantId => tenantId.Value,
            value => value == Guid.Empty ? default : new TenantId(value))
    {
    }
}
