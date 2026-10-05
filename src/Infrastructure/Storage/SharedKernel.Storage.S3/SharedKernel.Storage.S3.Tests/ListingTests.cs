using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage.S3.Tests.Infrastructure;

namespace SharedKernel.Storage.S3.Tests;

[Collection(MinioCollection.Name)]
public sealed class ListingTests : IAsyncLifetime
{
    private readonly ServiceProvider _host;
    private readonly IFileStorage _files;
    private readonly string _root = $"{Guid.NewGuid():N}/";

    public ListingTests(MinioFixture minio)
    {
        _host = minio.CreateHost();
        _files = _host.GetRequiredKeyedService<IFileStorage>("files");
    }

    public async Task InitializeAsync()
    {
        foreach (string key in new[] { "a.txt", "b.txt", "c.txt", "sub1/x.txt", "sub2/y.txt", "sub2/deep/z.txt" })
        {
            await _files.UploadAsync(_root + key, new MemoryStream([1, 2, 3]));
        }
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_recursive_listing_streams_every_object_in_key_order()
    {
        List<FileListItem> items = [];
        await foreach (FileListItem item in _files.ListAsync(_root))
        {
            items.Add(item);
        }

        items.Select(i => i.Key[_root.Length..]).Should().Equal("a.txt", "b.txt", "c.txt", "sub1/x.txt", "sub2/deep/z.txt", "sub2/y.txt");
        items.Should().AllSatisfy(i =>
        {
            i.ContentLength.Should().Be(3);
            i.ETag.Should().NotBeNullOrEmpty();
            i.LastModified.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task A_folder_listing_returns_direct_objects_and_sub_folders()
    {
        FileListPage page = (await _files.ListPageAsync(new FileListRequest { Prefix = _root, Recursive = false })).Ok();

        page.Items.Select(i => i.Key[_root.Length..]).Should().Equal("a.txt", "b.txt", "c.txt");
        page.Folders.Select(f => f[_root.Length..]).Should().Equal("sub1/", "sub2/");
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Pages_continue_from_the_previous_token()
    {
        var keys = new List<string>();
        string? token = null;
        int pages = 0;
        do
        {
            FileListPage page = (await _files.ListPageAsync(new FileListRequest { Prefix = _root, PageSize = 4, ContinuationToken = token })).Ok();
            keys.AddRange(page.Items.Select(i => i.Key));
            token = page.ContinuationToken;
            pages++;
        }
        while (token is not null);

        pages.Should().Be(2);
        keys.Should().HaveCount(6).And.OnlyHaveUniqueItems();
    }
}
