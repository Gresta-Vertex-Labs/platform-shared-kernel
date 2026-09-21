using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

/// <summary>
/// The "after a deploy" counterpart of <see cref="EncryptionTestDbContext"/>, used only by
/// <c>EncryptionRotationCheckpointIntegrationTests</c> to prove a rotation checkpoint survives the model gaining a
/// new encrypted entity type between the call that produced it and the call that resumes from it (H11 coverage).
/// </summary>
/// <remarks>
/// Exposes the SAME <see cref="Customers"/> DbSet (same property name, so the same physical table
/// <c>customers</c>) as <see cref="EncryptionTestDbContext"/>, plus a new <see cref="Orders"/> DbSet unknown to
/// that context. <see cref="EncAaaOrder"/> is configured inline in <see cref="OnModelCreating"/> rather than
/// through a discoverable <c>IEntityTypeConfiguration&lt;EncAaaOrder&gt;</c> class — <c>SharedKernelDbContext
/// .OnModelCreating</c>'s <c>ApplyConfigurationsFromAssembly</c> scan is assembly-wide, not context-scoped
/// (see <c>ShadowKeyGuardFixtures.cs</c>'s identical concern), so a discoverable configuration for
/// <see cref="EncAaaOrder"/> would silently bleed into <see cref="EncryptionTestDbContext"/> too — defeating the
/// entire point of this fixture, which is that the ORIGINAL context genuinely has no idea this entity type exists.
/// </remarks>
public sealed class EncryptionTestDbContextV2 : TenantedDbContext
{
    public DbSet<EncCustomer> Customers => Set<EncCustomer>();

    public DbSet<EncAaaOrder> Orders => Set<EncAaaOrder>();

    public EncryptionTestDbContextV2(
        DbContextOptions<EncryptionTestDbContextV2> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // picks up EncCustomerConfig via the assembly-wide scan, as EncryptionTestDbContext does today.

        modelBuilder.Entity<EncAaaOrder>(builder => builder.ConfigureEncAaaOrder());
    }
}
