using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentsApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Tests.Infrastructure;

/// <summary>The DocumentsApi Program configured for one backend, plus helpers the scenarios share.</summary>
public sealed class SampleHost : WebApplicationFactory<Program>
{
    public static readonly string[] Tenants = ["acme", "globex"];

    private readonly Dictionary<string, string?> _settings;

    public SampleHost(string backend, Dictionary<string, string?> settings)
    {
        Backend = backend;
        _settings = settings;
    }

    public string Backend { get; }

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>A client of the API acting for <paramref name="tenant"/> (ignored by shared stores).</summary>
    public HttpClient Api(string tenant = "acme")
    {
        HttpClient client = CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add(Stores.TenantHeader, tenant);
        return client;
    }

    /// <summary>The store the API resolves for <paramref name="store"/> and <paramref name="tenant"/>.</summary>
    public IFileStorage Store(string store, string tenant = "acme")
    {
        IFileStorageFactory factory = Services.GetRequiredService<IFileStorageFactory>();
        return factory.IsTenantScoped(store) ? factory.GetTenantStore(store).ForTenant(tenant) : factory.GetStore(store);
    }

    /// <summary>Deletes everything this run wrote: every key under each store's run prefix, for every tenant used.</summary>
    public async Task CleanUpAsync()
    {
        var targets = new List<IFileStorage> { Store(Stores.Assets), Store(Stores.Archive) };
        targets.AddRange(Tenants.Select(t => Store(Stores.Documents, t)));

        foreach (IFileStorage store in targets)
        {
            var keys = new List<string>();
            await foreach (FileListItem item in store.ListAsync())
            {
                keys.Add(item.Key);
            }

            if (keys.Count > 0)
            {
                Result<BatchDeleteResult> deleted = await store.DeleteManyAsync(keys);
                if (deleted.IsFailure || !deleted.Value.IsComplete)
                {
                    throw new InvalidOperationException($"Cleanup of {Backend}/{store.StoreName}/{store.TenantId} left objects behind.");
                }
            }
        }
    }

    public static string NewKey(string name = "file.bin") => $"{Guid.NewGuid():N}/{name}";

    public static byte[] Bytes(int length, int seed = 1)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static string Sha256(byte[] bytes) => Convert.ToBase64String(SHA256.HashData(bytes));

    /// <summary>Reads the <c>errorCode</c> of a ProblemDetails response.</summary>
    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("errorCode", out JsonElement code) ? code.GetString()
            : body.RootElement.TryGetProperty("title", out JsonElement title) ? title.GetString()
            : null;
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach ((string key, string? value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
