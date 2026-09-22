using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage.Abstractions.Tests;

public sealed class StoreRegistryTests
{
    [Fact]
    public void A_single_shared_store_resolves_keyed_unkeyed_and_by_name()
    {
        using ServiceProvider provider = Build(("invoices", false));

        IFileStorage keyed = provider.GetRequiredKeyedService<IFileStorage>("invoices");
        IFileStorage unkeyed = provider.GetRequiredService<IFileStorage>();
        IFileStorage byName = provider.GetRequiredService<IFileStorageFactory>().GetStore("INVOICES");

        keyed.Should().BeSameAs(unkeyed).And.BeSameAs(byName);
        keyed.StoreName.Should().Be("invoices");
        keyed.TenantId.Should().BeNull();
    }

    [Fact]
    public void The_provider_store_is_never_handed_out_directly()
    {
        var raw = new RecordingFileStorage("invoices");
        using ServiceProvider provider = Build(("invoices", false, raw));

        provider.GetRequiredService<IFileStorage>().Should().NotBeSameAs(raw);
    }

    [Fact]
    public void An_unkeyed_store_is_ambiguous_when_several_are_registered()
    {
        using ServiceProvider provider = Build(("invoices", false), ("exports", false));

        Action resolve = () => provider.GetRequiredService<IFileStorage>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*invoices, exports*FromKeyedServices*");
    }

    [Fact]
    public void An_unkeyed_store_explains_that_none_is_registered()
    {
        using ServiceProvider provider = Build(("documents", true));

        Action resolve = () => provider.GetRequiredService<IFileStorage>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*No shared storage store*");
    }

    [Fact]
    public void A_tenant_store_is_only_reachable_through_tenant_views()
    {
        using ServiceProvider provider = Build(("documents", true));
        IFileStorageFactory factory = provider.GetRequiredService<IFileStorageFactory>();

        factory.IsTenantScoped("documents").Should().BeTrue();
        Action asShared = () => factory.GetStore("documents");
        asShared.Should().Throw<InvalidOperationException>().WithMessage("*tenant-scoped*ForTenant*");
        provider.GetKeyedService<IFileStorage>("documents").Should().BeNull();

        ITenantFileStorage tenantStore = provider.GetRequiredKeyedService<ITenantFileStorage>("documents");
        tenantStore.Should().BeSameAs(provider.GetRequiredService<ITenantFileStorage>());
        tenantStore.ForTenant("t1").TenantId.Should().Be("t1");
    }

    [Fact]
    public void A_shared_store_cannot_be_resolved_as_a_tenant_store()
    {
        using ServiceProvider provider = Build(("invoices", false));

        Action asTenant = () => provider.GetRequiredService<IFileStorageFactory>().GetTenantStore("invoices");

        asTenant.Should().Throw<InvalidOperationException>().WithMessage("*shared by all tenants*");
    }

    [Fact]
    public void Unknown_stores_name_the_registered_ones()
    {
        using ServiceProvider provider = Build(("invoices", false));

        Action resolve = () => provider.GetRequiredService<IFileStorageFactory>().GetStore("invoice");

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*'invoice'*Registered stores: invoices*");
    }

    [Fact]
    public void Duplicate_store_names_fail_at_registration_ignoring_case()
    {
        IStorageBuilder builder = new ServiceCollection().AddSharedKernelStorage();
        builder.AddStore(Registration("invoices", false, new RecordingFileStorage("invoices")));

        Action duplicate = () => builder.AddStore(Registration("Invoices", false, new RecordingFileStorage("Invoices")));

        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    [Fact]
    public void Invalid_store_names_fail_at_registration()
    {
        Action create = () => Registration("bad name", false, new RecordingFileStorage("bad name"));

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_provider_factory_returning_the_wrong_store_fails_on_first_use()
    {
        using ServiceProvider provider = Build(("invoices", false, new RecordingFileStorage("other")));

        Action resolve = () => provider.GetRequiredService<IFileStorage>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*returned a store named 'other'*");
    }

    [Fact]
    public void Open_resolves_a_reference_to_its_store_and_tenant()
    {
        using ServiceProvider provider = Build(("invoices", false), ("documents", true));
        IFileStorageFactory factory = provider.GetRequiredService<IFileStorageFactory>();

        factory.Open(new FileReference { Store = "invoices", Key = "a" }).TenantId.Should().BeNull();
        factory.Open(new FileReference { Store = "documents", TenantId = "t1", Key = "a" }).TenantId.Should().Be("t1");

        Action missingTenant = () => factory.Open(new FileReference { Store = "documents", Key = "a" });
        Action unexpectedTenant = () => factory.Open(new FileReference { Store = "invoices", TenantId = "t1", Key = "a" });
        Action invalidTenant = () => factory.Open(new FileReference { Store = "documents", TenantId = "../t2", Key = "a" });
        missingTenant.Should().Throw<InvalidOperationException>();
        unexpectedTenant.Should().Throw<InvalidOperationException>();
        invalidTenant.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_health_probe_runs_the_store_probe()
    {
        IStorageBuilder builder = new ServiceCollection().AddSharedKernelStorage();
        builder.AddStore(new FileStoreRegistration(
            "invoices",
            tenantScoped: false,
            _ => new RecordingFileStorage("invoices"),
            (_, _) => Task.FromResult(Result.Failure(StorageErrors.Unavailable("invoices", "probe")))));
        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        Result result = await provider.GetRequiredService<IFileStorageHealthProbe>().ProbeAsync("invoices");

        result.Error.Code.Should().Be(StorageErrorCodes.Unavailable);
    }

    internal static ServiceProvider Build(params (string Name, bool Tenant)[] stores) =>
        Build(stores.Select(s => (s.Name, s.Tenant, new RecordingFileStorage(s.Name))).ToArray());

    internal static ServiceProvider Build(params (string Name, bool Tenant, RecordingFileStorage Store)[] stores)
    {
        IStorageBuilder builder = new ServiceCollection().AddSharedKernelStorage();
        foreach ((string name, bool tenant, RecordingFileStorage store) in stores)
        {
            builder.AddStore(Registration(name, tenant, store));
        }

        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static FileStoreRegistration Registration(string name, bool tenant, RecordingFileStorage store) =>
        new(name, tenant, _ => store, (_, _) => Task.FromResult(Result.Success()));
}
