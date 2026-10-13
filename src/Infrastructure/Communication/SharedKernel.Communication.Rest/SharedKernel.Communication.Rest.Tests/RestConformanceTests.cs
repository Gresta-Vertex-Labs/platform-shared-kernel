using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SharedKernel.Communication.Rest.Internal;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Communication.Rest.Tests;

/// <summary>
/// The observability and error-code conformance of the REST client: its log events, that nothing is swallowed
/// silently, and the cross-domain contracts of the reverse maps. Every test runs a real registered client.
/// </summary>
public sealed class RestConformanceTests
{
    private const string Prefix = "SharedKernel:Communication:Clients:inventory:";

    private static readonly TenantId Tenant = TenantId.Parse("7f0c0f0e-5c1a-4a8e-9a55-2b3c4d5e6f70");

    private static readonly Dictionary<string, string?> NoRetries = new() { [Prefix + "Retry:MaxRetryAttempts"] = "0" };

    private static RestHarness Create(
        InMemoryLoggerFactory logs,
        IDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? more = null) =>
        new(settings, services: s =>
        {
            s.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(logs));
            more?.Invoke(s);
        });

    private static IReadOnlyList<LogRecord> Records(InMemoryLoggerFactory logs) =>
        [.. logs.Loggers.Values.SelectMany(l => l.Records)];

    /// <summary>The kernel's own records: the framework's HttpClient logging prints addresses and is not ours.</summary>
    private static IReadOnlyList<LogRecord> KernelRecords(InMemoryLoggerFactory logs) =>
        [.. Records(logs).Where(r => r.EventId.Id >= LoggingEventIdRanges.Communication)];

    private static HttpResponseMessage Text(HttpStatusCode status, string body, string mediaType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    // --- Event ids and templates ------------------------------------------------------------------------------

    [Fact]
    public void The_event_ids_are_pinned_and_live_in_the_rest_sub_block()
    {
        CommunicationRestLoggingEventIds.PropagationFailed.Should().Be(11200);
        CommunicationRestLoggingEventIds.ProblemDetailsUnreadable.Should().Be(11201);
        CommunicationRestLoggingEventIds.SuccessBodyUnreadable.Should().Be(11202);

        int[] ids = LoggerMessageIds(typeof(HttpClientResultExtensions).Assembly);
        ids.Should().BeEquivalentTo([11200, 11201, 11202]);
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Communication, 2));
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotContain(id => id >= 11300 && id <= 11399);
    }

    [Fact]
    public void The_rest_assembly_holds_no_static_logger_and_its_events_stay_in_range()
    {
        Assembly assembly = typeof(HttpClientResultExtensions).Assembly;

        assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(f => f.FieldType == typeof(ILogger))
            .Should().BeEmpty();
        LoggerMessageIds(assembly).Should().OnlyContain(id => id >= 11200 && id <= 11299);
    }

    private static int[] LoggerMessageIds(Assembly assembly) =>
        [.. assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(a => a.EventId)];

    // --- 11200: the propagation handler ------------------------------------------------------------------------

    [Fact]
    public async Task A_propagation_failure_logs_11200_with_its_exception_sends_the_request_and_keeps_a_supplied_header()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, more: s => s.Replace(ServiceDescriptor.Singleton<IRequestContextAccessor>(new ThrowingAccessor())));
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.OK);
        using var request = new HttpRequestMessage(HttpMethod.Get, "stock");
        request.Headers.Add(WellKnownHeaders.CorrelationId, "mine");

        Result result = await harness.Http.SendResultAsync(request);

        result.IsSuccess.Should().BeTrue();
        harness.Stub.Requests.Single().Header(WellKnownHeaders.CorrelationId).Should().Be("mine");
        LogRecord record = Records(logs).ShouldHaveLogged(new EventId(11200), LogLevel.Error);
        record.Exception.Should().BeOfType<InvalidOperationException>();
        Records(logs).ShouldHaveLoggedCount(new EventId(11200), 1);
        logs.ShouldHaveCategory(typeof(RequestContextPropagationHandler));
    }

    [Fact]
    public async Task A_retried_call_logs_a_propagation_failure_once()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, more: s => s.Replace(ServiceDescriptor.Singleton<IRequestContextAccessor>(new ThrowingAccessor())));
        harness.Stub.Respond(HttpMethod.Get, "/stock", (_, n) => new HttpResponseMessage(n == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        (await harness.Http.GetAsync("stock")).EnsureSuccessStatusCode();

        harness.Stub.Requests.Should().HaveCount(2);
        Records(logs).ShouldHaveLoggedCount(new EventId(11200), 1);
    }

    // --- 11201: the problem-details body -----------------------------------------------------------------------

    [Fact]
    public async Task A_malformed_problem_json_body_falls_back_to_the_status_and_logs_11201_once_with_its_exception()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, NoRetries);
        harness.Stub.Respond(HttpMethod.Get, "/stock", (_, _) => Text(HttpStatusCode.BadRequest, "{not json", "application/problem+json"));

        Result<StockLevel> result = await harness.Http.GetResultAsync("stock", InventoryJson.Default.StockLevel);

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Validation, Code = CommunicationErrorCodes.ForHttpStatus(400) });
        LogRecord record = Records(logs).ShouldHaveLogged(new EventId(11201), LogLevel.Warning);
        record.Exception.Should().BeAssignableTo<System.Text.Json.JsonException>();
        Records(logs).ShouldHaveLoggedCount(new EventId(11201), 1);
        record.Message.Should().Contain("400");
    }

    [Fact]
    public async Task An_html_gateway_body_is_unavailable_with_the_status_code_and_logs_nothing()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, NoRetries);
        harness.Stub.Respond(HttpMethod.Get, "/stock", (_, _) => Text(HttpStatusCode.ServiceUnavailable, "<html>bad gateway</html>", "text/html"));

        Result<StockLevel> result = await harness.Http.GetResultAsync("stock", InventoryJson.Default.StockLevel);

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = "http.503" });
        Records(logs).Where(r => r.EventId.Id >= 11200).Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_error_body_logs_nothing()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, NoRetries);
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.NotFound);

        Result<StockLevel> result = await harness.Http.GetResultAsync("stock", InventoryJson.Default.StockLevel);

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.NotFound, Code = "http.404" });
        Records(logs).Where(r => r.EventId.Id >= 11200).Should().BeEmpty();
    }

    [Fact]
    public async Task The_callers_cancellation_during_the_body_read_propagates_and_logs_nothing()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, NoRetries);
        using var cancellation = new CancellationTokenSource();
        harness.Stub.Respond(HttpMethod.Get, "/stock", (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new CancelOnReadContent(cancellation) };
            response.Content.Headers.ContentType = new("application/problem+json");
            return response;
        });

        Func<Task> send = () => harness.Http.GetResultAsync("stock", InventoryJson.Default.StockLevel, cancellation.Token);

        await send.Should().ThrowAsync<OperationCanceledException>();
        Records(logs).Where(r => r.EventId.Id >= 11200).Should().BeEmpty();
    }

    [Fact]
    public async Task A_response_that_did_not_come_through_a_registered_client_does_not_throw()
    {
        using var response = Text(HttpStatusCode.BadRequest, "{not json", "application/problem+json");

        Result result = await response.ToResultAsync();

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Validation, Code = "http.400" });
    }

    // --- 11202: the success body --------------------------------------------------------------------------------

    [Fact]
    public async Task A_success_body_that_is_not_json_for_the_type_is_invalid_body_and_logs_11202_once_with_its_exception()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, NoRetries);
        harness.Stub.Respond(HttpMethod.Get, "/stock", (_, _) => Text(HttpStatusCode.OK, "<html>not json</html>", "application/json"));

        Result<StockLevel> result = await harness.Http.GetResultAsync("stock", InventoryJson.Default.StockLevel);

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unexpected, Code = CommunicationErrorCodes.InvalidBody });
        LogRecord record = Records(logs).ShouldHaveLogged(new EventId(11202), LogLevel.Warning);
        record.Exception.Should().BeAssignableTo<System.Text.Json.JsonException>();
        record.Message.Should().Contain(typeof(StockLevel).FullName!);
        Records(logs).ShouldHaveLoggedCount(new EventId(11202), 1);
    }

    [Fact]
    public async Task An_empty_success_body_is_empty_body_and_logs_nothing()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, NoRetries);
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.NoContent);

        Result<StockLevel> result = await harness.Http.GetResultAsync("stock", InventoryJson.Default.StockLevel);

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unexpected, Code = CommunicationErrorCodes.EmptyBody });
        Records(logs).Where(r => r.EventId.Id >= 11200).Should().BeEmpty();
    }

    // --- Conformance of everything the package logs -------------------------------------------------------------

    [Fact]
    public async Task Every_event_follows_the_log_conventions_and_leaks_no_caller()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, NoRetries, s => s.Replace(ServiceDescriptor.Singleton<IRequestContextAccessor>(new ThrowingAccessor())));
        harness.Stub.Respond(HttpMethod.Get, "/problem", (_, _) => Text(HttpStatusCode.BadRequest, "{not json", "application/problem+json"));
        harness.Stub.Respond(HttpMethod.Get, "/body", (_, _) => Text(HttpStatusCode.OK, "nope", "application/json"));

        await harness.Http.GetResultAsync("problem", InventoryJson.Default.StockLevel);
        await harness.Http.GetResultAsync("body", InventoryJson.Default.StockLevel);

        IReadOnlyList<LogRecord> records = KernelRecords(logs);
        records.Select(r => r.EventId.Id).Distinct().Order().Should().Equal(11200, 11201, 11202);
        records.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Communication, 2);
        records.Where(r => r.EventId.Id is 11200 or 11201 or 11202).Should().OnlyContain(r => r.Exception != null);
    }

    [Fact]
    public async Task A_caller_with_tenant_actor_and_client_ids_never_reaches_a_log_record_on_the_failure_paths()
    {
        var logs = new InMemoryLoggerFactory();
        using RestHarness harness = Create(logs, NoRetries);
        harness.Stub.Respond(HttpMethod.Get, "/problem", (_, _) => Text(HttpStatusCode.BadRequest, "{not json", "application/problem+json"));
        harness.Stub.Respond(HttpMethod.Get, "/body", (_, _) => Text(HttpStatusCode.OK, "nope", "application/json"));
        var caller = TestRequestContext.ForTenant(Tenant, "actor-canary-1");
        caller.CorrelationId = "3f2d1c0b-aaaa-4bbb-8ccc-0000000000ff";

        using (RequestContextScope.Begin(caller))
        {
            await harness.Http.GetResultAsync("problem", InventoryJson.Default.StockLevel);
            await harness.Http.GetResultAsync("body", InventoryJson.Default.StockLevel);
        }

        KernelRecords(logs).Should().NotBeEmpty();
        KernelRecords(logs).ShouldNotLeak(Tenant.ToString(), "actor-canary-1", caller.CorrelationId!, "inventory/problem", "http://inventory");
    }

    // --- Cross-domain contracts (reverse maps, commit-outcome-unknown) -----------------------------------------

    [Fact]
    public async Task A_problem_with_the_unexpected_default_code_passes_through_as_unexpected()
    {
        using var harness = new RestHarness(NoRetries);
        harness.Stub.RespondProblem(HttpMethod.Get, "/stock", HttpStatusCode.InternalServerError, ErrorCodes.Unexpected.Default);

        Result<StockLevel> result = await harness.Http.GetResultAsync("stock", InventoryJson.Default.StockLevel);

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unexpected, Code = "unexpected.default" });
    }

    [Fact]
    public async Task A_504_commit_outcome_unknown_is_a_timeout_error_with_the_services_code()
    {
        using var harness = new RestHarness(NoRetries);
        harness.Stub.RespondProblem(HttpMethod.Post, "/orders", HttpStatusCode.GatewayTimeout, "execution.commit_outcome_unknown");

        Result result = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Post, "orders"));

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Timeout, Code = "execution.commit_outcome_unknown" });
    }

    [Fact]
    public async Task A_post_without_an_idempotency_key_is_sent_exactly_once_after_a_commit_outcome_unknown()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondProblem(HttpMethod.Post, "/orders", HttpStatusCode.GatewayTimeout, "execution.commit_outcome_unknown");

        Result result = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Post, "orders"));

        result.Error.Type.Should().Be(ErrorType.Timeout);
        harness.Stub.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A_post_with_an_idempotency_key_is_retried_with_the_same_key_on_every_attempt()
    {
        using var harness = new RestHarness(new Dictionary<string, string?> { [Prefix + "PropagateIdempotencyKey"] = "true" });
        harness.Stub.Respond(HttpMethod.Post, "/orders", (_, n) => n < 2
            ? StubHttpMessageHandlerProblem()
            : new HttpResponseMessage(HttpStatusCode.OK));

        Result result = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Post, "orders"));

        result.IsSuccess.Should().BeTrue();
        List<string?> keys = [.. harness.Stub.Requests.Select(r => r.Header(WellKnownHeaders.IdempotencyKey))];
        keys.Should().HaveCount(3);
        keys[0].Should().NotBeNullOrEmpty();
        keys.Should().OnlyContain(k => k == keys[0]);
    }

    [Fact]
    public async Task A_get_is_retried_after_a_commit_outcome_unknown()
    {
        using var harness = new RestHarness();
        harness.Stub.Respond(HttpMethod.Get, "/stock", (_, n) => n == 0 ? StubHttpMessageHandlerProblem() : new HttpResponseMessage(HttpStatusCode.OK));

        (await harness.Http.GetAsync("stock")).EnsureSuccessStatusCode();

        harness.Stub.Requests.Should().HaveCount(2);
    }

    [Fact]
    public void The_retired_code_names_survive_nowhere_in_the_compiled_assembly()
    {
        byte[] assembly = File.ReadAllBytes(typeof(HttpClientResultExtensions).Assembly.Location);

        assembly.AsSpan().IndexOf(Encoding.Unicode.GetBytes("SharedKernel.Communication.Rest.Logger")).Should().BeGreaterThan(0, "the scan must find a literal that is there");
        assembly.AsSpan().IndexOf(Encoding.Unicode.GetBytes("communication.unreachable")).Should().Be(-1);
        assembly.AsSpan().IndexOf(Encoding.Unicode.GetBytes("money.proto.invalid")).Should().Be(-1);
    }

    private static HttpResponseMessage StubHttpMessageHandlerProblem() =>
        SharedKernel.Testing.Communication.StubHttpMessageHandler.Problem(HttpStatusCode.GatewayTimeout, "execution.commit_outcome_unknown");

    private sealed class ThrowingAccessor : IRequestContextAccessor
    {
        public IRequestContext? Current => throw new InvalidOperationException("The caller is not available.");
    }

    /// <summary>A body whose read cancels the caller's token, as a caller giving up mid-read would.</summary>
    private sealed class CancelOnReadContent(CancellationTokenSource cancellation) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 1;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(Stream.Null);
        }
    }
}
