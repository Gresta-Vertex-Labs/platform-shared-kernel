using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Storage;

/// <summary>
/// Proves <see cref="InMemoryStorageBuilderExtensions"/> and <see cref="InMemoryStorage"/>: in-memory
/// stores registered through the real storage registry resolve, validate and isolate tenants exactly as
/// a provider's stores do.
/// </summary>
public sealed class InMemoryStorageRegistrationTests
{
    private static readonly byte[] Payload = "payload"u8.ToArray();

    [Fact]
    public async Task AddInMemoryStore_ResolvesAsKeyedUnkeyedAndThroughTheFactory()
    {
        using var provider = BuildProvider(b => b.AddInMemoryStore("invoices"));

        var keyed = provider.GetRequiredKeyedService<IFileStorage>("invoices");
        var unkeyed = provider.GetRequiredService<IFileStorage>();
        var factory = provider.GetRequiredService<IFileStorageFactory>();

        Assert.Same(keyed, unkeyed);
        Assert.Same(keyed, factory.GetStore("invoices"));
        Assert.Equal("invoices", keyed.StoreName);
        Assert.IsNotType<InMemoryFileStorage>(keyed);

        var upload = await keyed.UploadAsync("a.txt", new MemoryStream(Payload));
        Assert.True(upload.IsSuccess);
        Assert.True(provider.GetInMemoryStore("invoices").WasUploaded("a.txt"));
    }

    [Fact]
    public async Task AddInMemoryTenantStore_TenantViewCannotSeeAnotherTenantsObjects()
    {
        using var provider = BuildProvider(b => b.AddInMemoryTenantStore("documents"));
        var store = provider.GetRequiredKeyedService<ITenantFileStorage>("documents");
        var tenantA = store.ForTenant("tenant-a");
        var tenantB = store.ForTenant("tenant-b");

        var upload = await tenantA.UploadAsync("secret.txt", new MemoryStream(Payload));

        Assert.True(upload.IsSuccess);
        Assert.Equal("tenant-a", upload.Value.TenantId);
        Assert.Equal("secret.txt", upload.Value.Key);
        Assert.Equal([InMemoryFileStorage.TenantKey("tenant-a", "secret.txt")], provider.GetInMemoryStore("documents").Keys);

        Assert.False((await tenantB.ExistsAsync("secret.txt")).Value);
        Assert.Equal(StorageErrorCodes.NotFound, (await tenantB.DownloadAsync("secret.txt")).Error.Code);
        Assert.Equal(StorageErrorCodes.InvalidKey, (await tenantB.DownloadAsync("../tenant-a/secret.txt")).Error.Code);

        var listed = new List<string>();
        await foreach (var item in tenantB.ListAsync())
        {
            listed.Add(item.Key);
        }

        Assert.Empty(listed);
        Assert.True((await tenantB.DeleteAsync("secret.txt")).IsSuccess);
        Assert.True((await tenantA.ExistsAsync("secret.txt")).Value);

        var ownListing = new List<string>();
        await foreach (var item in tenantA.ListAsync())
        {
            ownListing.Add(item.Key);
        }

        Assert.Equal(["secret.txt"], ownListing);
    }

    [Fact]
    public async Task AddInMemoryTenantStore_FactoryOpensAReferenceInItsTenantView()
    {
        using var provider = BuildProvider(b => b.AddInMemoryTenantStore("documents"));
        var tenantA = provider.GetRequiredService<ITenantFileStorage>().ForTenant("tenant-a");
        var reference = (await tenantA.UploadAsync("a.txt", new MemoryStream(Payload))).Value;

        var opened = provider.GetRequiredService<IFileStorageFactory>().Open(reference);

        Assert.Equal("tenant-a", opened.TenantId);
        Assert.True((await opened.ExistsAsync(reference.Key)).Value);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IFileStorageFactory>().GetStore("documents"));
    }

    [Fact]
    public async Task CopyToAsync_BetweenRegisteredStores_LandsInTheDestinationTenant()
    {
        using var provider = BuildProvider(b => b.AddInMemoryStore("quarantine").AddInMemoryTenantStore("documents"));
        var quarantine = provider.GetRequiredKeyedService<IFileStorage>("quarantine");
        var documents = provider.GetRequiredKeyedService<ITenantFileStorage>("documents").ForTenant("tenant-a");
        await quarantine.UploadAsync("in.pdf", new MemoryStream(Payload));

        var copy = await quarantine.CopyToAsync("in.pdf", documents, "final.pdf");

        Assert.True(copy.IsSuccess);
        Assert.Equal("documents", copy.Value.Store);
        Assert.Equal("tenant-a", copy.Value.TenantId);
        Assert.Equal("final.pdf", copy.Value.Key);
        Assert.Equal(Payload, provider.GetInMemoryStore("documents").GetContent(InMemoryFileStorage.TenantKey("tenant-a", "final.pdf")));
    }

    [Fact]
    public async Task HealthProbe_ReportsTheStoresSimulatedAvailability()
    {
        using var provider = BuildProvider(b => b.AddInMemoryStore("invoices"));
        var probe = provider.GetRequiredService<IFileStorageHealthProbe>();

        var healthy = await probe.ProbeAsync("invoices");
        provider.GetInMemoryStore("invoices").SimulateUnavailable = true;
        var unhealthy = await probe.ProbeAsync("invoices");

        Assert.True(healthy.IsSuccess);
        Assert.Equal(StorageErrorCodes.Unavailable, unhealthy.Error.Code);
    }

    [Fact]
    public void AddInMemoryStore_ExistingInstance_IsTheRegisteredRawStore()
    {
        var store = new InMemoryFileStorage("reports", new InMemoryFileStorageOptions { MaxPresignExpiry = TimeSpan.FromMinutes(5) });
        using var provider = BuildProvider(b => b.AddInMemoryStore(store));

        Assert.Same(store, provider.GetInMemoryStore("reports"));
    }

    [Fact]
    public void AddInMemoryStore_DuplicateName_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelStorage().AddInMemoryStore("invoices");

        Assert.Throws<InvalidOperationException>(() => builder.AddInMemoryTenantStore("INVOICES"));
    }

    [Fact]
    public void AddInMemoryStore_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IStorageBuilder)null!).AddInMemoryStore("invoices"));

    [Fact]
    public async Task CreateFactory_OverExistingStores_ValidatesRequestsBeforeTheStore()
    {
        var reports = new InMemoryFileStorage("reports");
        var factory = InMemoryStorage.CreateFactory(reports);

        var invalid = await factory.GetStore("reports").UploadAsync("/absolute.csv", new MemoryStream(Payload));
        var valid = await factory.GetStore("reports").UploadAsync("export.csv", new MemoryStream(Payload));

        Assert.Equal(StorageErrorCodes.InvalidKey, invalid.Error.Code);
        Assert.True(valid.IsSuccess);
        Assert.Equal(["export.csv"], reports.UploadedKeys);
    }

    private static ServiceProvider BuildProvider(Action<IStorageBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddSharedKernelStorage());
        return services.BuildServiceProvider();
    }
}
