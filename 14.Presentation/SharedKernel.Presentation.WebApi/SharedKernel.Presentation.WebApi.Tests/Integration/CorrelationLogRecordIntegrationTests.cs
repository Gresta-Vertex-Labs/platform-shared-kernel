using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using SharedKernel.Presentation.WebApi.Middleware;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Proves <see cref="CorrelationIdMiddleware"/>'s <see cref="Activity"/> baggage output is
/// compatible with <c>13.ServiceDefaults</c>'s documented <c>BaggageLogRecordProcessor</c> ambient
/// log-enrichment mechanism (WO-041, P-256) — without taking a <c>ProjectReference</c> on
/// <c>SharedKernel.ServiceDefaults</c>. A test-local minimal <see cref="BaseProcessor{LogRecord}"/>
/// mirrors that processor's documented contract (generic <see cref="Activity.Baggage"/> →
/// <see cref="LogRecord.Attributes"/> copy, never overwriting an existing attribute). This is the
/// reciprocal of the technique <c>13.ServiceDefaults</c>'s own correlation test already uses in the
/// opposite direction.
/// </summary>
public class CorrelationLogRecordIntegrationTests
{
    [Fact]
    public async Task RequestThroughMiddleware_ThenLogRecordEmitted_ContainsCorrelationIdUnderBaggageKey()
    {
        var captured = new List<IReadOnlyList<KeyValuePair<string, object?>>?>();

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(options =>
            {
                options.AddProcessor(new TestBaggageLogRecordProcessor());
                options.AddProcessor(new CapturingProcessor(captured));
            }));

        var logger = loggerFactory.CreateLogger("CorrelationLogRecordIntegrationTests");

        using var activity = new Activity("test-request").Start();

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new NoOpHttpResponseFeature());
        httpContext.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));

        string? observedCorrelationId = null;

        var middleware = new CorrelationIdMiddleware(
            ctx =>
            {
                observedCorrelationId = (string?)ctx.Items[CorrelationIdMiddleware.ItemsKey];
                logger.LogInformation("Downstream handler executed.");
                return Task.CompletedTask;
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);

        observedCorrelationId.Should().NotBeNullOrWhiteSpace();
        captured.Should().ContainSingle();
        var attributes = captured[0];
        attributes.Should().NotBeNull();
        attributes!.Should().Contain(kv =>
            kv.Key == CorrelationIdMiddleware.BaggageKey && Equals(kv.Value, observedCorrelationId));
    }

    private sealed class NoOpHttpResponseFeature : HttpResponseFeature
    {
        public override void OnStarting(Func<object, Task> callback, object state)
        {
            // Test double: OnStarting registration is irrelevant to this test's assertions.
        }
    }

    /// <summary>
    /// Test-local mirror of <c>SharedKernel.ServiceDefaults.Telemetry.BaggageLogRecordProcessor</c>'s
    /// documented contract — generic <see cref="Activity.Baggage"/> → <see cref="LogRecord.Attributes"/>
    /// copy, never overwriting an existing attribute. Deliberately duplicated here rather than
    /// referenced via <c>ProjectReference</c>, per this domain's non-dependency rule on
    /// <c>13.ServiceDefaults</c>.
    /// </summary>
    private sealed class TestBaggageLogRecordProcessor : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            if (Activity.Current is not { } activity)
            {
                return;
            }

            var existingAttributes = data.Attributes;
            var mergedAttributes = new List<KeyValuePair<string, object?>>(existingAttributes?.Count ?? 0);

            if (existingAttributes is not null)
            {
                mergedAttributes.AddRange(existingAttributes);
            }

            var existingKeys = new HashSet<string>(
                mergedAttributes.Select(attribute => attribute.Key),
                StringComparer.Ordinal);

            foreach (var (key, value) in activity.Baggage)
            {
                if (existingKeys.Add(key))
                {
                    mergedAttributes.Add(new KeyValuePair<string, object?>(key, value));
                }
            }

            data.Attributes = mergedAttributes;
        }
    }

    /// <summary>Copies a <see cref="LogRecord"/>'s attributes at <see cref="OnEnd"/> time, since the
    /// SDK may pool/reset the <see cref="LogRecord"/> instance after the pipeline completes.</summary>
    private sealed class CapturingProcessor(List<IReadOnlyList<KeyValuePair<string, object?>>?> sink)
        : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data) =>
            sink.Add(data.Attributes is null ? null : [.. data.Attributes]);
    }
}
