using System.Security.Cryptography;
using System.Text;
using SharedKernel.Primitives.Errors;
using SharedKernel.Storage;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Storage;
using Xunit;

using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Testing.SelfTests.Storage;

/// <summary>
/// Proves <see cref="InMemoryFileStorage"/> against <c>IFileStorage</c>'s documented contract, used
/// directly as a raw provider store (the registry in front of it is proven in
/// <see cref="InMemoryStorageRegistrationTests"/>).
/// </summary>
public sealed class InMemoryFileStorageTests
{
    private static readonly TenantId TenantA = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("hello world");

    [Fact]
    public async Task RoundTrip_UploadDownloadPropertiesExistsCopyDelete_ThenNotFound()
    {
        var storage = new InMemoryFileStorage("docs");

        var upload = await storage.UploadAsync(
            "a.txt",
            new MemoryStream(Payload),
            new FileUploadOptions
            {
                ContentType = "text/plain",
                CacheControl = "no-cache",
                Metadata = new Dictionary<string, string> { ["Origin"] = "test" },
                Tier = StorageTier.InfrequentAccess,
            });

        Assert.True(upload.IsSuccess);
        Assert.Equal("docs", upload.Value.Store);
        Assert.Null(upload.Value.TenantId);
        Assert.Equal("a.txt", upload.Value.Key);
        Assert.StartsWith("\"", upload.Value.ETag, StringComparison.Ordinal);
        Assert.EndsWith("\"", upload.Value.ETag, StringComparison.Ordinal);
        Assert.True(storage.WasUploaded("a.txt"));

        var download = await storage.DownloadAsync("a.txt");
        Assert.True(download.IsSuccess);
        await using (download.Value)
        {
            Assert.Equal(Payload.Length, download.Value.Length);
            Assert.Equal("text/plain", download.Value.Properties.ContentType);
            Assert.Equal("no-cache", download.Value.Properties.CacheControl);
            Assert.Equal(StorageTier.InfrequentAccess, download.Value.Properties.Tier);
            Assert.Equal("test", download.Value.Properties.Metadata["origin"]);
            Assert.Equal(upload.Value.ETag, download.Value.Properties.ETag);
            using var reader = new StreamReader(download.Value.Content);
            Assert.Equal("hello world", await reader.ReadToEndAsync());
        }

        var properties = await storage.GetPropertiesAsync("a.txt");
        Assert.True(properties.IsSuccess);
        Assert.Equal(Payload.Length, properties.Value.ContentLength);
        Assert.Equal(Convert.ToBase64String(SHA256.HashData(Payload)), properties.Value.ChecksumSha256);

        Assert.True((await storage.ExistsAsync("a.txt")).Value);

        var copy = await storage.CopyAsync("a.txt", "b.txt");
        Assert.True(copy.IsSuccess);
        Assert.Equal("b.txt", copy.Value.Key);
        Assert.NotEqual(upload.Value.ETag, copy.Value.ETag);
        Assert.True(storage.WasCopied("a.txt", "b.txt"));
        Assert.Equal(Payload, storage.GetContent("b.txt"));

        Assert.True((await storage.DeleteAsync("a.txt")).IsSuccess);
        Assert.True(storage.WasDeleted("a.txt"));

        var batch = await storage.DeleteManyAsync(["b.txt", "b.txt", "missing.txt"]);
        Assert.True(batch.IsSuccess);
        Assert.True(batch.Value.IsComplete);
        Assert.Equal(["b.txt", "missing.txt"], batch.Value.Deleted);
        Assert.True(storage.WasDeleted("b.txt"));

        var notFound = await storage.DownloadAsync("a.txt");
        Assert.True(notFound.IsFailure);
        Assert.Equal(StorageErrorCodes.NotFound, notFound.Error.Code);
        Assert.Equal(ErrorType.NotFound, notFound.Error.Type);
    }

