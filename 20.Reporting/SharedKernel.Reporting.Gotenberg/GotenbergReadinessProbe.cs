using System.Diagnostics;
using SharedKernel.Primitives.Health;

namespace SharedKernel.Reporting.Gotenberg;

/// <summary>The <c>gotenberg</c> readiness probe: <c>GET /health</c> on the configured Gotenberg.</summary>
internal sealed class GotenbergReadinessProbe(IHttpClientFactory httpClientFactory) : IReadinessProbe
{
    public const string ProbeName = "gotenberg";

    private const string HealthPath = "health";

    public string Name => ProbeName;

    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            HttpClient client = httpClientFactory.CreateClient(GotenbergHtmlToPdfConverter.HttpClientName);
            using HttpResponseMessage response = await client.GetAsync(HealthPath, cancellationToken).ConfigureAwait(false);
            TimeSpan latency = Stopwatch.GetElapsedTime(start);

            return response.IsSuccessStatusCode
                ? ReadinessReport.Healthy(latency: latency)
                : ReadinessReport.Unhealthy($"Gotenberg answered {(int)response.StatusCode} to /health.", latency: latency);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or Polly.Timeout.TimeoutRejectedException
            && !cancellationToken.IsCancellationRequested)
        {
            return ReadinessReport.Unhealthy($"Gotenberg could not be reached: {exception.GetType().Name}.", latency: Stopwatch.GetElapsedTime(start));
        }
    }
}
