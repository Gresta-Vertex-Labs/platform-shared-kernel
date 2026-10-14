using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using SharedKernel.ServiceDefaults.Extensions;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// C-08: the standard resilience handler's Polly telemetry logs the outcome of a failed attempt under the "Polly"
/// category, outside every kernel logger. The base composition must keep the exception text (a host, a URL, a key) out of
/// those records.
/// </summary>
[Collection(NonParallelTelemetryCollection.Name)]
public sealed class PollyResilienceLogTests
{
    private const string Sentinel = "https://sentinel-provider.invalid/v1/send?key=sentinel-key-731904";
    private const string RequestUri = "https://callee.invalid/v1/send";

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException(Sentinel);
    }

    private sealed class FaultingClient(HttpClient http)
    {
        public Task<HttpResponseMessage> CallAsync() => http.GetAsync(RequestUri);
    }

    private sealed record Captured(string Category, LogLevel Level, string Message, string State, Exception? Exception);

    private sealed class CapturingProvider(List<Captured> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, List<Captured> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join("|", pairs.Select(p => $"{p.Key}={p.Value}"))
                    : string.Empty;
                lock (sink)
                {
                    sink.Add(new Captured(category, logLevel, formatter(state, exception), properties, exception));
                }
            }
        }
    }

    private static async Task<List<Captured>> RunFaultingCallAsync(Action<ILoggingBuilder>? afterDefaults = null)
    {
        var records = new List<Captured>();
        var builder = WebApplication.CreateBuilder();
        builder.AddServiceDefaults();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        afterDefaults?.Invoke(builder.Logging);
        builder.Logging.AddProvider(new CapturingProvider(records));
        builder.Services.AddHttpClient<FaultingClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new ThrowingHandler())
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 1;
                o.Retry.Delay = TimeSpan.Zero;
                o.Retry.UseJitter = false;
            });

        await using var provider = builder.Services.BuildServiceProvider();
        var client = provider.GetRequiredService<FaultingClient>();

        var act = () => client.CallAsync();
        await act.Should().ThrowAsync<HttpRequestException>();

        lock (records)
        {
            return [.. records];
        }
    }

    [Fact]
    public async Task AFaultingTypedHttpClient_LeavesNoExceptionTextInAnyLogRecord()
    {
        var records = await RunFaultingCallAsync();

        records.Should().NotBeEmpty("the HttpClient pipeline logs, so the assertion below inspects real records");
        records.Select(r => (r.Message, r.State)).Should().OnlyContain(
            r => !r.Message.Contains("sentinel", StringComparison.OrdinalIgnoreCase) && !r.State.Contains("sentinel", StringComparison.OrdinalIgnoreCase),
            "no log record of the host may carry the exception text of a failed outbound call");
        records.Should().NotContain(r => r.Category.StartsWith("Polly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheFilterIsWhatKeepsThePollyRecordsOut_AServiceThatReEnablesTheCategoryGetsTheLeak()
    {
        // The control: proves the assertion above is meaningful. Polly\'s records do carry the exception message when the
        // category is on, which is why AddSharedKernelTelemetry() switches it off.
        var records = await RunFaultingCallAsync(logging => logging.AddFilter("Polly", LogLevel.Trace));

        records.Where(r => r.Category.StartsWith("Polly", StringComparison.Ordinal))
            .Should().Contain(r => r.Message.Contains("sentinel-key-731904", StringComparison.Ordinal));
    }
}
