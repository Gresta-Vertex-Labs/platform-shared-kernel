using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Logging;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Logging;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.Meilisearch.Tests.Errors;

/// <summary>
/// Classify or rethrow, cancellation, logs, probes and provisioning faults for the Meilisearch provider. Only a
/// recognised engine or transport fault becomes a <c>Result</c> failure; anything else rethrows with its stack and
/// still completes the span and the histogram; the caller's cancellation always propagates; every classified
/// failure logs once with the exception and no free text.
/// </summary>
public sealed class MeilisearchRethrowAndLogTests
{
    private const int ClientConfigured = LoggingEventIdRanges.Search + 100;
    private const int RequestRejected = LoggingEventIdRanges.Search + 110;
    private const int ProbeDegraded = LoggingEventIdRanges.Search + 116;
    private const int OperationFaulted = LoggingEventIdRanges.Search + 123;
    private const int TenantTokenFailed = LoggingEventIdRanges.Search + 128;
    private const int CutoverStepFailed = LoggingEventIdRanges.Search + 129;

    public static TheoryData<string> OperationNames => MeiliOperations.Names;

    [Fact]
    public async Task UnclassifiedException_Rethrows_TheSameInstance_WithItsStack()
    {
        var original = new InvalidOperationException("defect in the stack");
        var handler = new ScriptedHttpHandler(HttpReply.Throws(original));
        var harness = new MeiliFaultHarness(handler);

        var act = () => harness.Index.CountAsync(null, MeiliOperations.Tenant);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(original);
        handler.Requests.Should().NotBeEmpty();
        harness.IndexLog.Records.Should().BeEmpty("a rethrown, unclassified exception is not logged by the provider; the caller's handler logs it once");
    }

