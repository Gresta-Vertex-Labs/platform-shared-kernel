using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Search.Meilisearch.Extensions;
using SharedKernel.Search.Meilisearch.Tests.Support;

namespace SharedKernel.Search.Meilisearch.Tests.Extensions;

/// <summary>
/// The client the DI extension builds must raise the SDK's typed errors. The SDK's own <c>(url, key)</c>
/// constructor installs a message handler that turns a failed HTTP call into <c>MeilisearchApiError</c>; its
/// <c>(HttpClient, key)</c> constructor does not, and without that handler a rejected POST (search, add
/// documents, swap) deserialises the error body as if it were the success payload and no failure is ever
/// reported. A local listener answers HTTP 400, so the whole production chain (factory client, primary handler)
/// is exercised.
/// </summary>
public sealed class MeilisearchNamedClientErrorHandlingTests
{
    [Fact]
    public async Task TheRegisteredClient_RaisesMeilisearchApiError_ForARejectedPost()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "application/json";
                    var body = Encoding.UTF8.GetBytes(HttpReply.ApiErrorBody("invalid_search_filter", "engine-text"));
                    await context.Response.OutputStream.WriteAsync(body);
                    context.Response.Close();
                }
                catch (Exception) when (!listener.IsListening)
                {
                    return;
                }
            }
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Search:Meilisearch:Url"] = $"http://127.0.0.1:{port}",
                ["Search:Meilisearch:ApiKey"] = "key",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelMeilisearchSearch(configuration);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<global::Meilisearch.MeilisearchClient>();

        var search = () => client.Index("products").SearchAsync<System.Text.Json.JsonElement>("q");
        var add = () => client.Index("products").AddDocumentsAsync(new[] { new { id = 1 } });

        (await search.Should().ThrowAsync<global::Meilisearch.MeilisearchApiError>()).Which.Code.Should().Be("invalid_search_filter");
        await add.Should().ThrowAsync<global::Meilisearch.MeilisearchApiError>();
        listener.Stop();
    }

    private static int FreePort()
    {
        var probe = TcpListener.Create(0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
