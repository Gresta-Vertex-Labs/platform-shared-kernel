using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage.S3.Tests.Infrastructure;

namespace SharedKernel.Storage.S3.Tests;

[Collection(MinioCollection.Name)]
public sealed class PresignedRequestTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private readonly ServiceProvider _host;
    private readonly IFileStorage _files;

    public PresignedRequestTests(MinioFixture minio)
    {
        _host = minio.CreateHost(extraStores: s3 => s3.AddStore("sealed", o =>
        {
            o.Bucket = MinioFixture.BucketA;
            o.KeyPrefix = "sealed/";
            o.Encryption = S3.S3Encryption.S3Managed;
            o.MaxPresignExpiry = TimeSpan.FromMinutes(10);
        }));
        _files = _host.GetRequiredKeyedService<IFileStorage>("files");
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_download_url_serves_the_object_with_the_requested_disposition()
    {
        string key = TestData.UniqueKey("report.csv");
        await _files.UploadAsync(key, new MemoryStream("a,b"u8.ToArray()), new FileUploadOptions { ContentType = "text/csv" });

        PresignedRequest url = (await _files.CreateDownloadUrlAsync(key, new PresignedDownloadOptions
        {
            Expiry = TimeSpan.FromMinutes(5),
            ContentDisposition = "attachment; filename=\"report.csv\"",
        })).Ok();
        using HttpResponseMessage response = await Http.GetAsync(url.Url);

        url.Method.Should().Be("GET");
        url.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("a,b");
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be("report.csv");
    }

    [Fact]
    public async Task Expiries_beyond_the_store_maximum_are_refused()
    {
        var tooLong = new PresignedDownloadOptions { Expiry = TimeSpan.FromHours(2) };

        (await _files.CreateDownloadUrlAsync("a", tooLong)).Error.Code.Should().Be(StorageErrorCodes.ExpiryTooLong);
        (await _files.CreateDownloadUrlAsync("a", tooLong with { Expiry = TimeSpan.Zero })).Error.Code.Should().Be(StorageErrorCodes.ExpiryTooLong);
    }

    [Fact]
    public async Task An_upload_url_accepts_the_signed_content_type_and_nothing_else()
    {
        string key = TestData.UniqueKey("photo.png");
        PresignedRequest url = (await _files.CreateUploadUrlAsync(key, new PresignedUploadOptions
        {
            Expiry = TimeSpan.FromMinutes(5),
            ContentType = "image/png",
            Metadata = new Dictionary<string, string> { ["uploader"] = "u1" },
        })).Ok();

        using HttpResponseMessage wrongType = await SendAsync(url, [1, 2], overrideContentType: "text/html");
        using HttpResponseMessage accepted = await SendAsync(url, [1, 2]);

        url.Method.Should().Be("PUT");
        url.Headers.Should().Contain("Content-Type", "image/png").And.Contain("x-amz-meta-uploader", "u1");
        wrongType.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        FileProperties stored = (await _files.GetPropertiesAsync(key)).Ok();
        stored.ContentType.Should().Be("image/png");
        stored.Metadata.Should().Contain("uploader", "u1");
    }

    [Fact]
    public async Task A_create_only_upload_url_cannot_overwrite()
    {
        string key = TestData.UniqueKey();
        PresignedRequest url = (await _files.CreateUploadUrlAsync(key, new PresignedUploadOptions
        {
            Expiry = TimeSpan.FromMinutes(5),
            ContentType = "application/octet-stream",
            CreateOnly = true,
        })).Ok();

        using HttpResponseMessage first = await SendAsync(url, [1]);
        using HttpResponseMessage second = await SendAsync(url, [2]);

        url.Headers.Should().Contain("If-None-Match", "*");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await TestData.ReadAllAsync(_files, key)).Should().Equal(1);
    }

    [Fact]
    public async Task An_encrypted_store_tells_the_client_which_encryption_headers_to_send()
    {
        IFileStorage sealedStore = _host.GetRequiredKeyedService<IFileStorage>("sealed");

        PresignedRequest url = (await sealedStore.CreateUploadUrlAsync("a.bin", new PresignedUploadOptions
        {
            Expiry = TimeSpan.FromMinutes(5),
            ContentType = "application/octet-stream",
        })).Ok();

        url.Headers.Should().Contain("x-amz-server-side-encryption", "AES256");
        (await sealedStore.CreateUploadUrlAsync("a.bin", new PresignedUploadOptions
        {
            Expiry = TimeSpan.FromMinutes(11),
            ContentType = "application/octet-stream",
        })).Error.Code.Should().Be(StorageErrorCodes.ExpiryTooLong);
    }

    [Fact]
    public async Task An_upload_form_enforces_its_size_and_content_type_policy()
    {
        string key = TestData.UniqueKey("avatar.png");
        PresignedPost form = (await _files.CreateUploadFormAsync(key, new PresignedPostOptions
        {
            Expiry = TimeSpan.FromMinutes(5),
            MaxSize = 1024,
            ContentType = "image/",
        })).Ok();

        using HttpResponseMessage tooLarge = await PostAsync(form, "image/png", TestData.Bytes(2048));
        using HttpResponseMessage wrongType = await PostAsync(form, "text/html", TestData.Bytes(10));
        using HttpResponseMessage accepted = await PostAsync(form, "image/png", TestData.Bytes(512));

        form.Fields.Keys.Should().Contain(k => k.Equals("policy", StringComparison.OrdinalIgnoreCase));
        tooLarge.IsSuccessStatusCode.Should().BeFalse();
        wrongType.IsSuccessStatusCode.Should().BeFalse();
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _files.GetPropertiesAsync(key)).Ok().Should().BeEquivalentTo(new { ContentLength = 512L, ContentType = "image/png" });
    }

    [Fact]
    public async Task A_client_can_upload_a_large_file_in_presigned_parts()
    {
        string key = TestData.UniqueKey("video.mp4");
        byte[] part1 = TestData.Bytes(5 * 1024 * 1024, seed: 1);
        byte[] part2 = TestData.Bytes(1000, seed: 2);

        MultipartUpload upload = (await _files.StartMultipartUploadAsync(key, new MultipartUploadOptions { ContentType = "video/mp4" })).Ok();
        var parts = new List<UploadedPart>();
        foreach ((int number, byte[] bytes) in new[] { (2, part2), (1, part1) })
        {
            PresignedRequest url = (await _files.CreateUploadPartUrlAsync(upload, number, TimeSpan.FromMinutes(5))).Ok();
            using HttpResponseMessage response = await SendAsync(url, bytes);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            parts.Add(new UploadedPart(number, response.Headers.ETag!.Tag));
        }

        FileReference reference = (await _files.CompleteMultipartUploadAsync(upload, parts)).Ok();

        reference.Key.Should().Be(key);
        (await TestData.ReadAllAsync(_files, key)).Should().Equal([.. part1, .. part2]);
        (await _files.GetPropertiesAsync(key)).Ok().ContentType.Should().Be("video/mp4");
    }

    [Fact]
    public async Task An_aborted_or_unknown_multipart_upload_aborts_cleanly()
    {
        MultipartUpload upload = (await _files.StartMultipartUploadAsync(TestData.UniqueKey())).Ok();

        (await _files.AbortMultipartUploadAsync(upload)).IsSuccess.Should().BeTrue();
        (await _files.AbortMultipartUploadAsync(upload)).IsSuccess.Should().BeTrue();
        (await _files.CompleteMultipartUploadAsync(upload, [new UploadedPart(1, "\"x\"")])).Error.Code
            .Should().Be(StorageErrorCodes.NotFound);
    }

    private static async Task<HttpResponseMessage> SendAsync(PresignedRequest request, byte[] body, string? overrideContentType = null)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url) { Content = new ByteArrayContent(body) };
        foreach ((string name, string value) in request.Headers)
        {
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(overrideContentType ?? value);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return await Http.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> PostAsync(PresignedPost form, string contentType, byte[] file)
    {
        using var body = new MultipartFormDataContent();
        foreach ((string name, string value) in form.Fields)
        {
            body.Add(new StringContent(value), name);
        }

        body.Add(new StringContent(contentType), "Content-Type");
        body.Add(new ByteArrayContent(file), "file", "upload.bin");
        return await Http.PostAsync(form.Url, body);
    }
}
