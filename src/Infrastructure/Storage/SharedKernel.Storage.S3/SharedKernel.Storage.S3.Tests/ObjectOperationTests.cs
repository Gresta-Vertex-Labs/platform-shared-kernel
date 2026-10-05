using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.S3.Tests.Infrastructure;

namespace SharedKernel.Storage.S3.Tests;

[Collection(MinioCollection.Name)]
public sealed class ObjectOperationTests : IDisposable
{
    private static readonly TenantId Acme = new(Guid.NewGuid());

    private readonly ServiceProvider _host;
    private readonly IFileStorage _files;

    public ObjectOperationTests(MinioFixture minio)
    {
        _host = minio.CreateHost();
        _files = _host.GetRequiredKeyedService<IFileStorage>("files");
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task An_upload_round_trips_content_headers_and_metadata()
    {
        string key = TestData.UniqueKey("invoice.pdf");
        byte[] content = TestData.Bytes(4096);

        FileReference reference = (await _files.UploadAsync(key, new MemoryStream(content), new FileUploadOptions
        {
            ContentType = "application/pdf",
            CacheControl = "private, max-age=60",
            ContentDisposition = "attachment; filename=\"invoice.pdf\"",
            Metadata = new Dictionary<string, string> { ["Order-Id"] = "42" },
            Tags = new Dictionary<string, string> { ["classification"] = "internal" },
        })).Ok();

        reference.Store.Should().Be("files");
        reference.Key.Should().Be(key);
        reference.ETag.Should().NotBeNullOrEmpty();

        FileProperties properties = (await _files.GetPropertiesAsync(key)).Ok();
        properties.ContentLength.Should().Be(content.Length);
        properties.ContentType.Should().Be("application/pdf");
        properties.CacheControl.Should().Contain("private").And.Contain("max-age=60");
        properties.ContentDisposition.Should().Be("attachment; filename=\"invoice.pdf\"");
        properties.Metadata.Should().Contain("order-id", "42");
        properties.ETag.Should().Be(reference.ETag);
        properties.LastModified.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        (await TestData.ReadAllAsync(_files, key)).Should().Equal(content);
        (await _files.ExistsAsync(key)).Ok().Should().BeTrue();
    }

    [Fact]
    public async Task A_non_seekable_stream_larger_than_a_part_is_uploaded_in_parts()
    {
        string key = TestData.UniqueKey("export.csv");
        byte[] content = TestData.Bytes((12 * 1024 * 1024) + 123);

        Result<FileReference> uploaded = await _files.UploadAsync(key, new ForwardOnlyStream(content));

        uploaded.IsSuccess.Should().BeTrue(uploaded.IsFailure ? uploaded.Error.ToString() : null);
        (await TestData.ReadAllAsync(_files, key)).Should().Equal(content);
        (await _files.GetPropertiesAsync(key)).Ok().ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task A_range_read_returns_only_those_bytes_and_the_full_size()
    {
        string key = TestData.UniqueKey();
        byte[] content = TestData.Bytes(1000);
        await _files.UploadAsync(key, new MemoryStream(content));

        await using FileDownload middle = (await _files.DownloadAsync(key, new FileDownloadOptions { Range = new ByteRange(100, 199) })).Ok();
        using var buffer = new MemoryStream();
        await middle.Content.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal(content[100..200]);
        middle.Length.Should().Be(100);
        middle.Range.Should().Be(new ByteRange(100, 199));
        middle.Properties.ContentLength.Should().Be(1000);

        (await TestData.ReadAllAsync(_files, key, new FileDownloadOptions { Range = new ByteRange(990) })).Should().Equal(content[990..]);
        (await _files.DownloadAsync(key, new FileDownloadOptions { Range = new ByteRange(5000) })).Error.Code
            .Should().Be(StorageErrorCodes.InvalidRange);
    }

    [Fact]
    public async Task A_missing_object_is_not_found_and_does_not_exist()
    {
        string key = TestData.UniqueKey();

        (await _files.DownloadAsync(key)).Error.Code.Should().Be(StorageErrorCodes.NotFound);
        (await _files.GetPropertiesAsync(key)).Error.Code.Should().Be(StorageErrorCodes.NotFound);
        (await _files.ExistsAsync(key)).Ok().Should().BeFalse();
    }

    [Fact]
    public async Task A_create_only_upload_never_overwrites()
    {
        string key = TestData.UniqueKey();
        await _files.UploadAsync(key, new MemoryStream([1]), new FileUploadOptions { Condition = WriteCondition.IfNotExists });

        Result<FileReference> second = await _files.UploadAsync(
            key, new MemoryStream([2]), new FileUploadOptions { Condition = WriteCondition.IfNotExists });

        second.Error.Code.Should().Be(StorageErrorCodes.AlreadyExists);
        (await TestData.ReadAllAsync(_files, key)).Should().Equal(1);
    }

    [Fact]
    public async Task An_if_match_write_succeeds_only_against_the_current_version()
    {
        string key = TestData.UniqueKey();
        string first = (await _files.UploadAsync(key, new MemoryStream([1]))).Ok().ETag!;
        string second = (await _files.UploadAsync(key, new MemoryStream([2]), new FileUploadOptions { Condition = WriteCondition.IfMatch(first) })).Ok().ETag!;

        Result<FileReference> stale = await _files.UploadAsync(
            key, new MemoryStream([3]), new FileUploadOptions { Condition = WriteCondition.IfMatch(first) });
        Result<FileDownload> staleRead = await _files.DownloadAsync(key, new FileDownloadOptions { IfMatch = first });

        stale.Error.Code.Should().Be(StorageErrorCodes.PreconditionFailed);
        staleRead.Error.Code.Should().Be(StorageErrorCodes.PreconditionFailed);
        (await TestData.ReadAllAsync(_files, key, new FileDownloadOptions { IfMatch = second.Trim('"') })).Should().Equal(2);
    }

    [Fact]
    public async Task A_supplied_checksum_is_verified_by_the_provider()
    {
        byte[] content = TestData.Bytes(2048);
        string good = TestData.UniqueKey();
        string bad = TestData.UniqueKey();

        Result<FileReference> verified = await _files.UploadAsync(
            good, new MemoryStream(content), new FileUploadOptions { ChecksumSha256 = TestData.Sha256(content) });
        Result<FileReference> corrupted = await _files.UploadAsync(
            bad, new MemoryStream(content), new FileUploadOptions { ChecksumSha256 = TestData.Sha256([1, 2, 3]) });

        verified.IsSuccess.Should().BeTrue(verified.IsFailure ? verified.Error.ToString() : null);
        corrupted.Error.Code.Should().Be(StorageErrorCodes.ChecksumMismatch);
        (await _files.ExistsAsync(bad)).Ok().Should().BeFalse();
    }

    [Fact]
    public async Task A_checksum_on_a_forward_only_stream_needs_its_length()
    {
        byte[] content = TestData.Bytes(3000);
        string key = TestData.UniqueKey();

        Result<FileReference> withoutLength = await _files.UploadAsync(
            key, new ForwardOnlyStream(content), new FileUploadOptions { ChecksumSha256 = TestData.Sha256(content) });
        Result<FileReference> withLength = await _files.UploadAsync(
            key, new ForwardOnlyStream(content), new FileUploadOptions { ChecksumSha256 = TestData.Sha256(content), ContentLength = content.Length });
        Result<FileReference> wrongChecksum = await _files.UploadAsync(
            TestData.UniqueKey(), new ForwardOnlyStream(content), new FileUploadOptions { ChecksumSha256 = TestData.Sha256([1]), ContentLength = content.Length });

        withoutLength.Error.Code.Should().Be(StorageErrorCodes.InvalidRequest);
        withLength.IsSuccess.Should().BeTrue(withLength.IsFailure ? withLength.Error.ToString() : null);
        wrongChecksum.Error.Code.Should().Be(StorageErrorCodes.ChecksumMismatch);
        (await TestData.ReadAllAsync(_files, key)).Should().Equal(content);
    }

    [Fact]
    public async Task A_small_forward_only_stream_of_known_length_goes_in_one_request()
    {
        byte[] content = TestData.Bytes(1000);
        string key = TestData.UniqueKey();

        Result<FileReference> uploaded = await _files.UploadAsync(
            key, new ForwardOnlyStream(content), new FileUploadOptions { ContentLength = content.Length });

        uploaded.IsSuccess.Should().BeTrue(uploaded.IsFailure ? uploaded.Error.ToString() : null);
        (await TestData.ReadAllAsync(_files, key)).Should().Equal(content);
    }

    [Fact]
    public async Task Deleting_is_idempotent()
    {
        string key = TestData.UniqueKey();
        await _files.UploadAsync(key, new MemoryStream([1]));

        (await _files.DeleteAsync(key)).IsSuccess.Should().BeTrue();
        (await _files.DeleteAsync(key)).IsSuccess.Should().BeTrue();
        (await _files.ExistsAsync(key)).Ok().Should().BeFalse();
    }

    [Fact]
    public async Task A_batch_delete_reports_every_key_including_missing_ones()
    {
        string[] keys = [.. Enumerable.Range(0, 5).Select(i => TestData.UniqueKey($"{i}.bin"))];
        foreach (string key in keys[..3])
        {
            await _files.UploadAsync(key, new MemoryStream([1]));
        }

        BatchDeleteResult result = (await _files.DeleteManyAsync(keys)).Ok();

        result.IsComplete.Should().BeTrue();
        result.Deleted.Should().BeEquivalentTo(keys);
        foreach (string key in keys)
        {
            (await _files.ExistsAsync(key)).Ok().Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_copy_can_replace_metadata_while_keeping_the_other_headers()
    {
        string source = TestData.UniqueKey("a.txt");
        string destination = TestData.UniqueKey("b.txt");
        await _files.UploadAsync(source, new MemoryStream([1, 2]), new FileUploadOptions
        {
            ContentType = "text/plain",
            CacheControl = "no-cache",
            Metadata = new Dictionary<string, string> { ["v"] = "1" },
        });

        FileReference copy = (await _files.CopyAsync(source, destination, new FileCopyOptions
        {
            Metadata = new Dictionary<string, string> { ["v"] = "2" },
        })).Ok();

        FileProperties properties = (await _files.GetPropertiesAsync(destination)).Ok();
        copy.Key.Should().Be(destination);
        properties.Metadata.Should().Contain("v", "2");
        properties.ContentType.Should().Be("text/plain");
        properties.CacheControl.Should().Be("no-cache");
        (await TestData.ReadAllAsync(_files, destination)).Should().Equal(1, 2);
        (await _files.CopyAsync(TestData.UniqueKey(), destination)).Error.Code.Should().Be(StorageErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_create_only_copy_never_overwrites()
    {
        string source = TestData.UniqueKey();
        string destination = TestData.UniqueKey();
        await _files.UploadAsync(source, new MemoryStream([1]));
        await _files.UploadAsync(destination, new MemoryStream([2]));

        Result<FileReference> copy = await _files.CopyAsync(source, destination, new FileCopyOptions { Condition = WriteCondition.IfNotExists });

        copy.Error.Code.Should().Be(StorageErrorCodes.AlreadyExists);
        (await TestData.ReadAllAsync(_files, destination)).Should().Equal(2);
    }

    [Fact]
    public async Task Copies_between_stores_land_under_the_destination_store_and_tenant()
    {
        string source = TestData.UniqueKey("scan.pdf");
        await _files.UploadAsync(source, new MemoryStream([9, 9]), new FileUploadOptions { ContentType = "application/pdf" });
        IFileStorage archive = _host.GetRequiredKeyedService<IFileStorage>("archive");
        IFileStorage tenantDocs = _host.GetRequiredKeyedService<ITenantFileStorage>("docs").ForTenant(Acme);
        IFileStorage bucketB = _host.GetRequiredKeyedService<IFileStorage>("b-root");

        FileReference archived = (await _files.CopyToAsync(source, archive, "2026/scan.pdf")).Ok();
        FileReference filed = (await _files.CopyToAsync(source, tenantDocs, "inbox/scan.pdf")).Ok();

        archived.Should().BeEquivalentTo(new { Store = "archive", TenantId = (TenantId?)null, Key = "2026/scan.pdf" });
        filed.Should().BeEquivalentTo(new { Store = "docs", TenantId = (TenantId?)Acme, Key = "inbox/scan.pdf" });
        (await bucketB.GetPropertiesAsync("archive/2026/scan.pdf")).Ok().ContentType.Should().Be("application/pdf");
        (await TestData.ReadAllAsync(bucketB, $"tenants/{Acme}/inbox/scan.pdf")).Should().Equal(9, 9);
    }

    [Fact]
    public async Task A_store_prefix_is_applied_to_keys_and_hidden_from_results()
    {
        IFileStorage prefixed = _host.GetRequiredKeyedService<IFileStorage>("prefixed");
        string key = TestData.UniqueKey();

        FileReference reference = (await prefixed.UploadAsync(key, new MemoryStream([5]))).Ok();
        List<FileListItem> listed = [];
        await foreach (FileListItem item in prefixed.ListAsync(key[..33]))
        {
            listed.Add(item);
        }

        reference.Key.Should().Be(key);
        (await TestData.ReadAllAsync(_files, "pre/" + key)).Should().Equal(5);
        listed.Select(i => i.Key).Should().Equal(key);
    }
}
