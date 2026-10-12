using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Index;
using SharedKernel.Search.Meilisearch.Instant;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Search.Meilisearch.Provisioning;
using SharedKernel.Search.Meilisearch.Tenancy;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Search.Meilisearch.Tests.Support;

/// <summary>
/// Every Meilisearch component over one <see cref="ScriptedHttpHandler"/>: no container, no network, the real
/// SDK, so the throw shapes the mapper classifies are the ones production sees.
/// </summary>
internal sealed class MeiliFaultHarness
{
    public const string Sentinel = "SENTINEL-reason-4c9e";
    public const string HostSentinel = "meili-host.sentinel.invalid";

    public MeiliFaultHarness(ScriptedHttpHandler handler, MeilisearchOptions? options = null)
    {
        Handler = handler;
        Options = options ?? new MeilisearchOptions
        {
            Url = "http://" + HostSentinel + ":7700",
            ApiKey = "master-key",
            ApiKeyUid = "00000000-0000-0000-0000-000000000000",
            TaskWaitTimeoutSeconds = 1,
            TaskPollIntervalMilliseconds = 20,
        };
        var http = new HttpClient(new global::Meilisearch.MeilisearchMessageHandler(handler)) { BaseAddress = new Uri(Options.Url), Timeout = TimeSpan.FromSeconds(5) };
        Client = new global::Meilisearch.MeilisearchClient(http, Options.ApiKey);
        Definition = new SearchIndexDefinitionBuilder("products")
            .PrimaryKey("documentId")
            .TenantField("tenantId")
            .Field("tenantId", SearchFieldKind.Keyword, filterable: true)
            .Field("name", SearchFieldKind.Text, searchable: true)
            .Field("status", SearchFieldKind.Keyword, filterable: true, facetable: true)
            .Build()
            .Value;
        Index = new MeilisearchIndex<TestProduct>(Client, Definition, Options, new FakeClock(), IndexLog);
        Provisioner = new MeilisearchIndexProvisioner(
            Client,
            Options,
            new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal) { ["products"] = Definition },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            ProvisionerLog);
        Instant = new MeilisearchInstantSearch<TestProduct>(Client, Definition, InstantLog);
        TokenIssuer = new MeilisearchTenantTokenIssuer(Client, Options, new FakeClock(), TokenLog);
    }

    public ScriptedHttpHandler Handler { get; }

    public MeilisearchOptions Options { get; }

    public global::Meilisearch.MeilisearchClient Client { get; }

    public SearchIndexDefinition Definition { get; }

    public InMemoryLogger<MeilisearchIndex<TestProduct>> IndexLog { get; } = new();

    public InMemoryLogger<MeilisearchIndexProvisioner> ProvisionerLog { get; } = new();

    public InMemoryLogger<MeilisearchInstantSearch<TestProduct>> InstantLog { get; } = new();

    public InMemoryLogger<MeilisearchTenantTokenIssuer> TokenLog { get; } = new();

    public MeilisearchIndex<TestProduct> Index { get; }

    public MeilisearchIndexProvisioner Provisioner { get; }

    public MeilisearchInstantSearch<TestProduct> Instant { get; }

    public MeilisearchTenantTokenIssuer TokenIssuer { get; }

    public IReadOnlyList<LogRecord> AllLogs =>
        [.. IndexLog.Records, .. ProvisionerLog.Records, .. InstantLog.Records, .. TokenLog.Records];

    public static TestProduct Doc(string id = "doc-1") => new()
    {
        DocumentId = id,
        TenantId = "11111111-1111-1111-1111-111111111111",
        Name = "name",
        Description = "description",
        Status = "active",
        Category = "category",
        Price = 1,
        Stock = 1,
        InStock = true,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };
}
