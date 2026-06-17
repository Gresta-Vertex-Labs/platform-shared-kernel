using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using SharedKernel.Communication.Internal.Resolvers;
using SharedKernel.Communication.Rest.Handlers;
using SharedKernel.Communication.Rest.Options;

namespace SharedKernel.Communication.Rest.Builders;

/// <summary>
/// Default implementation of <see cref="IRestCommunicationBuilder"/>.
/// </summary>
internal sealed class RestCommunicationBuilder(IServiceCollection services) : IRestCommunicationBuilder
{
    /// <inheritdoc />
    public IServiceCollection Services { get; } = services;

    /// <inheritdoc />
    public IRestCommunicationBuilder AddRestClient<TClient>(
        string name,
        Action<RestClientOptions>? configure = null)
        where TClient : class
    {
        var options = new RestClientOptions();
        configure?.Invoke(options);

        var hasBaseAddress = !string.IsNullOrWhiteSpace(options.BaseAddress);
        var hasResolver = Services.Any(d => d.ServiceType == typeof(IServiceEndpointResolver));

        if (!hasBaseAddress && !hasResolver)
        {
            throw new InvalidOperationException(
                $"RestClientOptions for '{name}' requires either a BaseAddress or a registered " +
                $"IServiceEndpointResolver. Call AddK8sServiceDiscovery() or AddStaticServiceDiscovery() " +
                $"before registering typed clients without a BaseAddress.");
        }

        var builder = Services.AddHttpClient<TClient>();

        if (hasBaseAddress)
        {
            builder.ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(options.BaseAddress!);
                // Timeout is managed via StandardResilienceHandler per-request timeout
                client.Timeout = Timeout.InfiniteTimeSpan;
            });
        }
        else
        {
            // No BaseAddress — service discovery resolves at request time
            builder.ConfigureHttpClient(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
            });

            var capturedName = name;
            Services.AddTransient(sp =>
            {
                var resolver = sp.GetRequiredService<IServiceEndpointResolver>();
                return new ServiceDiscoveryResolvingHandler(resolver, capturedName);
            });

            builder.AddHttpMessageHandler(sp => sp.GetRequiredService<ServiceDiscoveryResolvingHandler>());
        }

        // Attach StandardResilienceHandler — mandatory; configures retry, circuit breaker, timeout.
        // Polly validation constraint: SamplingDuration must be >= 2 * AttemptTimeout.
        var resilience = options.Resilience;
        var attemptTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        var samplingDuration = TimeSpan.FromSeconds(resilience.SamplingDurationSec);
        var minimumSamplingDuration = TimeSpan.FromTicks(attemptTimeout.Ticks * 2 + 1);
        if (samplingDuration < minimumSamplingDuration)
        {
            samplingDuration = minimumSamplingDuration;
        }

        builder.AddStandardResilienceHandler(o =>
        {
            o.Retry.MaxRetryAttempts = resilience.RetryCount;
            o.Retry.Delay = TimeSpan.FromMilliseconds(resilience.RetryBaseDelayMs);
            o.Retry.BackoffType = DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;

            o.AttemptTimeout.Timeout = attemptTimeout;
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(
                options.TimeoutSeconds * (resilience.RetryCount + 1) + 10);

            if (resilience.CircuitBreakerEnabled)
            {
                o.CircuitBreaker.MinimumThroughput = resilience.FailureThreshold;
                o.CircuitBreaker.SamplingDuration = samplingDuration;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(resilience.BreakDurationSec);
            }
        });

        // Fixed pipeline order: CorrelationId → TenantId → StandardResilienceHandler → transport
        builder.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
        builder.AddHttpMessageHandler<TenantIdDelegatingHandler>();

        return this;
    }
}