    [Fact]
    public async Task UploadAsync_NonSeekableStream_IsReadToEndAndNotDisposed()
    {
        var storage = new InMemoryFileStorage();
        var content = new NonSeekableStream(Payload);

        var result = await storage.UploadAsync("a.bin", content);

        Assert.True(result.IsSuccess);
        Assert.False(content.Disposed);
        Assert.Equal(Payload, storage.GetContent("a.bin"));
    }

    [Fact]
    public async Task UploadAsync_InvalidKey_ReturnsInvalidKey_AndStoresNothing()
    {
        var storage = new InMemoryFileStorage();

        var result = await storage.UploadAsync("../escape.txt", new MemoryStream(Payload));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCodes.InvalidKey, result.Error.Code);
        Assert.Empty(storage.Keys);
    }

    [Fact]
    public async Task UploadAsync_IfNotExists_SecondWriteReturnsAlreadyExists()
    {
        var storage = new InMemoryFileStorage();
        var options = new FileUploadOptions { Condition = WriteCondition.IfNotExists };

        var first = await storage.UploadAsync("a.txt", new MemoryStream(Payload), options);
        var second = await storage.UploadAsync("a.txt", new MemoryStream("other"u8.ToArray()), options);

        Assert.True(first.IsSuccess);
        Assert.Equal(StorageErrorCodes.AlreadyExists, second.Error.Code);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
        Assert.Equal(Payload, storage.GetContent("a.txt"));
    }

    [Fact]
    public async Task UploadAsync_IfMatch_ReplacesOnlyTheExpectedVersion()
    {
        var storage = new InMemoryFileStorage();
        var original = storage.Seed("a.txt", Payload);

        var stale = await storage.UploadAsync(
            "a.txt",
            new MemoryStream("x"u8.ToArray()),
            new FileUploadOptions { Condition = WriteCondition.IfMatch("\"not-the-etag\"") });
        var current = await storage.UploadAsync(
            "a.txt",
            new MemoryStream("y"u8.ToArray()),
            new FileUploadOptions { Condition = WriteCondition.IfMatch(original.ETag!.Trim('"')) });
        var missing = await storage.UploadAsync(
            "missing.txt",
            new MemoryStream("z"u8.ToArray()),
            new FileUploadOptions { Condition = WriteCondition.IfMatch(original.ETag!) });

        Assert.Equal(StorageErrorCodes.PreconditionFailed, stale.Error.Code);
        Assert.True(current.IsSuccess);
        Assert.Equal("y"u8.ToArray(), storage.GetContent("a.txt"));
        Assert.Equal(StorageErrorCodes.NotFound, missing.Error.Code);
    }

    [Fact]
    public async Task UploadAsync_ChecksumMismatch_ReturnsChecksumMismatch_AndStoresNothing()
    {
        var storage = new InMemoryFileStorage();
        var wrong = Convert.ToBase64String(SHA256.HashData("something else"u8.ToArray()));
        var right = Convert.ToBase64String(SHA256.HashData(Payload));

        var rejected = await storage.UploadAsync("a.txt", new MemoryStream(Payload), new FileUploadOptions { ChecksumSha256 = wrong });
        var accepted = await storage.UploadAsync("b.txt", new MemoryStream(Payload), new FileUploadOptions { ChecksumSha256 = right });

        Assert.Equal(StorageErrorCodes.ChecksumMismatch, rejected.Error.Code);
        Assert.DoesNotContain("a.txt", storage.Keys);
        Assert.True(accepted.IsSuccess);
    }

    [Fact]
    public async Task DownloadAsync_Range_ReturnsTheSliceAndItsRange()
    {
        var storage = new InMemoryFileStorage();
        storage.Seed("a.txt", Payload);

        var middle = await storage.DownloadAsync("a.txt", new FileDownloadOptions { Range = new ByteRange(6, 8) });
        var open = await storage.DownloadAsync("a.txt", new FileDownloadOptions { Range = new ByteRange(6) });
        var beyond = await storage.DownloadAsync("a.txt", new FileDownloadOptions { Range = new ByteRange(100) });

        await using (middle.Value)
        {
            Assert.Equal(3, middle.Value.Length);
            Assert.Equal(new ByteRange(6, 8), middle.Value.Range);
            Assert.Equal(Payload.Length, middle.Value.Properties.ContentLength);
            Assert.Equal("wor", await new StreamReader(middle.Value.Content).ReadToEndAsync());
        }

        await using (open.Value)
        {
            Assert.Equal("world", await new StreamReader(open.Value.Content).ReadToEndAsync());
            Assert.Equal(new ByteRange(6, 10), open.Value.Range);
        }

        Assert.Equal(StorageErrorCodes.InvalidRange, beyond.Error.Code);
    }

    [Fact]
    public async Task DownloadAsync_IfMatchMismatch_ReturnsPreconditionFailed()
    {
        var storage = new InMemoryFileStorage();
        storage.Seed("a.txt", Payload);

        var result = await storage.DownloadAsync("a.txt", new FileDownloadOptions { IfMatch = "\"stale\"" });

        Assert.Equal(StorageErrorCodes.PreconditionFailed, result.Error.Code);
    }

    [Fact]
    public async Task DeleteAsync_AbsentKey_IsIdempotent_ButIfMatchOnAbsentKeyFails()
    {
        var storage = new InMemoryFileStorage();

        var plain = await storage.DeleteAsync("never-existed.txt");
        var conditional = await storage.DeleteAsync("never-existed.txt", new FileDeleteOptions { IfMatch = "\"x\"" });

        Assert.True(plain.IsSuccess);
        Assert.False(storage.WasDeleted("never-existed.txt"));
        Assert.Equal(StorageErrorCodes.PreconditionFailed, conditional.Error.Code);
    }

    [Fact]
    public async Task CopyAsync_MissingSource_ReturnsNotFound_AndReplacesContentTypeAndMetadataWhenAsked()
    {
        var storage = new InMemoryFileStorage();
        storage.Seed("a.txt", Payload, "text/plain", new Dictionary<string, string> { ["a"] = "1" });

        var missing = await storage.CopyAsync("missing.txt", "b.txt");
        var replaced = await storage.CopyAsync(
            "a.txt",
            "b.txt",
            new FileCopyOptions { ContentType = "application/octet-stream", Metadata = new Dictionary<string, string> { ["b"] = "2" } });

        Assert.Equal(StorageErrorCodes.NotFound, missing.Error.Code);
        Assert.True(replaced.IsSuccess);
        var properties = (await storage.GetPropertiesAsync("b.txt")).Value;
        Assert.Equal("application/octet-stream", properties.ContentType);
        Assert.Equal("2", properties.Metadata["b"]);
        Assert.False(properties.Metadata.ContainsKey("a"));
    }

    [Fact]
    public async Task CopyToAsync_AnotherInMemoryStore_CopiesAndRecordsTheDestinationStore()
    {
        var source = new InMemoryFileStorage("quarantine");
        var destination = new InMemoryFileStorage("documents");
        source.Seed("in/a.txt", Payload, "text/plain");

        var result = await source.CopyToAsync("in/a.txt", destination, "final/a.txt");

        Assert.True(result.IsSuccess);
        Assert.Equal("documents", result.Value.Store);
        Assert.Equal(Payload, destination.GetContent("final/a.txt"));
        Assert.Contains(("in/a.txt", "documents", "final/a.txt"), source.CopiedPairs);
        Assert.True(destination.WasUploaded("final/a.txt"));
    }

    [Fact]
    public async Task CopyToAsync_ForeignStore_StreamsThroughItsUpload()
    {
        var source = new InMemoryFileStorage("quarantine");
        source.Seed("a.txt", Payload, "text/plain");
        var inner = new InMemoryFileStorage("elsewhere");
        var foreign = new ForeignStore(inner);

        var result = await source.CopyToAsync("a.txt", foreign, "b.txt");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, foreign.Uploads);
        Assert.Equal(Payload, inner.GetContent("b.txt"));
        Assert.Equal("text/plain", (await inner.GetPropertiesAsync("b.txt")).Value.ContentType);
    }

    [Fact]
    public async Task ListPageAsync_NonRecursive_ReturnsObjectsAndFoldersInKeyOrder_AcrossPages()
    {
        var storage = new InMemoryFileStorage();
        foreach (var key in new[] { "docs/a.txt", "docs/b.txt", "docs/sub/c.txt", "docs/sub/d.txt", "docs/z.txt", "other/e.txt" })
        {
            storage.Seed(key, Payload);
        }

        var first = await storage.ListPageAsync(new FileListRequest { Prefix = "docs/", Recursive = false, PageSize = 2 });
        var second = await storage.ListPageAsync(new FileListRequest
        {
            Prefix = "docs/",
            Recursive = false,
            PageSize = 2,
            ContinuationToken = first.Value.ContinuationToken,
        });

        Assert.Equal(["docs/a.txt", "docs/b.txt"], first.Value.Items.Select(i => i.Key));
        Assert.Empty(first.Value.Folders);
        Assert.True(first.Value.HasMore);
        Assert.Equal(["docs/z.txt"], second.Value.Items.Select(i => i.Key));
        Assert.Equal(["docs/sub/"], second.Value.Folders);
        Assert.False(second.Value.HasMore);
    }

    [Fact]
    public async Task ListPageAsync_ForeignContinuationToken_ReturnsInvalidRequest()
    {
        var storage = new InMemoryFileStorage();

        var result = await storage.ListPageAsync(new FileListRequest { ContinuationToken = "not base64!" });

        Assert.Equal(StorageErrorCodes.InvalidRequest, result.Error.Code);
    }

    [Fact]
    public async Task ListAsync_YieldsEveryObjectUnderThePrefix_AcrossPages()
    {
        var storage = new InMemoryFileStorage();
        for (var i = 0; i < FileListRequest.MaxPageSize + 5; i++)
        {
            storage.Seed($"docs/{i:D5}.txt", Payload);
        }

        storage.Seed("other/x.txt", Payload);

        var keys = new List<string>();
        await foreach (var item in storage.ListAsync("docs/"))
        {
            keys.Add(item.Key);
        }

        Assert.Equal(FileListRequest.MaxPageSize + 5, keys.Count);
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        Assert.All(keys, k => Assert.StartsWith("docs/", k, StringComparison.Ordinal));
    }

    [Fact]
    public void ListAsync_InvalidPrefix_ThrowsStorageException()
    {
        var storage = new InMemoryFileStorage();

        var exception = Assert.Throws<StorageException>(() => storage.ListAsync("/absolute"));

        Assert.Equal(StorageErrorCodes.InvalidKey, exception.Error.Code);
    }

    [Fact]
    public async Task ListAsync_CancellationMidEnumeration_Stops()
    {
        var storage = new InMemoryFileStorage();
        for (var i = 0; i < 5; i++)
        {
            storage.Seed($"key-{i}", Payload);
        }

        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in storage.ListAsync(string.Empty, cts.Token))
            {
                seen.Add(item.Key);
                if (seen.Count == 1)
                {
                    await cts.CancelAsync();
                }
            }
        });

        Assert.True(seen.Count < 5);
    }

    [Fact]
    public async Task CreateDownloadUrlAsync_IsDeterministic_RecordsTheUrl_AndHonoursTheMaximumExpiry()
    {
        var clock = new FakeClock();
        var storage = new InMemoryFileStorage("docs", new InMemoryFileStorageOptions { Clock = clock, MaxPresignExpiry = TimeSpan.FromMinutes(30) });

        var first = await storage.CreateDownloadUrlAsync("a b/c.txt", new PresignedDownloadOptions { Expiry = TimeSpan.FromMinutes(30) });
        var second = await storage.CreateDownloadUrlAsync("a b/c.txt", new PresignedDownloadOptions { Expiry = TimeSpan.FromMinutes(30) });
        var tooLong = await storage.CreateDownloadUrlAsync("a b/c.txt", new PresignedDownloadOptions { Expiry = TimeSpan.FromMinutes(31) });

        Assert.True(first.IsSuccess);
        Assert.Equal("memory", first.Value.Url.Scheme);
        Assert.Equal("docs", first.Value.Url.Host);
        Assert.Contains("a%20b/c.txt", first.Value.Url.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("GET", first.Value.Method);
        Assert.Equal(clock.UtcNow + TimeSpan.FromMinutes(30), first.Value.ExpiresAt);
        Assert.Equal(first.Value.Url, second.Value.Url);
        Assert.Equal(StorageErrorCodes.ExpiryTooLong, tooLong.Error.Code);
        Assert.Equal(2, storage.IssuedDownloadUrls.Count);
    }

    [Fact]
    public async Task CreateUploadUrlAsync_ReturnsThePutHeadersTheClientMustSend()
    {
        var storage = new InMemoryFileStorage();

        var result = await storage.CreateUploadUrlAsync(
            "a.pdf",
            new PresignedUploadOptions { Expiry = TimeSpan.FromMinutes(5), ContentType = "application/pdf", CreateOnly = true });

        Assert.True(result.IsSuccess);
        Assert.Equal("PUT", result.Value.Method);
        Assert.Equal("application/pdf", result.Value.Headers["Content-Type"]);
        Assert.Equal("*", result.Value.Headers["If-None-Match"]);
        Assert.Single(storage.IssuedUploadUrls);
    }

    [Fact]
    public async Task CreateUploadFormAsync_ReturnsAFormPolicy_AndValidatesSizes()
    {
        var storage = new InMemoryFileStorage();

        var form = await storage.CreateUploadFormAsync(
            "a.png",
            new PresignedPostOptions { Expiry = TimeSpan.FromMinutes(5), MaxSize = 1024, ContentType = "image/" });
        var invalid = await storage.CreateUploadFormAsync(
            "a.png",
            new PresignedPostOptions { Expiry = TimeSpan.FromMinutes(5), MaxSize = 0, ContentType = "image/png" });

        Assert.True(form.IsSuccess);
        Assert.Equal("a.png", form.Value.Fields["key"]);
        Assert.Equal(StorageErrorCodes.InvalidRequest, invalid.Error.Code);
    }

    [Fact]
    public async Task MultipartUpload_StartUploadPartsComplete_AssemblesTheObjectInPartOrder()
    {
        var storage = new InMemoryFileStorage();
        var start = await storage.StartMultipartUploadAsync("big.bin", new MultipartUploadOptions { ContentType = "application/octet-stream" });
        var upload = start.Value;

        var partUrl = await storage.CreateUploadPartUrlAsync(upload, 2, TimeSpan.FromMinutes(5));
        var eTag2 = storage.UploadPart(upload, 2, "world"u8.ToArray());
        var eTag1 = storage.UploadPart(upload, 1, "hello "u8.ToArray());

        var incomplete = await storage.CompleteMultipartUploadAsync(upload, [new UploadedPart(1, "\"wrong\"")]);
        var complete = await storage.CompleteMultipartUploadAsync(upload, [new UploadedPart(2, eTag2), new UploadedPart(1, eTag1)]);
        var again = await storage.CompleteMultipartUploadAsync(upload, [new UploadedPart(1, eTag1)]);

        Assert.True(partUrl.IsSuccess);
        Assert.Contains("partNumber=2", partUrl.Value.Url.Query, StringComparison.Ordinal);
        Assert.Equal(StorageErrorCodes.InvalidRequest, incomplete.Error.Code);
        Assert.True(complete.IsSuccess);
        Assert.Equal("hello world"u8.ToArray(), storage.GetContent("big.bin"));
        Assert.Equal("application/octet-stream", (await storage.GetPropertiesAsync("big.bin")).Value.ContentType);
        Assert.Equal(StorageErrorCodes.NotFound, again.Error.Code);
    }

    [Fact]
    public async Task AbortMultipartUpload_DiscardsTheUpload_AndUnknownUploadsSucceed()
    {
        var storage = new InMemoryFileStorage();
        var upload = (await storage.StartMultipartUploadAsync("big.bin")).Value;
        var eTag = storage.UploadPart(upload, 1, Payload);

        var aborted = await storage.AbortMultipartUploadAsync(upload);
        var unknown = await storage.AbortMultipartUploadAsync(new MultipartUpload("x.bin", "unknown"));
        var complete = await storage.CompleteMultipartUploadAsync(upload, [new UploadedPart(1, eTag)]);

        Assert.True(aborted.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.Equal(StorageErrorCodes.NotFound, complete.Error.Code);
        Assert.Throws<InvalidOperationException>(() => storage.UploadPart(upload, 2, Payload));
    }

    [Fact]
    public async Task SimulateFailure_FailsWrites_ButLeavesReadsPresignAndProbeUnaffected()
    {
        var storage = new InMemoryFileStorage();
        storage.Seed("seeded.txt", Payload, "text/plain");
        storage.SimulateFailure = true;

        var upload = await storage.UploadAsync("new.txt", new MemoryStream(Payload));
        var copy = await storage.CopyAsync("seeded.txt", "copy.txt");
        var delete = await storage.DeleteAsync("seeded.txt");
        var deleteMany = await storage.DeleteManyAsync(["seeded.txt"]);

        Assert.Equal(StorageErrorCodes.Unavailable, upload.Error.Code);
        Assert.Equal(StorageErrorCodes.Unavailable, copy.Error.Code);
        Assert.Equal(StorageErrorCodes.Unavailable, delete.Error.Code);
        Assert.True(deleteMany.IsSuccess);
        Assert.Empty(deleteMany.Value.Deleted);
        Assert.Equal(StorageErrorCodes.Unavailable, Assert.Single(deleteMany.Value.Failed).Error.Code);
        Assert.Equal(["seeded.txt"], storage.Keys);
        Assert.Empty(storage.UploadedKeys);
        Assert.Empty(storage.DeletedKeys);

        var download = await storage.DownloadAsync("seeded.txt");
        Assert.True(download.IsSuccess);
        await download.Value.DisposeAsync();
        Assert.True((await storage.ExistsAsync("seeded.txt")).Value);
        Assert.True((await storage.CreateDownloadUrlAsync("seeded.txt", new PresignedDownloadOptions { Expiry = TimeSpan.FromMinutes(1) })).IsSuccess);
        Assert.True((await storage.ProbeAsync()).IsSuccess);
    }

    [Fact]
    public async Task SimulateUnavailable_FailsOnlyTheProbe()
    {
        var storage = new InMemoryFileStorage { SimulateUnavailable = true };

        var probe = await storage.ProbeAsync();
        var upload = await storage.UploadAsync("a.txt", new MemoryStream(Payload));

        Assert.Equal(StorageErrorCodes.Unavailable, probe.Error.Code);
        Assert.True(upload.IsSuccess);
    }

    [Fact]
    public void Seed_StoresWithoutRecordingAsUploaded()
    {
        var storage = new InMemoryFileStorage();

        var reference = storage.Seed("seeded.txt", new MemoryStream(Payload), "text/plain");

        Assert.False(storage.WasUploaded("seeded.txt"));
        Assert.Equal("seeded.txt", reference.Key);
        Assert.Equal(Payload, storage.GetContent("seeded.txt"));
    }

    [Fact]
    public async Task Reset_ClearsObjectsRecordingsAndSimulationFlags()
    {
        var storage = new InMemoryFileStorage { SimulateUnavailable = true };
        await storage.UploadAsync("a.txt", new MemoryStream(Payload));
        await storage.DeleteAsync("a.txt");
        storage.SimulateFailure = true;

        storage.Reset();

        Assert.False(storage.WasUploaded("a.txt"));
        Assert.False(storage.WasDeleted("a.txt"));
        Assert.Empty(storage.Keys);
        Assert.False(storage.SimulateFailure);
        Assert.False(storage.SimulateUnavailable);
    }

    [Fact]
    public void Constructor_InvalidStoreName_Throws() =>
        Assert.Throws<ArgumentException>(() => new InMemoryFileStorage("bad name"));

    [Fact]
    public void TenantKey_BuildsTheTenantViewPrefix() =>
        Assert.Equal($"tenants/{TenantA}/a.txt", InMemoryFileStorage.TenantKey(TenantA, "a.txt"));

    private sealed class NonSeekableStream(byte[] content) : MemoryStream(content)
    {
        public bool Disposed { get; private set; }

        public override bool CanSeek => false;

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>A store that is not an <see cref="InMemoryFileStorage"/>, forcing the streamed copy path.</summary>
    private sealed class ForeignStore(InMemoryFileStorage inner) : IFileStorage
    {
        public int Uploads { get; private set; }

        public string StoreName => inner.StoreName;

        public TenantId? TenantId => null;

        public Task<SharedKernel.Primitives.Results.Result<FileReference>> UploadAsync(string key, Stream content, FileUploadOptions? options = null, CancellationToken cancellationToken = default)
        {
            Uploads++;
            return inner.UploadAsync(key, content, options, cancellationToken);
        }

        public Task<SharedKernel.Primitives.Results.Result<FileDownload>> DownloadAsync(string key, FileDownloadOptions? options = null, CancellationToken cancellationToken = default) => inner.DownloadAsync(key, options, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<FileProperties>> GetPropertiesAsync(string key, CancellationToken cancellationToken = default) => inner.GetPropertiesAsync(key, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result> DeleteAsync(string key, FileDeleteOptions? options = null, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, options, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<BatchDeleteResult>> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) => inner.DeleteManyAsync(keys, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<FileReference>> CopyAsync(string sourceKey, string destinationKey, FileCopyOptions? options = null, CancellationToken cancellationToken = default) => inner.CopyAsync(sourceKey, destinationKey, options, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<FileReference>> CopyToAsync(string sourceKey, IFileStorage destination, string destinationKey, FileCopyOptions? options = null, CancellationToken cancellationToken = default) => inner.CopyToAsync(sourceKey, destination, destinationKey, options, cancellationToken);

        public IAsyncEnumerable<FileListItem> ListAsync(string prefix = "", CancellationToken cancellationToken = default) => inner.ListAsync(prefix, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<FileListPage>> ListPageAsync(FileListRequest request, CancellationToken cancellationToken = default) => inner.ListPageAsync(request, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<PresignedRequest>> CreateDownloadUrlAsync(string key, PresignedDownloadOptions options, CancellationToken cancellationToken = default) => inner.CreateDownloadUrlAsync(key, options, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<PresignedRequest>> CreateUploadUrlAsync(string key, PresignedUploadOptions options, CancellationToken cancellationToken = default) => inner.CreateUploadUrlAsync(key, options, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<PresignedPost>> CreateUploadFormAsync(string key, PresignedPostOptions options, CancellationToken cancellationToken = default) => inner.CreateUploadFormAsync(key, options, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<MultipartUpload>> StartMultipartUploadAsync(string key, MultipartUploadOptions? options = null, CancellationToken cancellationToken = default) => inner.StartMultipartUploadAsync(key, options, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<PresignedRequest>> CreateUploadPartUrlAsync(MultipartUpload upload, int partNumber, TimeSpan expiry, CancellationToken cancellationToken = default) => inner.CreateUploadPartUrlAsync(upload, partNumber, expiry, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result<FileReference>> CompleteMultipartUploadAsync(MultipartUpload upload, IReadOnlyCollection<UploadedPart> parts, WriteCondition? condition = null, CancellationToken cancellationToken = default) => inner.CompleteMultipartUploadAsync(upload, parts, condition, cancellationToken);

        public Task<SharedKernel.Primitives.Results.Result> AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default) => inner.AbortMultipartUploadAsync(upload, cancellationToken);
    }
}
