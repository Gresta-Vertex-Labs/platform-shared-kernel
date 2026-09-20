using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.TestFixtures;

/// <summary>
/// A minimal, domain-logic-free "business" row used only to prove that
/// <c>SharedKernel.Application.Behaviors.Transaction.TransactionBehavior{TRequest,TResponse}</c>'s
/// transactional capability genuinely makes a business write and a <c>Succeeded</c>-outcome audit
/// record commit — or roll back — atomically together, through the real, unmodified production
/// MediatR pipeline (never by hand-resolving <c>ITransactionalUnitOfWork</c> directly).
/// </summary>
public sealed class WiringTestOrder
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    private WiringTestOrder() { } // ORM path

    public WiringTestOrder(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
}

internal sealed class WiringTestOrderConfig : IEntityTypeConfiguration<WiringTestOrder>
{
    public void Configure(EntityTypeBuilder<WiringTestOrder> builder)
    {
        builder.ToTable("wiring_test_orders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Deliberately short: the "business write fails after the audit record is already staged"
        // test forces a genuine Postgres-level failure (22001, value too long for type character
        // varying(10)) at SaveChangesAsync time, INSIDE the still-open transaction TransactionBehavior
        // opened before the inner pipeline ran.
        builder.Property(x => x.Name).HasMaxLength(10).IsRequired();
    }
}

/// <summary>
/// Test <see cref="SharedKernelDbContext"/> hosting <see cref="WiringTestOrder"/> plus, once
/// <c>.WithAuditTrail()</c> is opted into, the audit-record table.
/// </summary>
public sealed class AuditWiringTestDbContext : SharedKernelDbContext
{
    public DbSet<WiringTestOrder> Orders => Set<WiringTestOrder>();

    public AuditWiringTestDbContext(
        DbContextOptions<AuditWiringTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }
}
