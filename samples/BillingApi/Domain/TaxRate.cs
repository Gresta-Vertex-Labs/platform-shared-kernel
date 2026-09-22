using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Primitives.Clocks;

namespace BillingApi.Domain;

/// <summary>
/// Global reference data shared by every tenant. <see cref="TenantSharedAttribute"/> opts it out of the tenant
/// filter, the write guard and row-level security — without it the tenanted model refuses to build.
/// </summary>
[TenantShared]
public sealed class TaxRate : AggregateRoot<string>
{
    public TaxRate(string code, string description, decimal rate, IClock clock) : base(code, clock)
    {
        Description = description;
        Rate = rate;
    }

    private TaxRate() { } // EF Core materialization

    public string Description { get; private set; } = string.Empty;

    public decimal Rate { get; private set; }
}
