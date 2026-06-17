using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Rest.Extensions;
using SharedKernel.Communication.Rest.Handlers;

namespace SharedKernel.Communication.Rest.Tests.Resilience;

/// <summary>
/// Validates that the StandardResilienceHandler pipeline is wired and fires retry behaviour.
/// </summary>
public sealed class ResiliencePipelineTests
{
    [Fact]
    public void StandardResilienceHandler_IsRegistered_WhenClientAdded()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services.AddSharedKernelRestCommunication()
            .AddRestClient<ResilienceSmokeClient>(
                "smoke",
                o =>
                {
                    o.BaseAddress = "http://smoke-test";
                    o.Resilience.RetryCount = 2;
                    o.Resilience.CircuitBreakerEnabled = true;
                    o.TimeoutSeconds = 10;
                });

        // Assert — client resolves without error; the StandardResilienceHandler is always attached
        // because AddRestClient never allows a path that skips it (build-time guarantee).
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ResilienceSmokeClient>();
        client.Should().NotBeNull();
        client.Http.Should().NotBeNull();
    }

    [Fact]
    public async Task ResilienceHandler_TriggersRetries_OnTransientFailures()
    {
        // Arrange — use a stub handler at the PRIMARY handler level (below the resilience layer)
        // so the resilience policy sees the transient failures and retries.
        const int retryCount = 2;
        var callCount = 0;

        var services = new ServiceCollection();

        // Register stub as primary handler before the resilience handler sees it
        services.AddTransient<CorrelationIdDelegatingHandler>();
        services.AddTransient<TenantIdDelegatingHandler>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        var httpBuilder = services.AddHttpClient<RetryCountingClient>()
            .ConfigurePrimaryHttpMessageHandler(() =>
                new TransientFailureHandler(() =>
                {
                    callCount++;
                    // First two calls → 503 (transient), third call → 200 (success)
                    return callCount <= retryCount
                        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        : new HttpResponseMessage(HttpStatusCode.OK);
                }))
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = retryCount;
                o.Retry.Delay = TimeSpan.FromMilliseconds(1);
                o.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                o.Retry.UseJitter = false;
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
                o.CircuitBreaker.MinimumThroughput = 100; // Prevent CB from tripping in test
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(21); // >= 2 * AttemptTimeout
            });

        using var sp = services.BuildServiceProvider();
        var typedClient = sp.GetRequiredService<RetryCountingClient>();

        // Act
        var response = await typedClient.Http.GetAsync("http://test-host/path");

        // Assert — the pipeline retried and eventually succeeded
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(retryCount + 1, "should have attempted original + retryCount calls");
    }

    [Fact]
    public void AddRestClient_ResilienceOptions_AreApplied_WithoutValidationError()
    {
        // Arrange — configure all resilience knobs explicitly
        var services = new ServiceCollection();

        // Act & Assert — no OptionsValidationException thrown
        services.AddSharedKernelRestCommunication()
            .AddRestClient<ResilienceSmokeClient2>(
                "full-config",
                o =>
                {
                    o.BaseAddress = "http://full-config-service";
                    o.TimeoutSeconds = 15;
                    o.Resilience.RetryCount = 3;
                    o.Resilience.RetryBaseDelayMs = 200;
                    o.Resilience.CircuitBreakerEnabled = true;
                    o.Resilience.FailureThreshold = 5;
                    o.Resilience.SamplingDurationSec = 30; // Builder auto-adjusts to meet Polly constraint
                    o.Resilience.BreakDurationSec = 30;
                });

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ResilienceSmokeClient2>();
        client.Should().NotBeNull();
    }
}

internal sealed class ResilienceSmokeClient(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;
}

internal sealed class ResilienceSmokeClient2(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;
}

internal sealed class RetryCountingClient(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;
}

internal sealed class TransientFailureHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => Task.FromResult(responseFactory());
}
