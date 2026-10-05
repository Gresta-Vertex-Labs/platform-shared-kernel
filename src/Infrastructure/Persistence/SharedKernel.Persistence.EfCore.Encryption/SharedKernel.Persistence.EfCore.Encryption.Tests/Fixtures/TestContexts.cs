using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;

// Entities are configured inside each context, never through IEntityTypeConfiguration classes: an assembly scan
// would apply one context's configuration to every other context of this test assembly.
public sealed class CustomerDbContext(DbContextOptions<CustomerDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        TestModel.ConfigureCustomer(modelBuilder);
        modelBuilder.Entity<Document>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Body).HasMaxLength(20).Encrypt("document.body");
        });
    }
}

public sealed class ZooDbContext(DbContextOptions<ZooDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<Animal> Animals => Set<Animal>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Animal>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasDiscriminator<string>("kind").HasValue<Dog>("dog").HasValue<Cat>("cat");
        });
        modelBuilder.Entity<Dog>().Property(x => x.ChipCode).HasColumnName("chip_code").Encrypt("animal.chip_code");
        modelBuilder.Entity<Cat>().Property(x => x.ChipCode).HasColumnName("chip_code").Encrypt("animal.chip_code");

        modelBuilder.Entity<Vehicle>(b =>
        {
            b.HasKey(x => x.Id);
            b.UseTptMappingStrategy().ToTable("vehicles");
            b.Property(x => x.Vin).Encrypt("vehicle.vin");
        });
        modelBuilder.Entity<Truck>(b =>
        {
            b.ToTable("trucks");
            b.Property(x => x.Permit).Encrypt("truck.permit").WithBlindIndex(BlindIndexNormalization.CaseFold);
        });
    }
}

/// <summary>Customer model without <c>UseFieldEncryption</c> wired: model building must fail.</summary>
public sealed class UnwiredDbContext(DbContextOptions<UnwiredDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        TestModel.ConfigureCustomer(modelBuilder);
    }
}

public sealed class DuplicatePurposeDbContext(DbContextOptions<DuplicatePurposeDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Document>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Body).Encrypt("shared.purpose");
        });
        modelBuilder.Entity<Vehicle>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Vin).Encrypt("shared.purpose");
        });
    }
}

public sealed class OwnerWithAccounts
{
    public Guid Id { get; set; }

    public List<BankAccount> Accounts { get; set; } = [];

    public Address Home { get; set; } = new();
}

public sealed class ComplexCollectionDbContext(DbContextOptions<ComplexCollectionDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OwnerWithAccounts>(b =>
        {
            b.HasKey(x => x.Id);
            b.Ignore(x => x.Home);
            b.ComplexCollection<BankAccount>(x => x.Accounts, a =>
            {
                a.ToJson();
                // No .Encrypt overload exists for a collection element; the annotation is what the convention sees.
                a.Property(x => x.Iban).HasAnnotation(Extensibility.PersistenceModelAnnotationNames.Encrypt, "owner.account.iban");
            });
        });
    }
}

public sealed class JsonComplexDbContext(DbContextOptions<JsonComplexDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OwnerWithAccounts>(b =>
        {
            b.HasKey(x => x.Id);
            b.Ignore(x => x.Accounts);
            b.ComplexProperty(x => x.Home, h =>
            {
                h.ToJson();
                h.ComplexProperty(x => x.Bank).Property(x => x.Iban).Encrypt("owner.home.iban");
            });
        });
    }
}

public sealed class StoreGeneratedKeyDbContext(DbContextOptions<StoreGeneratedKeyDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Document>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Body).Encrypt("generated.body");
        });
    }
}

public static class TestModel
{
    public static void ConfigureCustomer(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).Encrypt("customer.email")
                .WithBlindIndex(BlindIndexNormalization.Trim | BlindIndexNormalization.CaseFold);
            b.Property(x => x.Note).Encrypt("customer.note");
            b.ComplexProperty(x => x.Billing, a =>
                a.ComplexProperty(x => x.Bank, bank =>
                    bank.Property(x => x.Iban).Encrypt("customer.billing.iban")
                        .WithBlindIndex(BlindIndexNormalization.RemoveWhitespace, normalizer: IbanNormalizer.NormalizerName)));
        });
}

public sealed class Supplier
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Email { get; set; } = string.Empty;

    public Address Billing { get; set; } = new();
}

/// <summary>A context without query filters, which compiled models do not support.</summary>
public sealed class SupplierDbContext(DbContextOptions<SupplierDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Supplier>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).Encrypt("supplier.email").WithBlindIndex(BlindIndexNormalization.CaseFold);
            b.ComplexProperty(x => x.Billing, a =>
                a.ComplexProperty(x => x.Bank, bank =>
                    bank.Property(x => x.Iban).Encrypt("supplier.billing.iban")
                        .WithBlindIndex(BlindIndexNormalization.RemoveWhitespace, normalizer: IbanNormalizer.NormalizerName)));
        });
    }
}
