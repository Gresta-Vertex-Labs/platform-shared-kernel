using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace SharedKernel.Communication.Rest.Internal;

/// <summary>Maps <see cref="RestClientOptions"/> onto Microsoft.Extensions.Http.Resilience's standard pipelines.</summary>
internal static class RestResilience
{
    public static void AddStandard(IHttpClientBuilder http, string name) =>
        http.AddStandardResilienceHandler().Configure((HttpStandardResilienceOptions o, IServiceProvider services) =>
        {
            RestClientOptions options = RestCommunicationBuilderExtensions.Options(services, name);

            o.AttemptTimeout.Timeout = options.AttemptTimeout;
            o.TotalRequestTimeout.Timeout = options.TotalTimeout;

            o.Retry.BackoffType = DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;
            o.Retry.Delay = options.Retry.BaseDelay;
            o.Retry.MaxRetryAttempts = Math.Max(1, options.Retry.MaxRetryAttempts);
            if (options.Retry.MaxRetryAttempts == 0)
            {
                // The strategy needs at least one attempt configured; it never handles anything instead.
                o.Retry.ShouldHandle = static _ => ValueTask.FromResult(false);
            }
            else if (!options.RetriesNonIdempotentMethods)
            {
                o.Retry.DisableFor(HttpMethod.Post, HttpMethod.Patch);
            }

            ApplyCircuitBreaker(o.CircuitBreaker, options);
        });

    public static void AddHedging(IHttpClientBuilder http, string name) =>
        http.AddStandardHedgingHandler().Configure((HttpStandardHedgingResilienceOptions o, IServiceProvider services) =>
        {
            RestClientOptions options = RestCommunicationBuilderExtensions.Options(services, name);

            o.TotalRequestTimeout.Timeout = options.TotalTimeout;
            o.Endpoint.Timeout.Timeout = options.AttemptTimeout;
            o.Hedging.MaxHedgedAttempts = options.Hedging.MaxHedgedAttempts;
            o.Hedging.Delay = options.Hedging.Delay;

            if (!options.RetriesNonIdempotentMethods)
            {
                // A POST or PATCH without an Idempotency-Key is sent once: no parallel copy is started after the delay
                // (an infinite delay waits for the first attempt), and its failure starts none either.
                TimeSpan hedgingDelay = options.Hedging.Delay;
                o.Hedging.DelayGenerator = args => ValueTask.FromResult(
                    IsNonIdempotent(args.Context.GetRequestMessage()?.Method) ? Timeout.InfiniteTimeSpan : hedgingDelay);

                var shouldHandle = o.Hedging.ShouldHandle;
                o.Hedging.ShouldHandle = args =>
                    IsNonIdempotent(args.Context.GetRequestMessage()?.Method) ? ValueTask.FromResult(false) : shouldHandle(args);
            }

            ApplyCircuitBreaker(o.Endpoint.CircuitBreaker, options);
        });

    private static void ApplyCircuitBreaker(HttpCircuitBreakerStrategyOptions breaker, RestClientOptions options)
    {
        if (options.CircuitBreaker.Enabled)
        {
            breaker.FailureRatio = options.CircuitBreaker.FailureRatio;
            breaker.MinimumThroughput = options.CircuitBreaker.MinimumThroughput;
            breaker.SamplingDuration = options.CircuitBreaker.SamplingDuration;
            breaker.BreakDuration = options.CircuitBreaker.BreakDuration;
            return;
        }

        // Switched off: it never counts a failure. The pipeline still validates its sampling window against the attempt timeout.
        breaker.ShouldHandle = static _ => ValueTask.FromResult(false);
        breaker.SamplingDuration = TimeSpan.FromTicks(Math.Max(breaker.SamplingDuration.Ticks, options.AttemptTimeout.Ticks * 2));
    }

    internal static bool IsNonIdempotent(HttpMethod? method) =>
        method is not null && (method == HttpMethod.Post || method == HttpMethod.Patch);
}
