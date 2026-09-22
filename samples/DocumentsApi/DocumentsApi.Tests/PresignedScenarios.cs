using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DocumentsApi.Tests.Infrastructure;
using FluentAssertions;
using SharedKernel.Storage;
using Xunit.Abstractions;

namespace DocumentsApi.Tests;

/// <summary>
/// Presigned transfers: the API signs, and a plain HttpClient — like a browser or mobile app — talks to the provider
/// directly, with no SDK and no credentials.
/// </summary>
[Collection(BackendsCollection.Name)]
public sealed class PresignedScenarios(Backends backends, ITestOutputHelper output)
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task A_download_link_serves_the_file_with_the_requested_file_name(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey("report.csv");
        (await api.PutAsync($"/files/{store}/{key}", new StringContent("id,total\n1,10\n"))).EnsureSuccessStatusCode();

        PresignedRequest link = await SampleHost.ReadAsync<PresignedRequest>(
            await api.PostAsJsonAsync($"/links/{store}/download", new DownloadLinkRequest(key, 300, "report.csv")));
        using HttpResponseMessage response = await Client.GetAsync(link.Url);

        link.Method.Should().Be("GET");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Be("id,total\n1,10\n");
        response.Content.Headers.ContentDisposition?.FileName?.Trim('"').Should().Be("report.csv");
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task A_link_beyond_the_store_maximum_is_refused(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();

        // documents allows 15 minutes; the others the 1 hour default.
        using HttpResponseMessage response = await api.PostAsJsonAsync($"/links/{store}/download", new DownloadLinkRequest("a.txt", 2 * 3600));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SampleHost.ErrorCodeAsync(response)).Should().Be(StorageErrorCodes.ExpiryTooLong);
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task An_upload_link_accepts_the_signed_content_type_only(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey("photo.png");
        PresignedRequest link = await SampleHost.ReadAsync<PresignedRequest>(
            await api.PostAsJsonAsync($"/links/{store}/upload", new UploadLinkRequest(key, "image/png", 300)));

        using HttpResponseMessage wrongType = await SendAsync(link, [1, 2, 3], contentType: "text/html");
        using HttpResponseMessage accepted = await SendAsync(link, [1, 2, 3]);

        output.WriteLine($"{backend}/{store} signed headers: {string.Join(", ", link.Headers.Keys)}");
        wrongType.IsSuccessStatusCode.Should().BeFalse();
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        FileProperties stored = await SampleHost.ReadAsync<FileProperties>(await api.GetAsync($"/properties/{store}/{key}"));
        stored.ContentType.Should().Be("image/png");
        stored.ContentLength.Should().Be(3);
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task An_upload_form_enforces_its_size_and_type_policy(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey("avatar.png");
        PresignedPost form = await SampleHost.ReadAsync<PresignedPost>(
            await api.PostAsJsonAsync($"/links/{store}/form", new UploadFormRequest(key, "image/", 1024, 300)));

        using HttpResponseMessage tooLarge = await PostFormAsync(form, "image/png", SampleHost.Bytes(4096));
        using HttpResponseMessage wrongType = await PostFormAsync(form, "application/x-msdownload", SampleHost.Bytes(100));
        using HttpResponseMessage accepted = await PostFormAsync(form, "image/png", SampleHost.Bytes(700));

        output.WriteLine($"{backend}/{store}: too large {(int)tooLarge.StatusCode}, wrong type {(int)wrongType.StatusCode}, accepted {(int)accepted.StatusCode}");
        tooLarge.IsSuccessStatusCode.Should().BeFalse();
        wrongType.IsSuccessStatusCode.Should().BeFalse();
        accepted.IsSuccessStatusCode.Should().BeTrue(await accepted.Content.ReadAsStringAsync());
        FileProperties stored = await SampleHost.ReadAsync<FileProperties>(await api.GetAsync($"/properties/{store}/{key}"));
        stored.Should().BeEquivalentTo(new { ContentLength = 700L, ContentType = "image/png" });
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task A_large_file_uploads_directly_in_presigned_parts(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = SampleHost.NewKey("video.mp4");
        byte[] part1 = SampleHost.Bytes(5 * 1024 * 1024, seed: 6);
        byte[] part2 = SampleHost.Bytes(123_456, seed: 7);

        MultipartUpload upload = await SampleHost.ReadAsync<MultipartUpload>(
            await api.PostAsJsonAsync($"/multipart/{store}/start", new StartMultipartRequest(key, "video/mp4")));
        var parts = new List<UploadedPart>();
        foreach ((int number, byte[] bytes) in new[] { (1, part1), (2, part2) })
        {
            PresignedRequest url = await SampleHost.ReadAsync<PresignedRequest>(
                await api.PostAsJsonAsync($"/multipart/{store}/part-url", new PartUrlRequest(upload, number, 300)));
            using HttpResponseMessage response = await SendAsync(url, bytes);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            parts.Add(new UploadedPart(number, response.Headers.ETag!.Tag));
        }

        FileReference reference = await SampleHost.ReadAsync<FileReference>(
            await api.PostAsJsonAsync($"/multipart/{store}/complete", new CompleteMultipartRequest(upload, parts)));
        byte[] downloaded = await api.GetByteArrayAsync($"/files/{store}/{key}");

        reference.Key.Should().Be(key);
        SampleHost.Sha256(downloaded).Should().Be(SampleHost.Sha256([.. part1, .. part2]));
        (await SampleHost.ReadAsync<FileProperties>(await api.GetAsync($"/properties/{store}/{key}"))).ContentType.Should().Be("video/mp4");
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task An_abandoned_multipart_upload_is_aborted(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        MultipartUpload upload = await SampleHost.ReadAsync<MultipartUpload>(
            await api.PostAsJsonAsync($"/multipart/{store}/start", new StartMultipartRequest(SampleHost.NewKey())));

        using HttpResponseMessage aborted = await api.PostAsJsonAsync($"/multipart/{store}/abort", upload);
        using HttpResponseMessage completeAfterAbort = await api.PostAsJsonAsync(
            $"/multipart/{store}/complete", new CompleteMultipartRequest(upload, [new UploadedPart(1, "\"x\"")]));

        aborted.IsSuccessStatusCode.Should().BeTrue(await aborted.Content.ReadAsStringAsync());
        completeAfterAbort.IsSuccessStatusCode.Should().BeFalse();
        output.WriteLine($"{backend}/{store}: complete after abort → {(int)completeAfterAbort.StatusCode} {await SampleHost.ErrorCodeAsync(completeAfterAbort)}");
    }

    private static async Task<HttpResponseMessage> SendAsync(PresignedRequest request, byte[] body, string? contentType = null)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url) { Content = new ByteArrayContent(body) };
        foreach ((string name, string value) in request.Headers)
        {
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? value);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return await Client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(PresignedPost form, string contentType, byte[] file)
    {
        using var body = new MultipartFormDataContent();
        foreach ((string name, string value) in form.Fields)
        {
            body.Add(new StringContent(value), name);
        }

        body.Add(new StringContent(contentType), "Content-Type");

        // Built like a browser's file part: a plain filename. .NET's default adds filename*=utf-8'', which OBS rejects.
        var filePart = new ByteArrayContent(file);
        filePart.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"file\"", FileName = "\"upload.bin\"" };
        filePart.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        body.Add(filePart);
        return await Client.PostAsync(form.Url, body);
    }
}
