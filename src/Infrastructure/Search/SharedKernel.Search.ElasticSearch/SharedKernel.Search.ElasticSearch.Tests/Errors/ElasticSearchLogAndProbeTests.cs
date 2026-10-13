using System.Reflection;
using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Serialization;
using Elastic.Transport;
using HttpMethod = System.Net.Http.HttpMethod;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Health;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Diagnostics;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Logging;
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.Errors;

/// <summary>
/// Every classified failure logs once, at Warning, with the exception passed; no log template carries a free-text
/// reason, URL or error message; the readiness probe degrades instead of throwing yet lets the caller's
/// cancellation through; provisioning faults keep their classification.
/// </summary>
public sealed class ElasticSearchLogAndProbeTests
{
    private const int EngineFault = LoggingEventIdRanges.Search + 222;
    private const int OperationFaulted = LoggingEventIdRanges.Search + 225;
    private const int CursorReadFaulted = LoggingEventIdRanges.Search + 230;
    private const int PointInTimeCloseFailed = LoggingEventIdRanges.Search + 215;
    private const int ProbeDegraded = LoggingEventIdRanges.Search + 219;
    private const int RequestRejected = LoggingEventIdRanges.Search + 209;

    [Fact]
    public async Task ClassifiedResponseFailure_LogsOnce_AtWarning_WithTheTransportException()
    {
        var harness = EsFaultHarness.Failing(CannedReply.Error(503, EsFaultHarness.Sentinel));

        await harness.Index.CountAsync(null, EsOperations.Tenant);

        var record = harness.IndexLog.Records.ShouldHaveLogged(new EventId(EngineFault), LogLevel.Warning);
        record.Exception.Should().BeAssignableTo<TransportException>("the exception the transport captured reaches the log");
        harness.IndexLog.Records.ShouldHaveLoggedCount(new EventId(EngineFault), 1);
    }