    [Fact]
    public async Task UntranslatableFilterNode_ThrowsSwitchExpressionException_BeforeAnyIo()
    {
        var constructor = typeof(SearchValue).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(SearchValueKind), typeof(string), typeof(long), typeof(double), typeof(bool), typeof(DateTimeOffset)])!;
        var rogue = (SearchValue)constructor.Invoke([(SearchValueKind)99, null, 0L, 0d, false, default(DateTimeOffset)]);
        var handler = new ScriptedHttpHandler(HttpReply.Ok());
        var harness = new MeiliFaultHarness(handler);
        var request = SearchRequest.Default with { Filter = SearchFilter.Eq("status", rogue) };

        var act = () => harness.Index.SearchAsync(request, MeiliOperations.Tenant);

        var thrown = await act.Should().ThrowAsync<SwitchExpressionException>();
        thrown.Which.StackTrace.Should().Contain("MeilisearchFilterCompiler", "the original stack is preserved");
        handler.Requests.Should().BeEmpty("a defect in translation must not reach the engine");
    }

    [Fact]
    public async Task RethrownException_StillCompletesTheSpan_WithTheTypeNameAsDescription_AndTheHistogram()
    {
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Throws(new InvalidOperationException("never a description"))));
        // A distinct index name keeps parallel telemetry tests out of this assertion.
        var definition = new SearchIndexDefinitionBuilder("products-rethrow-telemetry")
            .Field("name", SearchFieldKind.Text, searchable: true).Build().Value;
        var index = new SharedKernel.Search.Meilisearch.Index.MeilisearchIndex<TestProduct>(
            harness.Client, definition, harness.Options, new SharedKernel.Testing.Clocks.FakeClock(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SharedKernel.Search.Meilisearch.Index.MeilisearchIndex<TestProduct>>.Instance);

        var act = () => index.SearchAsync(SearchRequest.Default, TenantScope.Global);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var span = spans.RecordedActivities.Single(a => (string?)a.GetTagItem(SearchWellKnown.IndexTagName) == "products-rethrow-telemetry");
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be(nameof(InvalidOperationException)).And.NotContain("never a description");
        span.GetTagItem("error.type").Should().Be(typeof(InvalidOperationException).FullName);
        var measurement = metrics.MeasurementsOf(SearchWellKnown.OperationDurationMetricName)
            .Single(m => m.TryGetTag(SearchWellKnown.IndexTagName, out var value) && (string?)value == "products-rethrow-telemetry");
        measurement.TryGetTag("error.type", out var errorType).Should().BeTrue();
        errorType.Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task CancellingTheCallersToken_InFlight_PropagatesOperationCanceled_AndLogsNoFailure(string operationName)
    {
        var handler = new ScriptedHttpHandler(HttpReply.HangUntilCancelled());
        var harness = new MeiliFaultHarness(handler);
        using var cts = new CancellationTokenSource();

        var pending = MeiliOperations.All[operationName](harness, cts.Token);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        var act = () => pending;
        await act.Should().ThrowAsync<OperationCanceledException>(
            $"'{operationName}': the caller's cancellation is an instruction, not an engine failure");
        harness.AllLogs.Should().NotContain(
            record => record.LogLevel >= LogLevel.Warning,
            "a cancelled request must not inflate the failure signal");
    }

    [Fact]
    public async Task HttpClientTimeout_WithTheTokenNotCancelled_IsSearchTimeout()
    {
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Throws(new TaskCanceledException("client timeout"))));

        var result = await harness.Index.CountAsync(null, MeiliOperations.Tenant);

        result.Error.Code.Should().Be("search.timeout");
        result.Error.Type.Should().Be(ErrorType.Timeout);
    }

    [Fact]
    public async Task ClassifiedFailure_LogsOnce_AtWarning_WithTheException()
    {
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Throws(new HttpRequestException("refused"))));

        await harness.Index.CountAsync(null, MeiliOperations.Tenant);

        var record = harness.IndexLog.Records.ShouldHaveLogged(new EventId(OperationFaulted), LogLevel.Warning);
        record.Exception.Should().NotBeNull();
        harness.IndexLog.Records.ShouldHaveLoggedCount(new EventId(OperationFaulted), 1);
        record.Message.Should().NotContain("refused");
    }

    [Fact]
    public async Task PreflightRejection_LogsTheErrorTypeAndCode_NotItsMessage()
    {
        var handler = new ScriptedHttpHandler(HttpReply.Ok());
        var harness = new MeiliFaultHarness(handler);

        var result = await harness.Index.SearchAsync(SearchRequest.Default with { Page = 0 }, MeiliOperations.Tenant);

        result.IsFailure.Should().BeTrue();
        handler.Requests.Should().BeEmpty("a pre-flight rejection performs no I/O");
        var record = harness.IndexLog.Records.ShouldHaveLogged(new EventId(RequestRejected));
        record.TryGetProperty("ErrorCode", out var code).Should().BeTrue();
        code.Should().Be(result.Error.Code);
        record.TryGetProperty("ErrorType", out var type).Should().BeTrue();
        type.Should().Be(result.Error.Type.ToString());
        record.Message.Should().NotContain(result.Error.Message);
    }

    [Fact]
    public void NoLogTemplate_CarriesAFreeTextReason_Url_OrErrorMessage_AndTheNewIdsAreInTheSubBlock()
    {
        var methods = typeof(MeilisearchLog)
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
        ids.Should().OnlyContain(id => id >= LoggingEventIdRanges.Search + 100 && id <= LoggingEventIdRanges.Search + 199);
        ids.Should().Contain([TenantTokenFailed, CutoverStepFailed]);
        methods.Single(pair => pair.Attribute!.EventId == ClientConfigured).Method.GetParameters()
            .Select(p => p.Name).Should().NotContain("url", "L5: no URLs or hosts in a log");
    }

    [Fact]
    public async Task Probe_WhenTheEngineIsUnreachable_Degrades_NotThrows_AndLogsTheFirstException()
    {
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Throws(new HttpRequestException("refused"))));

        var result = await harness.Provisioner.ProbeAsync("products");

        result.IsSuccess.Should().BeTrue("a failed measurement degrades the report instead of throwing");
        result.Value.Reachable.Should().BeFalse();
        var record = harness.ProvisionerLog.Records.ShouldHaveLogged(new EventId(ProbeDegraded), LogLevel.Warning);
        record.Exception.Should().NotBeNull();
        record.Message.Should().NotContain("refused");
    }

    [Fact]
    public async Task Probe_WhenTheCallerCancelsMidProbe_Propagates()
    {
        var handler = new ScriptedHttpHandler(HttpReply.HangUntilCancelled());
        var harness = new MeiliFaultHarness(handler);
        using var cts = new CancellationTokenSource();

        var pending = harness.Provisioner.ProbeAsync("products", cts.Token);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        var act = () => pending;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UnhealthyReadinessReport_FromAFailedProbe_CarriesNoEngineText()
    {
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.ApiError(503, "internal", MeiliFaultHarness.Sentinel)));
        var probe = new SearchIndexReadinessProbe("meilisearch", "products", harness.Provisioner.ProbeAsync);

        var report = await probe.ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Unhealthy);
        report.Description.Should().NotContain(MeiliFaultHarness.Sentinel).And.NotContain(MeiliFaultHarness.HostSentinel);
    }

    [Theory]
    [InlineData("swap", "search.cutover_failed")]
    public async Task Cutover_SwapRejected_NamesTheStep_AndNeverTheEngineText(string step, string expectedCode)
    {
        var handler = new ScriptedHttpHandler(request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("swap-indexes", StringComparison.Ordinal)
            ? HttpReply.ApiError(400, "invalid_swap_indexes", MeiliFaultHarness.Sentinel)
            : HttpReply.Ok("{\"uid\":\"x\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}"));
        var harness = new MeiliFaultHarness(handler);

        var result = await harness.Provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products" });

        result.Error.Code.Should().Be(expectedCode);
        result.Error.Message.Should().Contain(step).And.NotContain(MeiliFaultHarness.Sentinel);
        var record = harness.ProvisionerLog.Records.ShouldHaveLogged(new EventId(CutoverStepFailed), LogLevel.Warning);
        record.TryGetProperty("Step", out var loggedStep).Should().BeTrue();
        loggedStep.Should().Be(step);
    }

    [Fact]
    public async Task Cutover_WithAnUnreachableEngine_IsUnreachable_AndLogsTheException()
    {
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Throws(new HttpRequestException(MeiliFaultHarness.HostSentinel))));

        var result = await harness.Provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products" });

        // The first step (does the live index exist) already meets the outage and reports it unchanged.
        result.Error.Code.Should().Be("search.unreachable");
        result.Error.Message.Should().NotContain(MeiliFaultHarness.HostSentinel);
        harness.ProvisionerLog.Records.ShouldHaveLogged(new EventId(OperationFaulted), LogLevel.Warning).Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task Cutover_WithAnUnclassifiedException_Rethrows()
    {
        var original = new InvalidOperationException("defect");
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Throws(original)));

        var act = () => harness.Provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products" });

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(original);
    }

    [Fact]
    public async Task EnsureIndex_WithAnOutage_IsUnreachable_NotAThrownException()
    {
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Throws(new HttpRequestException("refused"))));

        var result = await harness.Provisioner.EnsureIndexAsync(harness.Definition);

        result.Error.Code.Should().Be("search.unreachable");
    }

    [Fact]
    public async Task TenantToken_WhenSigningFails_IsIssuanceFailed_WithFixedText_AndLogsTheException()
    {
        var harness = new MeiliFaultHarness(
            new ScriptedHttpHandler(HttpReply.Ok()),
            new SharedKernel.Search.Meilisearch.Options.MeilisearchOptions
            {
                Url = "http://" + MeiliFaultHarness.HostSentinel + ":7700",
                ApiKey = string.Empty,
                ApiKeyUid = "not-a-guid-SENTINEL",
            });

        var result = await harness.TokenIssuer.IssueAsync(
            TenantScope.For(TestTenants.TenantA), "tenantId", ["products"], TimeSpan.FromMinutes(5));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.meilisearch.tenant_token_issuance_failed");
        result.Error.Message.Should().NotContain("SENTINEL").And.NotContain(MeiliFaultHarness.HostSentinel);
        var record = harness.TokenLog.Records.ShouldHaveLogged(new EventId(TenantTokenFailed), LogLevel.Warning);
        record.Exception.Should().NotBeNull();
        record.Message.Should().NotContain("SENTINEL");
    }
}
