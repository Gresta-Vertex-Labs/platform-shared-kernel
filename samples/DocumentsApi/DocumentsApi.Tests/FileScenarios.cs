using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DocumentsApi.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Storage;

namespace DocumentsApi.Tests;

/// <summary>Server-side file operations through the API, for every store on every backend.</summary>
[Collection(BackendsCollection.Name)]
public sealed class FileScenarios(Backends backends)
{
    /// <summary>
    /// The platform caps request bodies at 4 MiB, and Kestrel — which the in-memory test server does not run — enforces
    /// it. So the one endpoint that lifts the cap is pinned by its metadata: without it, every upload above 4 MiB would
    /// fail in production while these tests stayed green.
    /// </summary>
    [Fact]
    public void Only_the_upload_endpoint_lifts_the_request_body_limit()
    {
        var limited = backends[Backends.MinIO].Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>() is not null)
            .ToList();

        RouteEndpoint upload = limited.Should().ContainSingle().Subject;
        upload.RoutePattern.RawText.Should().Be("/files/{store}/{**key}");
        upload.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Should().Equal("PUT");
        upload.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize.Should().Be(FileEndpoints.MaxUploadBytes);
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task An_upload_round_trips_bytes_headers_and_metadata(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey("invoice.pdf");
        byte[] content = SampleHost.Bytes(64 * 1024);

        using var upload = new HttpRequestMessage(HttpMethod.Put, $"/files/{store}/{key}") { Content = new ByteArrayContent(content) };
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        upload.Headers.CacheControl = new CacheControlHeaderValue { Private = true, MaxAge = TimeSpan.FromMinutes(1) };
        upload.Headers.Add("X-Meta-Order-Id", "ord-42");
        FileReference reference = await SampleHost.ReadAsync<FileReference>(await api.SendAsync(upload));

        using HttpResponseMessage download = await api.GetAsync($"/files/{store}/{key}");
        FileProperties properties = await SampleHost.ReadAsync<FileProperties>(await api.GetAsync($"/properties/{store}/{key}"));

        reference.Should().BeEquivalentTo(new { Store = store, Key = key });
        reference.ETag.Should().NotBeNullOrEmpty();
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(content);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        properties.ContentLength.Should().Be(content.Length);
        properties.ContentType.Should().Be("application/pdf");
        properties.CacheControl.Should().Contain("max-age=60");
        properties.Metadata.Should().Contain("order-id", "ord-42");
        properties.ETag.Should().Be(reference.ETag);
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task A_large_request_body_is_streamed_into_the_store_in_parts(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey("export.bin");
        byte[] content = SampleHost.Bytes((12 * 1024 * 1024) + 321, seed: 2);

        // The ASP.NET Core request body is a forward-only stream of unknown length.
        using HttpResponseMessage upload = await api.PutAsync($"/files/{store}/{key}", new StreamContent(new MemoryStream(content)));
        using HttpResponseMessage download = await api.GetAsync($"/files/{store}/{key}");

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        SampleHost.Sha256(await download.Content.ReadAsByteArrayAsync()).Should().Be(SampleHost.Sha256(content));
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task A_range_request_returns_206_with_only_those_bytes(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey();
        byte[] content = SampleHost.Bytes(10_000, seed: 3);
        await api.PutAsync($"/files/{store}/{key}", new ByteArrayContent(content));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/files/{store}/{key}");
        request.Headers.Range = new RangeHeaderValue(1000, 1999);
        using HttpResponseMessage response = await api.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(content[1000..2000]);
        response.Content.Headers.ContentRange!.ToString().Should().Be("bytes 1000-1999/10000");
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task A_missing_object_is_404_and_deleting_it_is_idempotent(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey();

        using HttpResponseMessage download = await api.GetAsync($"/files/{store}/{key}");
        using HttpResponseMessage delete = await api.DeleteAsync($"/files/{store}/{key}");

        download.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SampleHost.ErrorCodeAsync(download)).Should().Be(StorageErrorCodes.NotFound);
        delete.IsSuccessStatusCode.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task Create_only_uploads_never_overwrite(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey();

        HttpResponseMessage first = await PutConditionalAsync(api, store, key, [1], ifNoneMatch: true);
        HttpResponseMessage second = await PutConditionalAsync(api, store, key, [2], ifNoneMatch: true);

        if (IsObs(store))
        {
            first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await SampleHost.ErrorCodeAsync(first)).Should().Be(StorageErrorCodes.NotSupported);
            return;
        }

        // storage.already_exists is a conflict; the client asked for it with If-None-Match, so the answer is 412.
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await SampleHost.ErrorCodeAsync(second)).Should().Be(StorageErrorCodes.AlreadyExists);
        (await api.GetByteArrayAsync($"/files/{store}/{key}")).Should().Equal(1);
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task If_match_writes_detect_a_concurrent_change(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey();
        FileReference original = await SampleHost.ReadAsync<FileReference>(await api.PutAsync($"/files/{store}/{key}", new ByteArrayContent([1])));

        HttpResponseMessage update = await PutConditionalAsync(api, store, key, [2], ifMatch: original.ETag);
        HttpResponseMessage stale = await PutConditionalAsync(api, store, key, [3], ifMatch: original.ETag);

        if (IsObs(store))
        {
            (await SampleHost.ErrorCodeAsync(update)).Should().Be(StorageErrorCodes.NotSupported);
            return;
        }

        // storage.precondition_failed is a conflict; the client named the version in If-Match, so the answer is 412.
        update.StatusCode.Should().Be(HttpStatusCode.Created);
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await SampleHost.ErrorCodeAsync(stale)).Should().Be(StorageErrorCodes.PreconditionFailed);
        (await api.GetByteArrayAsync($"/files/{store}/{key}")).Should().Equal(2);

        // A read pinned to the old version is refused the same way.
        using HttpResponseMessage staleRead = await SendWithIfMatchAsync(api, HttpMethod.Get, $"/files/{store}/{key}", original.ETag!);
        staleRead.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await SampleHost.ErrorCodeAsync(staleRead)).Should().Be(StorageErrorCodes.PreconditionFailed);
    }

