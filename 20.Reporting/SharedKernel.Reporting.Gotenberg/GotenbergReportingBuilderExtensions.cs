using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Primitives.Health;
using SharedKernel.Reporting.Gotenberg;

namespace SharedKernel.Reporting;

/// <summary>Adds Gotenberg HTML-to-PDF conversion to SharedKernel reporting.</summary>
public static class GotenbergReportingBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="IHtmlToPdfConverter"/> over Gotenberg, with <see cref="GotenbergOptions"/> bound from
    /// <c>SharedKernel:Reporting:Gotenberg</c> and validated at startup, retries and timeouts from
    /// Microsoft.Extensions.Http.Resilience, and the <c>gotenberg</c> readiness probe.
    /// </summary>
    /// <param name="builder">The builder from <c>services.AddSharedKernelReporting()</c>.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The builder.</returns>
    public static IReportingBuilder AddGotenberg(this IReportingBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        IServiceCollection services = builder.Services;
        services.AddValidatedOptions<GotenbergOptions>(configuration);

        services
            .AddHttpClient(GotenbergHtmlToPdfConverter.HttpClientName, (serviceProvider, client) =>
            {
                GotenbergOptions options = serviceProvider.GetRequiredService<IOptions<GotenbergOptions>>().Value;
                string baseUrl = options.BaseUrl!.AbsoluteUri;
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

                // The resilience pipeline owns every timeout; HttpClient's own 100 s default would cut it short.
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

                if (!string.IsNullOrEmpty(options.Username))
                {
                    string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }
            })
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions resilience, IServiceProvider serviceProvider) =>
            {
                GotenbergOptions options = serviceProvider.GetRequiredService<IOptions<GotenbergOptions>>().Value;

                resilience.AttemptTimeout.Timeout = options.Timeout;
                resilience.Retry.MaxRetryAttempts = Math.Max(1, options.MaxRetryAttempts);
                resilience.Retry.BackoffType = DelayBackoffType.Exponential;
                resilience.Retry.Delay = TimeSpan.FromSeconds(1);
                if (options.MaxRetryAttempts == 0)
                {
                    resilience.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
                }

                TimeSpan backoff = TimeSpan.FromSeconds(Math.Pow(2, options.MaxRetryAttempts + 1));
                resilience.TotalRequestTimeout.Timeout = (options.Timeout * (options.MaxRetryAttempts + 1)) + backoff;

                // The circuit breaker must sample at least two attempts' worth of time (the handler validates this).
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromTicks(Math.Max(
                    resilience.CircuitBreaker.SamplingDuration.Ticks,
                    options.Timeout.Ticks * 2));
            });

        services.AddReadinessProbe<GotenbergReadinessProbe>();
        return builder.AddHtmlToPdfConverter<GotenbergHtmlToPdfConverter>();
    }
}