    [Fact]
    public async Task ClassifiedThrownFault_LogsTheExceptionInstance_UnderOperationFaulted()
    {
        // A response-shaped fault goes through 9222; a fault the client throws (a serializer fault) through 9225.
        var broken = BrokenSerializerHarness(CannedReply.Ok(SearchBody));
        var result = await broken.Index.SearchAsync(SearchRequest.Default, EsOperations.Tenant);

        result.Error.Code.Should().Be("search.elasticsearch.source_serializer_context_missing");
        var record = broken.IndexLog.Records.ShouldHaveLogged(new EventId(OperationFaulted), LogLevel.Warning);
        record.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task ReadCursor_OnAMissingPointInTime_LogsCursorReadFaulted_WithTheException()
    {
        var token = await EsFaultHarness.OpenHealthyCursorAsync();
        var harness = EsFaultHarness.Failing(new CannedReply(
            System.Net.HttpStatusCode.NotFound,
            "{\"error\":{\"root_cause\":[{\"type\":\"search_context_missing_exception\",\"reason\":\"" + EsFaultHarness.Sentinel
            + "\"}],\"type\":\"search_phase_execution_exception\",\"reason\":\"all shards failed\"},\"status\":404}"));

        var result = await harness.Cursor.ReadCursorAsync(token);

        result.Error.Code.Should().Be("search.elasticsearch.cursor_expired");
        var record = harness.CursorLog.Records.ShouldHaveLogged(new EventId(CursorReadFaulted), LogLevel.Warning);
        record.Exception.Should().NotBeNull();
        record.Message.Should().NotContain(EsFaultHarness.Sentinel);
    }

    [Fact]
    public async Task ReadCursor_WithAMalformedToken_IsInvalidCursor_NamingNothingOfTheToken_AndLogsTheDecoderException()
    {
        var harness = EsFaultHarness.Failing(CannedReply.Ok());

        var result = await harness.Cursor.ReadCursorAsync(new SearchCursor("not-base64!!SENTINEL-token", DateTimeOffset.UnixEpoch));

        result.Error.Code.Should().Be("search.elasticsearch.invalid_cursor");
        result.Error.Message.Should().NotContain("SENTINEL").And.NotContain("base64");
        harness.CursorLog.Records.ShouldHaveLoggedWithException(new EventId(CursorReadFaulted), typeof(FormatException));
        harness.Invoker.Requests.Should().BeEmpty("a malformed token is rejected before any I/O");
    }

    [Fact]
    public async Task PointInTimeCloseFailure_InTheEnumeration_LogsTheExceptionAtWarning()
    {
        var invoker = new ScriptedRequestInvoker(request => request.Method == HttpMethod.Delete
            ? CannedReply.Error(500, EsFaultHarness.Sentinel)
            : request.RequestUri!.PathAndQuery.Contains("_pit", StringComparison.Ordinal)
                ? CannedReply.Ok("{\"id\":\"pit-1\"}")
                : CannedReply.Ok(EmptyHits));
        var harness = new EsFaultHarness(invoker);

        await foreach (var _ in harness.Index.EnumerateAsync(null, EsOperations.Tenant, batchSize: 10))
        {
        }

        var record = harness.IndexLog.Records.ShouldHaveLogged(new EventId(PointInTimeCloseFailed), LogLevel.Warning);
        record.Exception.Should().NotBeNull();
        record.Message.Should().NotContain(EsFaultHarness.Sentinel);
    }

    [Fact]
    public async Task PreflightRejection_LogsTheErrorTypeAndCode_NotItsMessage()
    {
        var logger = new InMemoryLogger<SharedKernel.Search.ElasticSearch.Index.ElasticSearchIndex<TestProduct>>();
        var index = new SharedKernel.Search.ElasticSearch.Index.ElasticSearchIndex<TestProduct>(
            client: null!,
            TestProductIndexDefinitions.Standard("products"),
            "products",
            new SharedKernel.Search.ElasticSearch.Options.ElasticSearchOptions { Nodes = ["http://es-host.sentinel.invalid:9200"] },
            new SharedKernel.Testing.Clocks.FakeClock(),
            logger,
            SharedKernel.Search.ElasticSearch.Tests.Support.EsTelemetry.Create());
        var request = SearchRequest.Default with { Page = 0 };

        var result = await index.SearchAsync(request, EsOperations.Tenant);

        result.IsFailure.Should().BeTrue();
        var record = logger.Records.ShouldHaveLogged(new EventId(RequestRejected));
        record.TryGetProperty("ErrorCode", out var code).Should().BeTrue();
        code.Should().Be(result.Error.Code);
        record.TryGetProperty("ErrorType", out var type).Should().BeTrue();
        type.Should().Be(result.Error.Type.ToString());
        record.Message.Should().NotContain(result.Error.Message);
    }

    [Fact]
    public void NoLogTemplate_CarriesAFreeTextReason_Url_OrErrorMessage_AndTheNewIdIsInTheSubBlock()
    {
        var methods = typeof(ElasticSearchLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(pair => pair.Attribute is not null)
            .ToList();

        methods.Should().NotBeEmpty();
        foreach (var (method, attribute) in methods)
        {
            foreach (var banned in new[] { "{Reason}", "{Url}", "{Error}", "{ErrorMessage}" })
            {
                attribute!.Message.Should().NotContain(banned, $"{method.Name} must not log free text");
            }
        }

        var ids = methods.Select(pair => pair.Attribute!.EventId).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => id >= LoggingEventIdRanges.Search + 200 && id <= LoggingEventIdRanges.Search + 299);
        ids.Should().Contain(CursorReadFaulted);
    }

    [Fact]
    public async Task Probe_WhenTheClusterAnswers503_Degrades_NotThrows_AndLogsTheException()
    {
        var harness = EsFaultHarness.Failing(CannedReply.Error(503, EsFaultHarness.Sentinel));

        var result = await harness.Provisioner.ProbeAsync("products");

        result.IsSuccess.Should().BeTrue("a failed measurement degrades the report instead of throwing");
        result.Value.Reachable.Should().BeFalse();
        var record = harness.ProvisionerLog.Records.ShouldHaveLogged(new EventId(ProbeDegraded), LogLevel.Warning);
        record.Exception.Should().NotBeNull();
        record.Message.Should().NotContain(EsFaultHarness.Sentinel);
    }

    [Fact]
    public async Task Probe_WhenTheCallerCancelsMidProbe_Propagates()
    {
        var invoker = new ScriptedRequestInvoker(CannedReply.Hang());
        var harness = new EsFaultHarness(invoker);
        using var cts = new CancellationTokenSource();

        var pending = harness.Provisioner.ProbeAsync("products", cts.Token);
        await invoker.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        var act = () => pending;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UnhealthyReadinessReport_FromAFailedProbe_CarriesNoEngineText()
    {
        var harness = EsFaultHarness.Failing(CannedReply.Error(503, EsFaultHarness.Sentinel));
        var probe = new SearchIndexReadinessProbe("elasticsearch", "products", harness.Provisioner.ProbeAsync);

        var report = await probe.ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Unhealthy);
        report.Description.Should().NotContain(EsFaultHarness.Sentinel).And.NotContain(EsFaultHarness.HostSentinel);
    }

    [Fact]
    public async Task EnsureIndex_PutMapping400_IsIndexDefinitionConflict_WithThePlaceholderField_AndNoEngineText()
    {
        var invoker = new ScriptedRequestInvoker(request =>
            request.Method == HttpMethod.Head ? new CannedReply(System.Net.HttpStatusCode.OK, string.Empty)
            : request.Method == HttpMethod.Put && request.RequestUri!.PathAndQuery.Contains("_mapping", StringComparison.Ordinal)
                ? CannedReply.Error(400, EsFaultHarness.Sentinel)
                : CannedReply.Ok());
        var harness = new EsFaultHarness(invoker);

        var result = await harness.Provisioner.EnsureIndexAsync(harness.Definition);

        result.Error.Code.Should().Be("search.index_definition_conflict");
        result.Error.Message.Should().Contain("(unknown)").And.NotContain(EsFaultHarness.Sentinel);
    }

    [Fact]
    public async Task EnsureIndex_PutMapping503_IsUnreachable_NotAConflict()
    {
        var invoker = new ScriptedRequestInvoker(request =>
            request.Method == HttpMethod.Head ? new CannedReply(System.Net.HttpStatusCode.OK, string.Empty)
            : CannedReply.Error(503, EsFaultHarness.Sentinel));
        var harness = new EsFaultHarness(invoker);

        var result = await harness.Provisioner.EnsureIndexAsync(harness.Definition);

        result.Error.Code.Should().Be(SearchErrorCodes.Unavailable);
    }

    [Theory]
    [InlineData(503, SearchErrorCodes.Unavailable)]
    [InlineData(401, SearchErrorCodes.EngineUnauthorized)]
    [InlineData(400, "search.cutover_failed")]
    [InlineData(500, "search.cutover_failed")]
    public async Task Cutover_AliasUpdateFault_KeepsOutageClassification_ElseNamesTheFailedStep(int status, string expectedCode)
    {
        var harness = EsFaultHarness.Failing(CannedReply.Error(status, EsFaultHarness.Sentinel));

        var result = await harness.Provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products" });

        result.Error.Code.Should().Be(expectedCode);
        result.Error.Message.Should().NotContain(EsFaultHarness.Sentinel).And.NotContain(EsFaultHarness.HostSentinel);
        if (expectedCode == "search.cutover_failed")
        {
            result.Error.Message.Should().Contain("update_aliases");
        }
    }

    [Fact]
    public async Task Cutover_DeleteStagingFault_NamesThatStep()
    {
        var invoker = new ScriptedRequestInvoker(request =>
            request.Method == HttpMethod.Delete ? CannedReply.Error(500, EsFaultHarness.Sentinel) : CannedReply.Ok());
        var harness = new EsFaultHarness(invoker);

        var result = await harness.Provisioner.CutoverAsync(new IndexCutoverRequest
        {
            StagingIndexName = "products-staging",
            LiveIndexName = "products",
            DeleteStagingAfterCutover = true,
        });

        result.Error.Code.Should().Be("search.cutover_failed");
        result.Error.Message.Should().Contain("delete_staging").And.NotContain(EsFaultHarness.Sentinel);
    }

    [Fact]
    public async Task SerializerContextMissing_OnSearch_IsTheNamedError_NotAnEngineFault()
    {
        var harness = BrokenSerializerHarness(CannedReply.Ok(SearchBody));

        var result = await harness.Index.SearchAsync(SearchRequest.Default, EsOperations.Tenant);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.elasticsearch.source_serializer_context_missing");
        result.Error.Message.Should().Contain(nameof(TestProduct)).And.NotContain("JsonTypeInfo");
    }

    [Fact]
    public async Task SerializerContextMissing_OnReadCursor_IsNotCursorExpired()
    {
        var token = await EsFaultHarness.OpenHealthyCursorAsync();
        var harness = BrokenSerializerHarness(CannedReply.Ok(SearchBody));

        var result = await harness.Cursor.ReadCursorAsync(token);

        result.Error.Code.Should().Be("search.elasticsearch.source_serializer_context_missing");
    }

    private const string EmptyHits =
        "{\"took\":1,\"timed_out\":false,\"_shards\":{\"total\":1,\"successful\":1,\"skipped\":0,\"failed\":0},"
        + "\"hits\":{\"total\":{\"value\":0,\"relation\":\"eq\"},\"hits\":[]}}";

    private const string SearchBody =
        "{\"took\":1,\"timed_out\":false,\"_shards\":{\"total\":1,\"successful\":1,\"skipped\":0,\"failed\":0},"
        + "\"hits\":{\"total\":{\"value\":1,\"relation\":\"eq\"},\"hits\":[{\"_index\":\"products\",\"_id\":\"doc-1\","
        + "\"_source\":{\"documentId\":\"doc-1\"},\"sort\":[1]}]}}";

    /// <summary>
    /// A harness whose client has a source-generated serializer context that lacks <see cref="TestProduct"/> —
    /// the condition a trimmed consumer that forgot <c>.WithSourceSerializerContext(...)</c> (or left a type out
    /// of it) is in.
    /// </summary>
    private static EsFaultHarness BrokenSerializerHarness(CannedReply reply)
    {
        var invoker = new ScriptedRequestInvoker(request =>
            request.RequestUri!.PathAndQuery.Contains("_pit", StringComparison.Ordinal)
                ? CannedReply.Ok("{\"id\":\"pit-1\"}")
                : reply);
        var client = new ElasticsearchClient(new ElasticsearchClientSettings(
            new SingleNodePool(new Uri("http://es-host.sentinel.invalid:9200")),
            new HttpRequestInvoker((_, _) => invoker),
            (_, settings) => new DefaultSourceSerializer(settings, EmptyJsonContext.Default, _ => { })).DisablePing());
        return new EsFaultHarness(invoker, client: client);
    }
}