    /// <summary>
    /// A precondition the service cannot use is refused, never ignored: ignoring it would make the client's conditional
    /// write unconditional. The endpoint declares <c>If-Match</c> as a nullable <c>IfMatch&lt;string&gt;</c>, so the
    /// platform refuses it before the endpoint runs: the ETag without its quotes (not an entity tag), <c>*</c> or a list
    /// is 400, a weak tag — which <c>If-Match</c>'s strong comparison never matches — 412.
    /// </summary>
    [Theory]
    [InlineData("unquoted", HttpStatusCode.BadRequest, PresentationErrorCodes.PreconditionInvalid)]
    [InlineData("any", HttpStatusCode.BadRequest, PresentationErrorCodes.PreconditionInvalid)]
    [InlineData("list", HttpStatusCode.BadRequest, PresentationErrorCodes.PreconditionInvalid)]
    [InlineData("weak", HttpStatusCode.PreconditionFailed, PresentationErrorCodes.PreconditionFailed)]
    public async Task An_if_match_the_service_cannot_use_is_refused_not_ignored(string sent, HttpStatusCode status, string code)
    {
        using HttpClient api = backends[Backends.MinIO].Api();
        string key = SampleHost.NewKey();
        FileReference original = await SampleHost.ReadAsync<FileReference>(
            await api.PutAsync($"/files/{Stores.Assets}/{key}", new ByteArrayContent([1])));
        string ifMatch = sent switch
        {
            "unquoted" => original.ETag!.Trim('"'),
            "any" => "*",
            "list" => $"{original.ETag}, \"another\"",
            _ => $"W/{original.ETag}",
        };

        HttpResponseMessage refused = await PutConditionalAsync(api, Stores.Assets, key, [2], ifMatch: ifMatch);

        refused.StatusCode.Should().Be(status);
        (await SampleHost.ErrorCodeAsync(refused)).Should().Be(code);
        (await api.GetByteArrayAsync($"/files/{Stores.Assets}/{key}")).Should().Equal(1);
    }

