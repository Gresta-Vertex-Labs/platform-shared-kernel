using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage.Abstractions.Tests;

public sealed class TenantIsolationTests : IDisposable
{
    private readonly RecordingFileStorage _raw = new("documents");
    private readonly RecordingFileStorage _otherRaw = new("archive");
    private readonly ServiceProvider _provider;
    private readonly IFileStorage _tenantA;
    private readonly IFileStorage _tenantB;

    public TenantIsolationTests()
    {
        _provider = StoreRegistryTests.Build(("documents", true, _raw), ("archive", true, _otherRaw));
        ITenantFileStorage store = _provider.GetRequiredKeyedService<ITenantFileStorage>("documents");
        _tenantA = store.ForTenant("tenant-a");
        _tenantB = store.ForTenant("tenant-b");
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task Every_key_reaches_the_provider_under_the_tenant_prefix_and_returns_relative()
    {
        Result<FileReference> uploaded = await _tenantA.UploadAsync("reports/q3.pdf", new MemoryStream([1]));

        _raw.Keys.Should().Equal("tenants/tenant-a/reports/q3.pdf");
        uploaded.Value.Should().Be(new FileReference { Store = "documents", TenantId = "tenant-a", Key = "reports/q3.pdf", ETag = "\"e\"" });
    }

    [Fact]
    public async Task Downloads_and_properties_come_back_with_relative_keys()
    {
        Result<FileDownload> download = await _tenantA.DownloadAsync("a.txt");
        Result<FileProperties> properties = await _tenantA.GetPropertiesAsync("a.txt");

        await using FileDownload file = download.Value;
        file.Properties.Key.Should().Be("a.txt");
        file.Length.Should().Be(3);
        properties.Value.Key.Should().Be("a.txt");
        _raw.Keys.Should().Equal("tenants/tenant-a/a.txt", "tenants/tenant-a/a.txt");
    }

    [Fact]
    public async Task Errors_name_the_key_the_caller_passed_not_the_tenant_prefix()
    {
        _raw.FailWith = StorageErrors.NotFound("documents", "tenants/tenant-a/missing.txt");

        Result<FileProperties> result = await _tenantA.GetPropertiesAsync("missing.txt");

        result.Error.Code.Should().Be(StorageErrorCodes.NotFound);
        result.Error.Message.Should().Be("Object 'missing.txt' was not found in store 'documents'.");
    }

    [Fact]
    public async Task Invalid_keys_are_rejected_before_the_provider_is_called()
    {
        Result<FileReference> traversal = await _tenantA.UploadAsync("../tenant-b/x", new MemoryStream());
        Result<bool> absolute = await _tenantA.ExistsAsync("/tenants/tenant-b/x");

        traversal.Error.Code.Should().Be(StorageErrorCodes.InvalidKey);
        absolute.Error.Code.Should().Be(StorageErrorCodes.InvalidKey);
        _raw.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Listing_is_confined_to_the_tenant_and_strips_the_prefix()
    {
        _raw.StoredKeys.AddRange(["tenants/tenant-a/x/1", "tenants/tenant-a/x/2", "tenants/tenant-b/x/3"]);

        List<string> streamed = [];
        await foreach (FileListItem item in _tenantA.ListAsync("x/"))
        {
            streamed.Add(item.Key);
        }

        Result<FileListPage> page = await _tenantB.ListPageAsync(new FileListRequest { Prefix = "x/", Recursive = false });

        streamed.Should().Equal("x/1", "x/2");
        page.Value.Items.Select(i => i.Key).Should().Equal("x/3");
        page.Value.Folders.Should().Equal("x/sub/");
    }

    [Fact]
    public void An_invalid_listing_prefix_throws_a_storage_exception()
    {
        Action list = () => _tenantA.ListAsync("../");

        list.Should().Throw<StorageException>().Which.Error.Code.Should().Be(StorageErrorCodes.InvalidKey);
    }

    [Fact]
    public async Task Batch_deletes_are_deduplicated_validated_and_mapped_back()
    {
        Result<BatchDeleteResult> invalid = await _tenantA.DeleteManyAsync(["a", "../b"]);
        Result<BatchDeleteResult> empty = await _tenantA.DeleteManyAsync([]);
        _raw.Calls.Should().Be(0);

        Result<BatchDeleteResult> result = await _tenantA.DeleteManyAsync(["a", "b", "a"]);

        invalid.Error.Code.Should().Be(StorageErrorCodes.InvalidKey);
        empty.Value.IsComplete.Should().BeTrue();
        _raw.Keys.Should().Equal("tenants/tenant-a/a", "tenants/tenant-a/b");
        result.Value.Deleted.Should().Equal("a");
        result.Value.Failed.Should().ContainSingle().Which.Key.Should().Be("b");
        result.Value.Failed[0].Error.Message.Should().Contain("'b'").And.NotContain("tenants/");
    }

    [Fact]
    public async Task Copies_between_views_reach_the_provider_stores_with_both_prefixes()
    {
        IFileStorage archiveB = _provider.GetRequiredKeyedService<ITenantFileStorage>("archive").ForTenant("tenant-b");

        Result<FileReference> copied = await _tenantA.CopyToAsync("a.txt", archiveB, "copy.txt");

        _raw.LastCopyTarget!.Value.Destination.Should().BeSameAs(_otherRaw);
        _raw.LastCopyTarget!.Value.DestinationKey.Should().Be("tenants/tenant-b/copy.txt");
        _raw.Keys.Should().StartWith("tenants/tenant-a/a.txt");
        copied.Value.Should().Be(new FileReference { Store = "archive", TenantId = "tenant-b", Key = "copy.txt" });
    }

    [Fact]
    public async Task Multipart_uploads_keep_relative_keys_for_the_caller()
    {
        MultipartUpload upload = (await _tenantA.StartMultipartUploadAsync("big.bin")).Value;
        Result<FileReference> completed = await _tenantA.CompleteMultipartUploadAsync(upload, [new UploadedPart(1, "\"p1\"")]);
        Result<FileReference> duplicateParts = await _tenantA.CompleteMultipartUploadAsync(
            upload, [new UploadedPart(1, "\"p1\""), new UploadedPart(1, "\"p2\"")]);

        upload.Should().Be(new MultipartUpload("big.bin", "upload-1"));
        completed.Value.Key.Should().Be("big.bin");
        duplicateParts.Error.Code.Should().Be(StorageErrorCodes.InvalidRequest);
        _raw.Keys.Should().Equal("tenants/tenant-a/big.bin", "tenants/tenant-a/big.bin");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    public void Invalid_tenant_ids_are_refused(string tenantId)
    {
        Action view = () => _provider.GetRequiredKeyedService<ITenantFileStorage>("documents").ForTenant(tenantId);

        view.Should().Throw<ArgumentException>();
    }
}
