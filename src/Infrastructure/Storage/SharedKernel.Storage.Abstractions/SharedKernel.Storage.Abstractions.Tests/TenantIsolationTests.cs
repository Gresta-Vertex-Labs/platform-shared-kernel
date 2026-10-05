using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage.Abstractions.Tests;

public sealed class TenantIsolationTests : IDisposable
{
    private static readonly TenantId TenantA = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
    private static readonly TenantId TenantB = new(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

    private readonly RecordingFileStorage _raw = new("documents");
    private readonly RecordingFileStorage _otherRaw = new("archive");
    private readonly ServiceProvider _provider;
    private readonly IFileStorage _tenantA;
    private readonly IFileStorage _tenantB;

    public TenantIsolationTests()
    {
        _provider = StoreRegistryTests.Build(("documents", true, _raw), ("archive", true, _otherRaw));
        ITenantFileStorage store = _provider.GetRequiredKeyedService<ITenantFileStorage>("documents");
        _tenantA = store.ForTenant(TenantA);
        _tenantB = store.ForTenant(TenantB);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task Every_key_reaches_the_provider_under_the_tenant_prefix_and_returns_relative()
    {
        Result<FileReference> uploaded = await _tenantA.UploadAsync("reports/q3.pdf", new MemoryStream([1]));

        _raw.Keys.Should().Equal($"tenants/{TenantA}/reports/q3.pdf");
        uploaded.Value.Should().Be(new FileReference { Store = "documents", TenantId = TenantA, Key = "reports/q3.pdf", ETag = "\"e\"" });
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
        _raw.Keys.Should().Equal($"tenants/{TenantA}/a.txt", $"tenants/{TenantA}/a.txt");
    }

    [Fact]
    public async Task Errors_name_the_key_the_caller_passed_not_the_tenant_prefix()
    {
        _raw.FailWith = StorageErrors.NotFound("documents", $"tenants/{TenantA}/missing.txt");

        Result<FileProperties> result = await _tenantA.GetPropertiesAsync("missing.txt");

        result.Error.Code.Should().Be(StorageErrorCodes.NotFound);
        result.Error.Message.Should().Be("Object 'missing.txt' was not found in store 'documents'.");
    }

    [Fact]
    public async Task Invalid_keys_are_rejected_before_the_provider_is_called()
    {
        Result<FileReference> traversal = await _tenantA.UploadAsync($"../{TenantB}/x", new MemoryStream());
        Result<bool> absolute = await _tenantA.ExistsAsync($"/tenants/{TenantB}/x");

        traversal.Error.Code.Should().Be(StorageErrorCodes.InvalidKey);
        absolute.Error.Code.Should().Be(StorageErrorCodes.InvalidKey);
        _raw.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Listing_is_confined_to_the_tenant_and_strips_the_prefix()
    {
        _raw.StoredKeys.AddRange([$"tenants/{TenantA}/x/1", $"tenants/{TenantA}/x/2", $"tenants/{TenantB}/x/3"]);

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
        _raw.Keys.Should().Equal($"tenants/{TenantA}/a", $"tenants/{TenantA}/b");
        result.Value.Deleted.Should().Equal("a");
        result.Value.Failed.Should().ContainSingle().Which.Key.Should().Be("b");
        result.Value.Failed[0].Error.Message.Should().Contain("'b'").And.NotContain("tenants/");
    }

    [Fact]
    public async Task Copies_between_views_reach_the_provider_stores_with_both_prefixes()
    {
        IFileStorage archiveB = _provider.GetRequiredKeyedService<ITenantFileStorage>("archive").ForTenant(TenantB);

        Result<FileReference> copied = await _tenantA.CopyToAsync("a.txt", archiveB, "copy.txt");

        _raw.LastCopyTarget!.Value.Destination.Should().BeSameAs(_otherRaw);
        _raw.LastCopyTarget!.Value.DestinationKey.Should().Be($"tenants/{TenantB}/copy.txt");
        _raw.Keys.Should().StartWith($"tenants/{TenantA}/a.txt");
        copied.Value.Should().Be(new FileReference { Store = "archive", TenantId = TenantB, Key = "copy.txt" });
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
        _raw.Keys.Should().Equal($"tenants/{TenantA}/big.bin", $"tenants/{TenantA}/big.bin");
    }

    [Fact]
    public void The_default_tenant_id_is_refused()
    {
        Action view = () => _provider.GetRequiredKeyedService<ITenantFileStorage>("documents").ForTenant(default);

        view.Should().Throw<ArgumentException>();
    }
}
