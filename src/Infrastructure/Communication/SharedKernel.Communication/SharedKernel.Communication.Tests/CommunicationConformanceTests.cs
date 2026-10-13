using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Internal;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Communication.Tests;

/// <summary>
/// The observability and exception conformance of the base package: its log events, the error factory, and that a
/// misconfiguration fails at startup instead of at the first request.
/// </summary>
public sealed class CommunicationConformanceTests
{
    private const string Prefix = "SharedKernel:Communication:Clients:orders:";

    private readonly StubHttpMessageHandler _service = new();
    private readonly StubHttpMessageHandler _tokenEndpoint = new();

    // --- Event ids ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_event_ids_are_pinned_and_live_in_the_base_sub_block()
    {
        CommunicationLoggingEventIds.TokenAcquired.Should().Be(11000);
        CommunicationLoggingEventIds.TokenRequestRefused.Should().Be(11001);
        CommunicationLoggingEventIds.TokenEndpointUnreachable.Should().Be(11002);
        CommunicationLoggingEventIds.AccessTokenUnavailable.Should().Be(11003);
        CommunicationLoggingEventIds.RetryingWithNewAccessToken.Should().Be(11004);

        int[] ids = LoggerMessageIds(typeof(CommunicationOptions).Assembly);
        ids.Should().Equal(11000, 11001, 11002, 11003, 11004);
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Communication, 0));
        ids.Should().NotContain(id => id >= 11300 && id <= 11399);
    }

    [Fact]
    public void The_base_assembly_logs_through_no_string_category()
    {
        typeof(CommunicationOptions).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(f => f.FieldType == typeof(ILogger))
            .Should().BeEmpty();
    }

    private static int[] LoggerMessageIds(Assembly assembly) =>
        [.. assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(a => a.EventId)
            .Order()];

    // --- Templates, levels and exceptions of every event -----------------------------------------------------------

    [Fact]
    public async Task Every_event_has_its_level_and_follows_the_log_conventions()
    {
        var records = new List<LogRecord>();

        // 11000: a token is acquired.
        records.AddRange(await RunAsync(
            service => service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.OK),
            token => token.RespondJson(HttpMethod.Post, "/token", new { access_token = "t1", token_type = "Bearer", expires_in = 3600 })));

        // 11001 and 11003: the endpoint refuses the client.
        records.AddRange(await RunAsync(
            service => service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.OK),
            token => token.RespondJson(HttpMethod.Post, "/token", new { error = "invalid_client" }, HttpStatusCode.Unauthorized)));

        // 11002: the endpoint cannot be reached.
        records.AddRange(await RunAsync(
            service => service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.OK),
            token => token.Throw(HttpMethod.Post, "/token", new HttpRequestException(HttpRequestError.ConnectionError))));

        // 11004: a 401 is answered with a new token.
        records.AddRange(await RunAsync(
            service => service.Respond(HttpMethod.Get, "/x", (request, _) =>
                new HttpResponseMessage(request.Headers.Authorization?.Parameter == "t2" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)),
            token => token.Respond(HttpMethod.Post, "/token", (_, n) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { access_token = $"t{n + 1}", token_type = "Bearer", expires_in = 3600 }),
            })));

        IReadOnlyList<LogRecord> kernel = [.. records.Where(r => r.EventId.Id >= LoggingEventIdRanges.Communication)];
        kernel.Select(r => r.EventId.Id).Distinct().Order().Should().Equal(11000, 11001, 11002, 11003, 11004);
        kernel.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Communication, 0);

        kernel.ShouldHaveLogged(new EventId(11000), LogLevel.Debug).Message.Should().Contain("'orders'").And.Contain("3600 seconds");
        kernel.ShouldHaveLogged(new EventId(11001), LogLevel.Warning).Message.Should().Contain("401").And.Contain("invalid_client");
        kernel.ShouldHaveLoggedWithException(new EventId(11002), typeof(HttpRequestException)).LogLevel.Should().Be(LogLevel.Warning);
        LogRecord unavailable = kernel.First(r => r.EventId.Id == 11003);
        unavailable.LogLevel.Should().Be(LogLevel.Warning);
        unavailable.Message.Should().Contain("Unavailable").And.Contain(CommunicationErrorCodes.AccessTokenUnavailable);
        kernel.ShouldHaveLogged(new EventId(11004), LogLevel.Debug).Message.Should().Contain("'orders'");
    }

    [Fact]
    public async Task The_handler_logs_under_its_own_type_as_the_category()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSharedKernelCommunication(TestHost.Configuration());
        services.UseStubHttpMessageHandler(ClientCredentialsTokenClient.HttpClientName, _tokenEndpoint.RespondStatus(HttpMethod.Post, "/token", HttpStatusCode.BadRequest));
        await using ServiceProvider provider = services.BuildServiceProvider();
        var options = new ClientAuthenticationOptions
        {
            Mode = ClientAuthenticationMode.ClientCredentials,
            ClientCredentials = { TokenEndpoint = new Uri("https://login.example.com/token"), ClientId = "id", ClientSecret = "secret" },
        };
        using var invoker = new HttpMessageInvoker(new ClientAuthenticationHandler(
            "orders", () => options, provider, provider.GetRequiredService<ILogger<ClientAuthenticationHandler>>())
        { InnerHandler = _service });

        Func<Task> send = () => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://orders/x"), CancellationToken.None);

        await send.Should().ThrowAsync<AccessTokenUnavailableException>();
        var factory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        factory.ShouldHaveCategory(typeof(ClientAuthenticationHandler));
        factory.Loggers.Keys.Should().NotContain("SharedKernel.Communication.Authentication");
    }

    private async Task<IReadOnlyList<LogRecord>> RunAsync(Action<StubHttpMessageHandler> service, Action<StubHttpMessageHandler> token)
    {
        var serviceHandler = new StubHttpMessageHandler();
        var tokenHandler = new StubHttpMessageHandler();
        service(serviceHandler);
        token(tokenHandler);

        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSharedKernelCommunication(TestHost.Configuration());
        services.UseStubHttpMessageHandler(ClientCredentialsTokenClient.HttpClientName, tokenHandler);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var options = new ClientAuthenticationOptions
        {
            Mode = ClientAuthenticationMode.ClientCredentials,
            ClientCredentials = { TokenEndpoint = new Uri("https://login.example.com/token"), ClientId = "canary-id", ClientSecret = "canary-secret" },
        };
        using var invoker = new HttpMessageInvoker(new ClientAuthenticationHandler(
            "orders", () => options, provider, provider.GetRequiredService<ILogger<ClientAuthenticationHandler>>())
        { InnerHandler = serviceHandler });

        try
        {
            using HttpResponseMessage response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://orders/x"), CancellationToken.None);
        }
        catch (AccessTokenUnavailableException)
        {
            // Expected for the failing token endpoints: the log is what is under test.
        }

        var factory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        return [.. factory.Loggers.Values.SelectMany(l => l.Records)];
    }

    // --- Exceptions fail at startup, not on the first request -------------------------------------------------------

    [Fact]
    public void An_undefined_mode_fails_startup_with_sentences_that_end_in_a_period()
    {
        using ServiceProvider provider = Register(out _, ($"{Prefix}Authentication:Mode", "42"));

        OptionsValidationException failure = Validate(provider);

        failure.OptionsName.Should().Be("orders");
        failure.Failures.Should().NotBeEmpty().And.OnlyContain(f => f.EndsWith('.'));
        failure.Failures.Should().Contain(f => f.Contains("is not a defined ClientAuthenticationMode value.", StringComparison.Ordinal));
    }

    [Fact]
    public void An_undefined_secret_transport_fails_startup_naming_the_enum()
    {
        using ServiceProvider provider = Register(
            out _,
            ($"{Prefix}Authentication:Mode", "ClientCredentials"),
            ($"{Prefix}Authentication:ClientCredentials:TokenEndpoint", "https://login.example.com/token"),
            ($"{Prefix}Authentication:ClientCredentials:ClientId", "id"),
            ($"{Prefix}Authentication:ClientCredentials:ClientSecret", "secret"),
            ($"{Prefix}Authentication:ClientCredentials:SecretTransport", "9"));

        OptionsValidationException failure = Validate(provider);

        failure.Failures.Should().Contain(f => f.Contains("is not a defined ClientSecretTransport value.", StringComparison.Ordinal) && f.EndsWith('.'));
    }

    [Fact]
    public void A_missing_keyed_provider_fails_startup_naming_the_client_and_the_remedy()
    {
        using ServiceProvider provider = Register(out _, ($"{Prefix}Authentication:Mode", "AccessTokenProvider"));

        OptionsValidationException failure = Validate(provider);

        failure.OptionsName.Should().Be("orders");
        string message = failure.Failures.Should().ContainSingle().Subject;
        message.Should().Contain("'orders'").And.Contain("Call UseAccessTokenProvider<T>() on it.").And.EndWith(".");
    }

    [Fact]
    public void A_registered_provider_passes_startup_and_is_never_resolved_by_the_check()
    {
        using ServiceProvider provider = Register(
            out ICommunicationBuilder builder,
            ($"{Prefix}Authentication:Mode", "AccessTokenProvider"));
        // The provider has a dependency that does not exist: validating the registration must not construct it.
        builder.Services.AddKeyedSingleton<IAccessTokenProvider>("orders", (_, _) => throw new InvalidOperationException("must not be built"));
        using ServiceProvider registered = builder.Services.BuildServiceProvider();

        Action validate = () => registered.GetRequiredService<IStartupValidator>().Validate();

        validate.Should().NotThrow();
    }

    [Fact]
    public async Task A_client_that_is_valid_at_startup_never_throws_an_invalid_operation_at_call_time()
    {
        var services = TestHost.Services();
        ICommunicationBuilder builder = services.AddSharedKernelCommunication(TestHost.Configuration(($"{Prefix}Authentication:Mode", "ApiKey"), ($"{Prefix}Authentication:ApiKey:Value", "k")));
        ClientPipeline.BindOptions<TestOptions>(builder, "orders", configure: null);
        services.UseStubHttpMessageHandler(ClientCredentialsTokenClient.HttpClientName, _tokenEndpoint);
        await using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
        _service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.OK);
        var options = provider.GetRequiredService<IOptionsMonitor<TestOptions>>().Get("orders");
        using var invoker = new HttpMessageInvoker(new ClientAuthenticationHandler(
            "orders", () => options.Authentication, provider, provider.GetRequiredService<ILogger<ClientAuthenticationHandler>>())
        { InnerHandler = _service });

        using HttpResponseMessage response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://orders/x"), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _service.Requests.Single().Header("X-Api-Key").Should().Be("k");
    }

    [Fact]
    public void The_registry_and_tls_messages_are_sentences_that_end_in_a_period_and_name_the_remedy()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCommunication(TestHost.Configuration());
        CommunicationClientRegistry.Reserve(services, "orders", "REST");

        Action duplicate = () => CommunicationClientRegistry.Reserve(services, "orders", "gRPC");
        Action colon = () => CommunicationClientRegistry.Reserve(services, "a:b", "REST");
        Action missing = () => CommunicationClientRegistry.Reserve(new ServiceCollection(), "orders", "REST");

        duplicate.Should().Throw<InvalidOperationException>().Which.Message.Should().EndWith(".").And.Contain("Use a different name");
        colon.Should().Throw<ArgumentException>().Which.Message.Should().Contain("must not contain ':'").And.Contain("Use a name without it.");
        missing.Should().Throw<InvalidOperationException>().Which.Message.Should().EndWith(".").And.Contain("AddSharedKernelCommunication");
    }

    // --- The error factory -------------------------------------------------------------------------------------------

    [Fact]
    public void The_error_factory_builds_each_error_with_its_documented_type_and_code()
    {
        CommunicationErrors.Unavailable().Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = "communication.unavailable" });
        CommunicationErrors.Timeout().Should().BeEquivalentTo(new { Type = ErrorType.Timeout, Code = "communication.timeout" });
        CommunicationErrors.CircuitOpen().Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = "communication.circuit_open" });
        CommunicationErrors.AccessTokenUnavailable().Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = "communication.access_token_unavailable" });
        CommunicationErrors.EmptyBody().Should().BeEquivalentTo(new { Type = ErrorType.Unexpected, Code = "communication.empty_body" });
        CommunicationErrors.InvalidBody(typeof(string)).Should().BeEquivalentTo(new { Type = ErrorType.Unexpected, Code = "communication.invalid_body" });
        CommunicationErrors.InvalidMoney().Should().BeEquivalentTo(new { Type = ErrorType.Validation, Code = "communication.grpc.invalid_money" });
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.BusinessRule)]
    [InlineData(ErrorType.Unavailable)]
    [InlineData(ErrorType.Timeout)]
    [InlineData(ErrorType.Unexpected)]
    public void Create_builds_the_requested_type(ErrorType type)
    {
        Error error = CommunicationErrors.Create(type, "x.y", "A message.");

        error.Should().BeEquivalentTo(new { Type = type, Code = "x.y", Message = "A message." });
    }

    [Fact]
    public void Every_message_is_a_fixed_sentence_ending_in_a_period()
    {
        Error[] errors =
        [
            CommunicationErrors.Unavailable(),
            CommunicationErrors.Unavailable("ConnectionError"),
            CommunicationErrors.Timeout(),
            CommunicationErrors.CircuitOpen(),
            CommunicationErrors.AccessTokenUnavailable(),
            CommunicationErrors.EmptyBody(),
            CommunicationErrors.InvalidBody(typeof(int)),
            CommunicationErrors.InvalidMoney(),
        ];

        errors.Select(e => e.Message).Should().OnlyContain(m => m.EndsWith('.'));
    }

    private ServiceProvider Register(out ICommunicationBuilder builder, params (string Key, string? Value)[] settings)
    {
        var services = TestHost.Services();
        builder = services.AddSharedKernelCommunication(TestHost.Configuration(settings));
        ClientPipeline.BindOptions<TestOptions>(builder, "orders", configure: null);
        return services.BuildServiceProvider();
    }

    private static OptionsValidationException Validate(IServiceProvider provider)
    {
        Action validate = () => provider.GetRequiredService<IStartupValidator>().Validate();
        return validate.Should().Throw<OptionsValidationException>().Which;
    }

    private sealed class TestOptions : CommunicationClientOptions;
}
