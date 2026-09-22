using BillingApi.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Primitives.Clocks;

namespace BillingApi.Infrastructure;

/// <summary>
/// Reference data, seeded after the startup migrations under the cross-replica migration lock. Seeders on a
/// tenanted context already run inside a cross-tenant scope; <see cref="TaxRate"/> is tenant-shared anyway.
/// </summary>
public sealed class TaxRateSeeder(IClock clock) : IDataSeeder<BillingDbContext>
{
    public async Task SeedAsync(BillingDbContext context, CancellationToken cancellationToken = default)
    {
        TaxRate[] rates =
        [
            new("STD", "Standard rate", 0.20m, clock),
            new("RED", "Reduced rate", 0.10m, clock),
            new("ZERO", "Zero rate", 0m, clock),
        ];

        var existing = await context.TaxRates.Select(t => t.Id).ToListAsync(cancellationToken);
        context.TaxRates.AddRange(rates.Where(r => !existing.Contains(r.Id)));
        await context.SaveChangesAsync(cancellationToken);
    }
}
