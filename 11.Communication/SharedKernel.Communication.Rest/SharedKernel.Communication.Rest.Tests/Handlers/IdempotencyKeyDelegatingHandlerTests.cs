using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Rest.Extensions;
using SharedKernel.Communication.Rest.Handlers;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Communication.Rest.Tests.Handlers;

/// <summary>
/// T-38 (P-364/WO-056): <c>IdempotencyKeyDelegatingHandler</c> tests. Uses
/// <see cref="HttpMessageHandler"/> test doubles exclusively — never a real HTTP call.
/// </summary>
public sealed class IdempotencyKeyDelegatingHandlerTests
{
    private const string HeaderName = WellKnownHeaders.IdempotencyKey;

    private static HttpClient BuildClient(HttpMessageHandler stub)
    {
        var handler = new IdempotencyKeyDelegatingHandler
        {
            InnerHandler = stub
        };
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    [Fact]
    public async Task SendAsync_WhenHeaderAbsent_InjectsCanonicalHyphenatedGuidKey()
    {
        // Arrange
        string? captured = null;
        var stub = new CaptureHeaderHandler(HeaderName, h => captured = h);
        var client = BuildClient(stub);

        // Act
        await client.GetAsync("/test");

        // Assert — must be the canonical hyphenated "D" format, not the 32-char "N" format.
        captured.Should().NotBeNullOrEmpty();
        Guid.TryParseExact(captured, "D", out _).Should().BeTrue(
            "the generated idempotency key must be the canonical hyphenated GUID format");
    }

    [Fact]
    public async Task SendAsync_WhenCallerAlreadySetHeader_DoesNotOverwrite()
    {
        // Arrange
        const string callerValue = "caller-set-idempotency-key";
        string? captured = null;
        var stub = new CaptureHeaderHandler(HeaderName, h => captured = h);
        var client = BuildClient(stub);

        var request = new HttpRequestMessage(HttpMethod.Post, "/test");
        request.Headers.TryAddWithoutValidation(HeaderName, callerValue);

        // Act
        await client.SendAsync(request);

        // Assert
        captured.Should().Be(callerValue, "handler must not overwrite a caller-supplied idempotency key");
    }

    [Fact]
    public async Task SendAsync_SameRequestInstanceSentMultipleTimes_ReusesSameKeyAcrossEveryAttempt()
    {
        // Arrange — simulates a Polly retry sequence: StandardResilienceHandler re-sends the SAME
        // HttpRequestMessage instance on every retry attempt, never constructing a new one.
        var captured = new List<string>();
        var stub = new CaptureHeaderHandler(HeaderName, h => captured.Add(h));
        var handler = new IdempotencyKeyDelegatingHandler { InnerHandler = stub };
        using var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/test");

        // Act — send the same request instance three times, as a retry loop would.
        await invoker.SendAsync(request, CancellationToken.None);
        await invoker.SendAsync(request, CancellationToken.None);
        await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        captured.Should().HaveCount(3);
        captured.Distinct().Should().ContainSingle(
            "the same idempotency key must survive unchanged through every retry attempt of one logical call");
    }

    [Fact]
    public async Task SendAsync_NeverThrows_OnAnyInput()
    {
        // Arrange
        var stub = new CaptureHeaderHandler(HeaderName, _ => { });
        var client = BuildClient(stub);

        // Act
        Func<Task> act = () => client.GetAsync("/test");

        // Assert
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// T-38 (P-364/WO-056): behavioral proof (not merely a DI-resolution smoke check) that the
    /// conditional handler is genuinely absent from the outbound pipeline when
    /// <c>EnableIdempotencyKeyPropagation</c> is left at its default (<c>false</c>) — swaps in a
    /// stub primary handler and issues a real call through the resolved typed client's own
    /// <see cref="HttpClient"/>, asserting the idempotency header never reaches the wire.
    /// </summary>
    [Fact]
    public async Task AddRestClient_WithIdempotencyPropagationDisabledByDefault_NeverInjectsIdempotencyHeader()
    {
        // Arrange
        var services = new ServiceCollection();
        string? captured = null;
        var stub = new CaptureHeaderHandler(HeaderName, h => captured = h);

        services.AddSharedKernelRestCommunication()
            .AddRestClient<IdempotencyTestTypedClient>(
                "idempotency-disabled-client",
                o => o.BaseAddress = "http://test-service");

        // Swap in a stub primary handler so the request never leaves the process — additive
        // configuration against the same named client AddRestClient<TClient> already registered.
        services.AddHttpClient<IdempotencyTestTypedClient>()
            .ConfigurePrimaryHttpMessageHandler(() => stub);

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<IdempotencyTestTypedClient>();

        // Act
        await client.Http.GetAsync("/test");

        // Assert
        captured.Should().BeNull(
            "IdempotencyKeyDelegatingHandler must be absent from the pipeline when " +
            "EnableIdempotencyKeyPropagation is false (the default)");
    }

    [Fact]
    public async Task AddRestClient_WithIdempotencyPropagationEnabled_InjectsIdempotencyHeader()
    {
        // Arrange
        var services = new ServiceCollection();
        string? captured = null;
        var stub = new CaptureHeaderHandler(HeaderName, h => captured = h);

        services.AddSharedKernelRestCommunication()
            .AddRestClient<IdempotencyTestTypedClient>(
                "idempotency-enabled-client",
                o =>
                {
                    o.BaseAddress = "http://test-service";
                    o.EnableIdempotencyKeyPropagation = true;
                });

        services.AddHttpClient<IdempotencyTestTypedClient>()
            .ConfigurePrimaryHttpMessageHandler(() => stub);

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<IdempotencyTestTypedClient>();

        // Act
        await client.Http.GetAsync("/test");

        // Assert
        captured.Should().NotBeNullOrEmpty(
            "IdempotencyKeyDelegatingHandler must be present in the pipeline when " +
            "EnableIdempotencyKeyPropagation is true");
    }
}

/// <summary>Minimal typed client for DI registration tests.</summary>
internal sealed class IdempotencyTestTypedClient(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;
}

/// <summary>Test double that captures a single header value and returns 200 OK.</summary>
internal sealed class CaptureHeaderHandler(string headerName, Action<string> capture) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.TryGetValues(headerName, out var values))
        {
            capture(values.First());
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
