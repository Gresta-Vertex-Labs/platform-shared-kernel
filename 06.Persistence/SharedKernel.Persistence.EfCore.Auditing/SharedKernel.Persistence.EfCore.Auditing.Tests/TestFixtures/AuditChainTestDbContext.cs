using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.TestFixtures;

/// <summary>
/// A minimal, deliberately domain-logic-free "business" row used ONLY to prove
/// <c>EfAuditTrailWriter</c>'s transaction semantics (does the row exist after commit/rollback,
/// alongside/independent of an audit record) — never a real aggregate, no domain events, no audit
/// columns of its own.
/// </summary>
public sealed class AuditTestOrder
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    private AuditTestOrder() { } // ORM path

    public AuditTestOrder(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
}

internal sealed class AuditTestOrderConfig : IEntityTypeConfiguration<AuditTestOrder>
{
    public void Configure(EntityTypeBuilder<AuditTestOrder> builder)
    {
        builder.ToTable("audit_test_orders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
    }
}

/// <summary>Test <see cref="SharedKernelDbContext"/> hosting <see cref="AuditTestOrder"/> plus, when <c>.WithAuditTrail()</c> is opted into, the audit-record table.</summary>
public sealed class AuditChainTestDbContext : SharedKernelDbContext
{
    public DbSet<AuditTestOrder> Orders => Set<AuditTestOrder>();

    public AuditChainTestDbContext(
        DbContextOptions<AuditChainTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }
}
