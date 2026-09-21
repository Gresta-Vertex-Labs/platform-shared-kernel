using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

/// <summary>Tenanted test DbContext with one encrypted-property aggregate, resolved entirely through DI (never hand-constructed) so <c>.WithEncryption()</c>'s real wiring is exercised.</summary>
public sealed class EncryptionTestDbContext : TenantedDbContext
{
    public DbSet<EncCustomer> Customers => Set<EncCustomer>();

    public EncryptionTestDbContext(
        DbContextOptions<EncryptionTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }
}
