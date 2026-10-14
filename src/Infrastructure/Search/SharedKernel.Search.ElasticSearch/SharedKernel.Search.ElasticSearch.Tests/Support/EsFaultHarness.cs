using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Diagnostics;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Search.ElasticSearch.Suggest;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.Support;

/// <summary>
/// Every ElasticSearch component over one <see cref="ScriptedRequestInvoker"/>: no container, no network, the
/// real client, so the invalid-response shapes the mapper classifies are the ones production sees.
/// </summary>
internal sealed class EsFaultHarness
{
    public const string Sentinel = "SENTINEL-reason-7f3a";
    public const string HostSentinel = "es-host.sentinel.invalid";
    public const string IndexName = "products";
    public const string SuggestField = "nameSuggest";

    public EsFaultHarness(ScriptedRequestInvoker invoker, string indexName = IndexName, ElasticsearchClient? client = null)
    {
        Invoker = invoker;
        Client = client ?? StubbedClient.Create(invoker);
        Definition = TestProductIndexDefinitions.Standard(indexName);
        var options = new ElasticSearchOptions { Nodes = ["http://" + HostSentinel + ":9200"] };
        Index = new ElasticSearchIndex<TestProduct>(Client, Definition, indexName, options, new FakeClock(), IndexLog, Telemetry);
        Provisioner = new ElasticSearchIndexProvisioner(
            Client,
            new ElasticSearchOptions { Nodes = options.Nodes, ProbeCacheSeconds = 0 },
            new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal) { [indexName] = Definition },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new FakeClock(),
            ProvisionerLog,
            Telemetry);
        Analytics = new ElasticSearchAnalytics<TestProduct>(Client, Definition, AnalyticsLog, Telemetry);
        Suggest = new ElasticSearchSuggestSearch<TestProduct>(Client, Definition, [SuggestField], options, SuggestLog, Telemetry);
        Cursor = new ElasticSearchCursorSearch<TestProduct>(Client, Definition, new FakeClock(), CursorLog, Telemetry);
    }

    public SearchTelemetry Telemetry { get; } = EsTelemetry.Create();

    public ScriptedRequestInvoker Invoker { get; }

    public ElasticsearchClient Client { get; }

    public SearchIndexDefinition Definition { get; }

    public InMemoryLogger<ElasticSearchIndex<TestProduct>> IndexLog { get; } = new();

    public InMemoryLogger<ElasticSearchIndexProvisioner> ProvisionerLog { get; } = new();

    public InMemoryLogger<ElasticSearchAnalytics<TestProduct>> AnalyticsLog { get; } = new();

    public InMemoryLogger<ElasticSearchSuggestSearch<TestProduct>> SuggestLog { get; } = new();

    public InMemoryLogger<ElasticSearchCursorSearch<TestProduct>> CursorLog { get; } = new();

    public ElasticSearchIndex<TestProduct> Index { get; }

    public ElasticSearchIndexProvisioner Provisioner { get; }

    public ElasticSearchAnalytics<TestProduct> Analytics { get; }

    public ElasticSearchSuggestSearch<TestProduct> Suggest { get; }

    public ElasticSearchCursorSearch<TestProduct> Cursor { get; }

    public IReadOnlyList<LogRecord> AllLogs =>
        [.. IndexLog.Records, .. ProvisionerLog.Records, .. AnalyticsLog.Records, .. SuggestLog.Records, .. CursorLog.Records];

    /// <summary>A harness whose every request is answered with <paramref name="reply"/>.</summary>
    public static EsFaultHarness Failing(CannedReply reply) => new(new ScriptedRequestInvoker(reply));

    /// <summary>
    /// A harness for the streaming and cursor members: opening a point-in-time and closing it succeed, every
    /// other request (the page read) is answered with <paramref name="pageReply"/>.
    /// </summary>
    public static EsFaultHarness WithOpenPointInTime(CannedReply pageReply) => new(new ScriptedRequestInvoker(request =>
        request.RequestUri!.PathAndQuery.Contains("_pit", StringComparison.Ordinal) ? CannedReply.Ok("{\"id\":\"pit-1\",\"succeeded\":true,\"num_freed\":1}") : pageReply));

    public static TestProduct Doc(string id = "doc-1") => new()
    {
        DocumentId = id,
        TenantId = TestTenants.TenantA.ToString(),
        Name = "name",
        Description = "description",
        Status = "active",
        Category = "category",
        Price = 1,
        Stock = 1,
        InStock = true,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>Opens a cursor on a healthy stub and returns its token, for the read and close paths.</summary>
    public static async Task<SearchCursor> OpenHealthyCursorAsync()
    {
        var healthy = WithOpenPointInTime(CannedReply.Ok());
        var opened = await healthy.Cursor.OpenCursorAsync(
            SearchRequest.Default, SharedKernel.Execution.Tenancy.TenantScope.For(TestTenants.TenantA), TimeSpan.FromMinutes(1));
        return opened.Value;
    }
}
