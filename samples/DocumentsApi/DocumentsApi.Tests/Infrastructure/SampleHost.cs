using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentsApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Tests.Infrastructure;

/// <summary>The DocumentsApi Program configured for one backend, plus helpers the scenarios share.</summary>
public sealed class SampleHost : WebApplicationFactory<Program>
{
    public static readonly TenantId Acme = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    public static readonly TenantId Globex = new(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

    public static readonly TenantId[] Tenants = [Acme, Globex];

    private readonly Dictionary<string, string?> _settings;

    public SampleHost(string backend, Dictionary<string, string?> settings)
    {
        Backend = backend;
        _settings = settings;
    }

    public string Backend { get; }

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>A client of the API acting for <paramref name="tenant"/>, <see cref="Acme"/> by default (ignored by shared stores).</summary>
    public HttpClient Api(TenantId? tenant = null)
    {
        HttpClient client = CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add(Stores.TenantHeader, (tenant ?? Acme).ToString());
        return client;
    }

    /// <summary>The store the API resolves for <paramref name="store"/> and <paramref name="tenant"/>.</summary>
    public IFileStorage Store(string store, TenantId? tenant = null)
    {
        IFileStorageFactory factory = Services.GetRequiredService<IFileStorageFactory>();
        return factory.IsTenantScoped(store) ? factory.GetTenantStore(store).ForTenant(tenant ?? Acme) : factory.GetStore(store);
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

    /// <summary>
    /// Reads the <c>errorCode</c> of a problem response — every error the API returns is
    /// <c>application/problem+json</c> and carries one.
    /// </summary>
    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/problem+json")
        {
            throw new InvalidOperationException(
                $"{(int)response.StatusCode} is not a problem response: {await response.Content.ReadAsStringAsync()}");
        }

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("errorCode", out JsonElement code) ? code.GetString() : null;
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
