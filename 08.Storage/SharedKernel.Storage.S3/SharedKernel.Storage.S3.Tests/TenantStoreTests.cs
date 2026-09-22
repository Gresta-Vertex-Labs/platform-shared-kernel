using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage.S3.Tests.Infrastructure;

namespace SharedKernel.Storage.S3.Tests;

[Collection(MinioCollection.Name)]
public sealed class TenantStoreTests : IDisposable
{
    private readonly ServiceProvider _host;
    private readonly ITenantFileStorage _docs;

    public TenantStoreTests(MinioFixture minio)
    {
        _host = minio.CreateHost();
        _docs = _host.GetRequiredKeyedService<ITenantFileStorage>("docs");
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Tenants_cannot_read_list_or_delete_each_others_objects()
    {
        string tenantA = $"a{Guid.NewGuid():N}";
        string tenantB = $"b{Guid.NewGuid():N}";
        IFileStorage a = _docs.ForTenant(tenantA);
        IFileStorage b = _docs.ForTenant(tenantB);

        FileReference reference = (await a.UploadAsync("contracts/nda.pdf", new MemoryStream([1]))).Ok();

        reference.Should().BeEquivalentTo(new { Store = "docs", TenantId = tenantA, Key = "contracts/nda.pdf" });
        (await b.ExistsAsync("contracts/nda.pdf")).Ok().Should().BeFalse();
        (await b.DownloadAsync("contracts/nda.pdf")).Error.Code.Should().Be(StorageErrorCodes.NotFound);
        (await b.DeleteAsync("contracts/nda.pdf")).IsSuccess.Should().BeTrue();
        (await a.ExistsAsync("contracts/nda.pdf")).Ok().Should().BeTrue();

        List<string> seenByB = [];
        await foreach (FileListItem item in b.ListAsync())
        {
            seenByB.Add(item.Key);
        }

        seenByB.Should().BeEmpty();
        (await b.CopyToAsync("contracts/nda.pdf", b, "stolen.pdf")).Error.Code.Should().Be(StorageErrorCodes.NotFound);
    }

    [Fact]
    public async Task Tenant_objects_live_under_the_tenant_prefix_in_the_bucket()
    {
        string tenant = $"t{Guid.NewGuid():N}";
        await _docs.ForTenant(tenant).UploadAsync("x.txt", new MemoryStream([7]));

        IFileStorage bucket = _host.GetRequiredKeyedService<IFileStorage>("b-root");

        (await TestData.ReadAllAsync(bucket, $"tenants/{tenant}/x.txt")).Should().Equal(7);
    }

    [Fact]
    public async Task A_reference_reopens_the_same_tenant_view()
    {
        string tenant = $"t{Guid.NewGuid():N}";
        FileReference reference = (await _docs.ForTenant(tenant).UploadAsync("y.txt", new MemoryStream([8]))).Ok();

        IFileStorage reopened = _host.GetRequiredService<IFileStorageFactory>().Open(reference);

        reopened.TenantId.Should().Be(tenant);
        (await TestData.ReadAllAsync(reopened, reference.Key)).Should().Equal(8);
    }

    [Fact]
    public async Task Errors_from_a_tenant_view_do_not_reveal_the_tenant_prefix()
    {
        string tenant = $"t{Guid.NewGuid():N}";

        var result = await _docs.ForTenant(tenant).GetPropertiesAsync("missing.txt");

        result.Error.Message.Should().Contain("'missing.txt'").And.NotContain("tenants/");
    }
}
