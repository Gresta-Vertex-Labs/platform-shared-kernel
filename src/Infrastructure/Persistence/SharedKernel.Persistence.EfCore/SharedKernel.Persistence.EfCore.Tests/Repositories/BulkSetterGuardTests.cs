using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

/// <summary>
/// A8 regression: bulk-update setter targets are resolved through the whole member chain, including
/// complex-type members, and fail closed on anything unresolvable. The recorder replaces the EF1001
/// internal-API inspection of <c>UpdateSettersBuilder</c>.
/// </summary>
public sealed class BulkSetterGuardTests
{
    public sealed class GuardAddress
    {
        public string City { get; set; } = "";

        public string Zip { get; set; } = "";
    }

    public sealed class GuardContact
    {
        public string Email { get; set; } = "";

        public string Phone { get; set; } = "";

        public GuardAddress Address { get; set; } = new();
    }

    public sealed class GuardOwner
    {
        public int Id { get; set; }
    }

    public sealed class GuardEntity : IHasTenant, IHasCreatedAudit
    {
        public int Id { get; set; }

        public TenantId TenantId { get; set; }

        public string Name { get; set; } = "";

        public string Secret { get; set; } = "";

        public uint Version { get; set; }

        public string CreatedBy { get; set; } = "";

        public DateTimeOffset CreatedOn { get; set; }

        public GuardContact Contact { get; set; } = new();

        public GuardOwner? Owner { get; set; }
    }

    private sealed class GuardContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlite("DataSource=:memory:");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GuardEntity>(b =>
            {
                // A bare DbContext has none of the platform conventions, so TenantId needs its converter here.
                b.Property(e => e.TenantId).HasConversion(tenant => tenant.Value, value => new TenantId(value));
                b.Property(e => e.Secret).HasAnnotation(PersistenceModelAnnotationNames.Encrypt, "secret");
                b.Property(e => e.Version).IsConcurrencyToken();
                b.ComplexProperty(e => e.Contact, c =>
                {
                    c.Property(x => x.Email).HasAnnotation(PersistenceModelAnnotationNames.Encrypt, "email");
                    c.ComplexProperty(x => x.Address, a =>
                        a.Property(x => x.Zip).HasAnnotation(PersistenceModelAnnotationNames.Encrypt, "zip"));
                });
                b.HasOne(e => e.Owner).WithMany();
            });
        }
    }

    private static readonly IEntityType EntityType = CreateEntityType();

    private static IEntityType CreateEntityType()
    {
        using var context = new GuardContext();
        return context.Model.FindEntityType(typeof(GuardEntity))!;
    }

    private static Action Validate(Action<BulkUpdateSetters<GuardEntity>> configure) => () =>
    {
        var setters = new BulkUpdateSetters<GuardEntity>();
        configure(setters);
        BulkSpecificationGuard.ValidateSetters(setters, EntityType);
    };

    [Fact]
    public void EncryptedComplexMember_IsRejected()
    {
        // Before: the inspector took only the last member name ("Email"), FindProperty returned null and the
        // plaintext went through.
        Validate(s => s.SetProperty(e => e.Contact.Email, "plain@example.com"))
            .Should().Throw<UnsupportedSpecificationException>().WithMessage("*Contact.Email*encrypted*");
    }

    [Fact]
    public void EncryptedNestedComplexMember_IsRejected() =>
        Validate(s => s.SetProperty(e => e.Contact.Address.Zip, "12345"))
            .Should().Throw<UnsupportedSpecificationException>().WithMessage("*Contact.Address.Zip*encrypted*");

    [Fact]
    public void EncryptedRootMember_IsRejected() =>
        Validate(s => s.SetProperty(e => e.Secret, "x"))
            .Should().Throw<UnsupportedSpecificationException>().WithMessage("*encrypted*");

    [Fact]
    public void PlainComplexMembers_AreResolvedAndAllowed()
    {
        var setters = new BulkUpdateSetters<GuardEntity>();
        setters.SetProperty(e => e.Contact.Phone, "555").SetProperty(e => e.Contact.Address.City, "Ankara");

        var resolved = BulkSpecificationGuard.ValidateSetters(setters, EntityType);

        resolved.Select(p => p.Name).Should().Equal("Phone", "City");
        resolved.Should().OnlyContain(p => p.DeclaringType is IComplexType);
    }

    [Fact]
    public void EfPropertyTarget_FailsClosed() =>
        Validate(s => s.SetProperty(e => EF.Property<TenantId>(e, nameof(GuardEntity.TenantId)), default(TenantId)))
            .Should().Throw<UnsupportedSpecificationException>().WithMessage("*plain member path*");

    [Fact]
    public void NavigationTarget_FailsClosed() =>
        Validate(s => s.SetProperty(e => e.Owner!.Id, 1))
            .Should().Throw<UnsupportedSpecificationException>().WithMessage("*not a complex property*");

    [Fact]
    public void ProtectedColumns_AreRejected()
    {
        Validate(s => s.SetProperty(e => e.TenantId, new TenantId(Guid.NewGuid()))).Should().Throw<UnsupportedSpecificationException>();
        Validate(s => s.SetProperty(e => e.Version, 1u)).Should().Throw<UnsupportedSpecificationException>().WithMessage("*concurrency*");
        Validate(s => s.SetProperty(e => e.Id, 5)).Should().Throw<UnsupportedSpecificationException>().WithMessage("*primary key*");
        Validate(s => s.SetProperty(e => e.CreatedBy, "x")).Should().Throw<UnsupportedSpecificationException>();
    }

    [Fact]
    public void NoSetters_IsRejected() =>
        Validate(_ => { }).Should().Throw<UnsupportedSpecificationException>();

    [Fact]
    public void ValueExpressionSetter_IsRecorded() =>
        Validate(s => s.SetProperty(e => e.Name, e => e.Name + "!")).Should().NotThrow();
}
