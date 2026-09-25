using System.Net;
using DocumentsApi.Tests.Infrastructure;
using FluentAssertions;
using SharedKernel.Storage;
using static DocumentsApi.Tests.Infrastructure.SampleHost;
using Xunit.Abstractions;

namespace DocumentsApi.Tests;

[Collection(BackendsCollection.Name)]
public sealed class TenantAndHealthScenarios(Backends backends, ITestOutputHelper output)
{
    [Theory]
    [MemberData(nameof(Backends.AllBackends), MemberType = typeof(Backends))]
    public async Task One_tenant_can_never_reach_another_tenants_documents(string backend)
    {
        using HttpClient acme = backends[backend].Api(Acme);
        using HttpClient globex = backends[backend].Api(Globex);
        string key = SampleHost.NewKey("contract.pdf");
        (await acme.PutAsync($"/files/{Stores.Documents}/{key}", new ByteArrayContent([4, 2]))).EnsureSuccessStatusCode();

        using HttpResponseMessage read = await globex.GetAsync($"/files/{Stores.Documents}/{key}");
        using HttpResponseMessage traversal = await globex.GetAsync($"/files/{Stores.Documents}/..%2F{Acme}%2F{key}");
        FileListPage listed = await SampleHost.ReadAsync<FileListPage>(await globex.GetAsync($"/list/{Stores.Documents}"));
        using HttpResponseMessage delete = await globex.DeleteAsync($"/files/{Stores.Documents}/{key}");

        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        traversal.IsSuccessStatusCode.Should().BeFalse();
        listed.Items.Select(i => i.Key).Should().NotContain(key);
        delete.IsSuccessStatusCode.Should().BeTrue();
        (await acme.GetByteArrayAsync($"/files/{Stores.Documents}/{key}")).Should().Equal(4, 2);
    }

    [Theory]
    [MemberData(nameof(Backends.AllBackends), MemberType = typeof(Backends))]
    public async Task Tenant_objects_live_under_the_tenant_prefix_of_the_bucket(string backend)
    {
        using HttpClient acme = backends[backend].Api(Acme);
        string key = SampleHost.NewKey();
        (await acme.PutAsync($"/files/{Stores.Documents}/{key}", new ByteArrayContent([1]))).EnsureSuccessStatusCode();

        PresignedRequest link = await SampleHost.ReadAsync<PresignedRequest>(
            await acme.PostAsJsonAsync($"/links/{Stores.Documents}/download", new DownloadLinkRequest(key, 60)));

        link.Url.AbsolutePath.Should().Contain($"sharedkernel-samples/{Backends.RunId}/documents/tenants/{Acme}/{key}");
    }

    [Fact]
    public async Task A_tenant_store_without_a_tenant_is_refused()
    {
        using HttpClient anonymous = backends[Backends.MinIO].CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync($"/list/{Stores.Documents}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SampleHost.ErrorCodeAsync(response)).Should().Be("documents.tenant_required");
    }

    [Theory]
    [MemberData(nameof(Backends.AllBackends), MemberType = typeof(Backends))]
    public async Task Readiness_probes_every_store(string backend)
    {
        using HttpClient api = backends[backend].CreateClient();

        using HttpResponseMessage ready = await api.GetAsync("/health/ready");

        output.WriteLine(await ready.Content.ReadAsStringAsync());
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Public_bucket_objects_are_or_are_not_readable_anonymously()
    {
        if (!Backends.LiveAvailable)
        {
            return;
        }

        using HttpClient api = backends[Backends.Live].Api();
        string key = SampleHost.NewKey("logo.txt");
        (await api.PutAsync($"/files/{Stores.Assets}/{key}", new StringContent("hello"))).EnsureSuccessStatusCode();
        PresignedRequest link = await SampleHost.ReadAsync<PresignedRequest>(
            await api.PostAsJsonAsync($"/links/{Stores.Assets}/download", new DownloadLinkRequest(key, 60)));

        // The same object URL without the signature: tells whether the "public" bucket serves it anonymously.
        var anonymousUrl = new Uri(link.Url.GetLeftPart(UriPartial.Path));
        using var client = new HttpClient();
        using HttpResponseMessage response = await client.GetAsync(anonymousUrl);

        output.WriteLine($"Anonymous GET of a new object in the public bucket: {(int)response.StatusCode}");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Forbidden);
    }
}