    /// <summary>
    /// 412 answers a precondition the client sent in a header. A create-only copy asks for it in its body, so the same
    /// <c>storage.already_exists</c> is an ordinary conflict: 409.
    /// </summary>
    [Theory]
    [MemberData(nameof(Backends.AllBackends), MemberType = typeof(Backends))]
    public async Task A_create_only_copy_onto_an_existing_file_is_409(string backend)
    {
        using HttpClient api = backends[backend].Api();
        string source = SampleHost.NewKey();
        string destination = SampleHost.NewKey();
        (await api.PutAsync($"/files/{Stores.Assets}/{source}", new ByteArrayContent([1]))).EnsureSuccessStatusCode();
        (await api.PutAsync($"/files/{Stores.Assets}/{destination}", new ByteArrayContent([2]))).EnsureSuccessStatusCode();

        using HttpResponseMessage copy = await api.PostAsJsonAsync(
            "/copy", new CopyRequest(Stores.Assets, source, Stores.Assets, destination, CreateOnly: true));

        copy.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SampleHost.ErrorCodeAsync(copy)).Should().Be(StorageErrorCodes.AlreadyExists);
        (await api.GetByteArrayAsync($"/files/{Stores.Assets}/{destination}")).Should().Equal(2);
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task A_checksum_is_verified_by_the_provider(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        byte[] content = SampleHost.Bytes(4096, seed: 4);

        HttpResponseMessage good = await PutWithChecksumAsync(api, store, SampleHost.NewKey(), content, SampleHost.Sha256(content));
        HttpResponseMessage bad = await PutWithChecksumAsync(api, store, SampleHost.NewKey(), content, SampleHost.Sha256([9, 9, 9]));

        if (IsObs(store))
        {
            (await SampleHost.ErrorCodeAsync(good)).Should().Be(StorageErrorCodes.NotSupported);
            return;
        }

        good.StatusCode.Should().Be(HttpStatusCode.Created, await good.Content.ReadAsStringAsync());
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SampleHost.ErrorCodeAsync(bad)).Should().Be(StorageErrorCodes.ChecksumMismatch);
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task Listing_returns_folders_and_pages(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string root = $"{Guid.NewGuid():N}/";
        foreach (string name in new[] { "a.txt", "b.txt", "c.txt", "2026/x.txt", "2027/y.txt" })
        {
            (await api.PutAsync($"/files/{store}/{root}{name}", new ByteArrayContent([1]))).EnsureSuccessStatusCode();
        }

        FileListPage folder = await SampleHost.ReadAsync<FileListPage>(await api.GetAsync($"/list/{store}?prefix={root}&recursive=false"));
        FileListPage first = await SampleHost.ReadAsync<FileListPage>(await api.GetAsync($"/list/{store}?prefix={root}&pageSize=3"));
        FileListPage second = await SampleHost.ReadAsync<FileListPage>(
            await api.GetAsync($"/list/{store}?prefix={root}&pageSize=3&continuationToken={Uri.EscapeDataString(first.ContinuationToken!)}"));

        folder.Items.Select(i => i.Key).Should().Equal($"{root}a.txt", $"{root}b.txt", $"{root}c.txt");
        folder.Folders.Should().Equal($"{root}2026/", $"{root}2027/");
        first.Items.Should().HaveCount(3);
        second.Items.Should().HaveCount(2);
        second.HasMore.Should().BeFalse();
        first.Items.Concat(second.Items).Select(i => i.Key).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task A_batch_delete_reports_every_key(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string[] keys = [.. Enumerable.Range(0, 4).Select(i => SampleHost.NewKey($"{i}.txt"))];
        foreach (string key in keys[..3])
        {
            (await api.PutAsync($"/files/{store}/{key}", new ByteArrayContent([1]))).EnsureSuccessStatusCode();
        }

        BatchDeleteResult result = await SampleHost.ReadAsync<BatchDeleteResult>(await api.PostAsJsonAsync($"/delete-many/{store}", keys));

        result.Failed.Should().BeEmpty();
        result.Deleted.Should().BeEquivalentTo(keys);
        foreach (string key in keys)
        {
            (await api.GetAsync($"/files/{store}/{key}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Theory]
    [MemberData(nameof(Backends.AllBackends), MemberType = typeof(Backends))]
    public async Task Files_copy_within_a_store_and_across_providers(string backend)
    {
        using HttpClient api = backends[backend].Api();
        string source = SampleHost.NewKey("scan.pdf");
        byte[] content = SampleHost.Bytes(2048, seed: 5);
        using var upload = new ByteArrayContent(content);
        upload.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        (await api.PutAsync($"/files/{Stores.Assets}/{source}", upload)).EnsureSuccessStatusCode();

        FileReference sameStore = await SampleHost.ReadAsync<FileReference>(
            await api.PostAsJsonAsync("/copy", new CopyRequest(Stores.Assets, source, Stores.Assets, $"copies/{source}")));
        FileReference toTenant = await SampleHost.ReadAsync<FileReference>(
            await api.PostAsJsonAsync("/copy", new CopyRequest(Stores.Assets, source, Stores.Documents, $"inbox/{source}")));
        FileReference toObs = await SampleHost.ReadAsync<FileReference>(
            await api.PostAsJsonAsync("/copy", new CopyRequest(Stores.Assets, source, Stores.Archive, $"archived/{source}")));

        sameStore.Should().BeEquivalentTo(new { Store = Stores.Assets, Key = $"copies/{source}" });
        toTenant.Should().BeEquivalentTo(new { Store = Stores.Documents, TenantId = "acme", Key = $"inbox/{source}" });
        toObs.Should().BeEquivalentTo(new { Store = Stores.Archive, Key = $"archived/{source}" });
        (await api.GetByteArrayAsync($"/files/{Stores.Documents}/inbox/{source}")).Should().Equal(content);
        FileProperties archived = await SampleHost.ReadAsync<FileProperties>(await api.GetAsync($"/properties/{Stores.Archive}/archived/{source}"));
        archived.ContentType.Should().Be("application/pdf");
        archived.ContentLength.Should().Be(content.Length);
    }

    private static bool IsObs(string store) => store == Stores.Archive;

    private static Task<HttpResponseMessage> PutConditionalAsync(
        HttpClient api, string store, string key, byte[] body, bool ifNoneMatch = false, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/files/{store}/{key}") { Content = new ByteArrayContent(body) };
        if (ifNoneMatch)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return api.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendWithIfMatchAsync(HttpClient api, HttpMethod method, string url, string ifMatch)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return api.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PutWithChecksumAsync(HttpClient api, string store, string key, byte[] body, string checksum)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/files/{store}/{key}") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Checksum-Sha256", checksum);
        return api.SendAsync(request);
    }
}
