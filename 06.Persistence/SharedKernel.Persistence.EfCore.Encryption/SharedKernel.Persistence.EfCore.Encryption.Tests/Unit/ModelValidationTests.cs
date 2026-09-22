using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

/// <summary>Model-build and startup validation; no database is contacted.</summary>
public sealed class ModelValidationTests
{
    private const string NoDatabase = "Host=localhost;Port=1;Database=none;Username=x;Password=y";

    private static readonly IConfiguration NoDatabaseConfiguration =
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:customers"] = NoDatabase })
            .Build();

    private static Microsoft.EntityFrameworkCore.Metadata.IModel ModelOf<TContext>(bool wireEncryption = true)
        where TContext : SharedKernelDbContext
    {
        using var services = EncryptionHost.Build<TContext>(NoDatabase, wireEncryption: wireEncryption);
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TContext>().Model;
    }

    [Fact]
    public void NestedComplexEncryptedProperty_IsValidated_AndGetsItsOwnBlindIndexColumn()
    {
        var customer = ModelOf<CustomerDbContext>().FindEntityType(typeof(Customer))!;
        var iban = customer.FindComplexProperty(nameof(Customer.Billing))!.ComplexType
            .FindComplexProperty(nameof(Address.Bank))!.ComplexType.FindProperty(nameof(BankAccount.Iban))!;

        iban.FindAnnotation(Extensibility.PersistenceModelAnnotationNames.EncryptApplied)!.Value.Should().Be(true);
        var blindIndex = customer.FindProperty("Billing_Bank_IbanBlindIndex")!;
        blindIndex.GetMaxLength().Should().Be(80);
        customer.GetIndexes().Should().Contain(i => i.Properties.Single() == blindIndex);
    }

    [Fact]
    public void EncryptedPropertyWithoutUseFieldEncryption_FailsModelBuilding()
    {
        var act = () => ModelOf<UnwiredDbContext>(wireEncryption: false);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Customer.Email*UseFieldEncryption*");
    }

    [Fact]
    public void SamePurposeOnTwoTables_FailsModelBuilding()
    {
        // Finding A14: purposes were unique per entity only, so ciphertext could move between tables.
        var act = () => ModelOf<DuplicatePurposeDbContext>();
        act.Should().Throw<InvalidOperationException>().WithMessage("*shared.purpose*already uses that purpose*");
    }

    [Fact]
    public void SamePurposeOnTphSiblingsSharingOneColumn_IsAllowed()
    {
        var model = ModelOf<ZooDbContext>();
        model.FindEntityType(typeof(Dog))!.FindProperty(nameof(Dog.ChipCode))!.GetColumnName().Should().Be("chip_code");
        model.FindEntityType(typeof(Cat))!.FindProperty(nameof(Cat.ChipCode))!.GetColumnName().Should().Be("chip_code");
    }

    [Fact]
    public void EncryptedPropertyInAComplexCollection_FailsModelBuilding()
    {
        var act = () => ModelOf<ComplexCollectionDbContext>();
        act.Should().Throw<InvalidOperationException>().WithMessage("*complex collection*");
    }

    [Fact]
    public void EncryptedPropertyInAJsonComplexType_FailsModelBuilding()
    {
        var act = () => ModelOf<JsonComplexDbContext>();
        act.Should().Throw<InvalidOperationException>().WithMessage("*JSON*");
    }

    [Fact]
    public void MaintenanceTargets_AreOnePerPhysicalColumn_WithTheTenantJoinedForTpt()
    {
        using var services = EncryptionHost.Build<ZooDbContext>(NoDatabase);
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ZooDbContext>();
        var targets = MaintenanceTarget.Build(
            context.Model,
            Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Storage.ISqlGenerationHelper>(context));

        targets.Select(t => t.Key).Should().BeEquivalentTo("public.animals.chip_code", "public.vehicles.vin", "public.trucks.permit");
        targets.Single(t => t.Key == "public.trucks.permit").TenantSource!.Value.Join.Should().Contain("JOIN vehicles AS r");
        targets.Single(t => t.Key == "public.vehicles.vin").TenantSource!.Value.Join.Should().BeEmpty();
    }

    [Fact]
    public void MissingKeySource_FailsOptionsValidation_AtStartup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelPostgres<CustomerDbContext>(NoDatabaseConfiguration, "customers", p => p
            .UseFieldEncryption());
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<EncryptionOptions>>().Value;
        act.Should().Throw<OptionsValidationException>().WithMessage("*no key source*");
    }

    [Fact]
    public void InvalidConfiguredKeys_FailOptionsValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelPostgres<CustomerDbContext>(NoDatabaseConfiguration, "customers", p => p
            .UseFieldEncryption(k => k.FromConfiguration().Configure(o =>
            {
                o.Keys.CurrentKeyId = "k1";
                o.Keys.Keys["k1"] = Convert.ToBase64String(new byte[16]);
                o.BlindIndexKeys.CurrentVersion = "V1!";
                o.BlindIndexKeys.Keys["V1!"] = Convert.ToBase64String(new byte[32]);
            })));
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<EncryptionOptions>>().Value;
        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(f => f.Contains("exactly 32 bytes")).And.Contain(f => f.Contains("lowercase"));
    }

    [Fact]
    public void FromConfiguration_WithValidKeys_Resolves_AndRegistersNoGlobalKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelPostgres<CustomerDbContext>(NoDatabaseConfiguration, "customers", p => p
            .UseFieldEncryption(k => k.FromConfiguration().Configure(o =>
            {
                o.Keys.CurrentKeyId = "k1";
                o.Keys.Keys["k1"] = Convert.ToBase64String(TestKeys.V1);
            })));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<EncryptionOptions>>().Value.Keys.CurrentKeyId.Should().Be("k1");
        provider.GetService<IEncryptionKeyProvider>().Should().BeNull();
        provider.GetService<ISynchronousEncryptionKeyProvider>().Should().BeNull();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITenantEncryptionKeyManager>().Should().NotBeNull();
    }

    [Fact]
    public void TwoDifferentKeySources_AreRejected()
    {
        var services = new ServiceCollection();
        var act = () => services.AddSharedKernelPostgres<CustomerDbContext>(NoDatabaseConfiguration, "customers", p => p
            .UseFieldEncryption(k => k.FromConfiguration().UseKeyProvider(_ => TestKeys.Provider())));
        act.Should().Throw<InvalidOperationException>().WithMessage("*already uses key source*");
    }

    [Fact]
    public void EncryptionInterceptor_IsTheLastSaveChangesInterceptor_AfterThePlatformAndUserInterceptors()
    {
        // The user interceptor is registered AFTER UseFieldEncryption on purpose: the core applies option
        // extensions (encryption) after its own and every user interceptor, whatever the registration order, so
        // values a SavingChanges interceptor writes are encrypted too.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEncryptionKeyProvider>(TestKeys.Provider());
        services.AddSharedKernelPostgres<CustomerDbContext>(NoDatabaseConfiguration, "customers", p => p
            .UseFieldEncryption()
            .AddInterceptor<StampingSaveInterceptor>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();

        var saveInterceptors = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!.Interceptors!
            .OfType<ISaveChangesInterceptor>()
            .ToList();

        saveInterceptors.Should().HaveCountGreaterThan(2);
        saveInterceptors.Should().ContainSingle(i => i is StampingSaveInterceptor);
        saveInterceptors[^1].GetType().Name.Should().Be("EncryptionInterceptor");
    }

    private sealed class StampingSaveInterceptor : SaveChangesInterceptor;
}
