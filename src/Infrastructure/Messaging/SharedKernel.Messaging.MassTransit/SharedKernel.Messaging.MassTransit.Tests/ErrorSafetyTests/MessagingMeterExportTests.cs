#pragma warning disable CS8602 // MassTransit harness nullable context
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.ServiceDefaults.Extensions;

namespace SharedKernel.Messaging.MassTransit.Tests.ErrorSafetyTests;

/// <summary>
/// SK.07.MessagingErrorSafety T-06: the <c>SharedKernel.Messaging</c> meter, owned by this package, reaches the exporter of a
/// host built with only <c>AddServiceDefaults()</c> (the <c>SharedKernel.*</c> wildcard), while a meter outside the prefix does not.
/// </summary>
/// <remarks>
/// Instrument names are those of today (<c>messaging.publish.count</c>, <c>messaging.consume.count</c>);
/// <c>SK.07.ObservabilityConformance</c> renames them to <c>sharedkernel.messaging.*</c> and updates this test.
/// The meter is a process-wide static, so every assertion filters by this test's unique message type tag.
/// </remarks>
public sealed class MessagingMeterExportTests
{
    private const string PublishCount = "sharedkernel.messaging.publish.count";
    private const string ConsumeCount = "sharedkernel.messaging.consume.count";
    private const string MessageTypeTag = "messaging.message_type";

    [Fact]
    public async Task ServiceDefaultsHost_ExportsTheMessagingMeter_AndNotOneOutsideThePrefix()
    {
        var snapshots = new List<MetricSnapshot>();

        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddInMemoryExporter(snapshots));

        builder.Services.AddSharedKernelMessaging(o => o.ServiceName = "meter-proof-service");
        builder.Services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<MeterProofConsumer>();
            cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
        });
        // MassTransitMessageBus's collaborators, which Build() would otherwise register; the harness replaces the transport only.
        builder.Services.AddSingleton<IReadOnlyDictionary<Type, string>>(
            new ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()));
        builder.Services.AddScoped<ConventionSendEndpointResolver>();
        builder.Services.AddScoped<IMessageBus, MassTransitMessageBus>();

        await using var provider = builder.Services.BuildServiceProvider(true);
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start(); // returns once the bus is started and its endpoints are bound

        await using (var scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            (await bus.PublishAsync(new MeterProofMessage("exported"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        (await harness.Consumed.Any<MeterProofMessage>()).Should().BeTrue();

        using var outsider = new Meter("Other.Messaging");
        outsider.CreateCounter<long>(PublishCount)
            .Add(1, new KeyValuePair<string, object?>(MessageTypeTag, nameof(MeterProofMessage)));

        snapshots.Clear();
        // The result is not asserted: the host defaults also register an OTLP reader with no collector to reach.
        meterProvider.ForceFlush();

        snapshots.Select(s => s.MeterName).Should().Contain("SharedKernel.Messaging");
        snapshots.Select(s => s.MeterName).Should().NotContain("Other.Messaging",
            "only SharedKernel.* meters are exported by the host defaults");

        SumFor(snapshots, PublishCount).Should().BeGreaterThan(0, "the publish counter recorded this test's publish");
        SumFor(snapshots, ConsumeCount).Should().BeGreaterThan(0, "the consume counter recorded this test's consume");

        await harness.Stop();
    }

    private static long SumFor(IEnumerable<MetricSnapshot> snapshots, string instrument)
    {
        long sum = 0;
        foreach (var snapshot in snapshots.Where(s => s.MeterName == "SharedKernel.Messaging" && s.Name == instrument))
        {
            foreach (var point in snapshot.MetricPoints)
            {
                var matches = false;
                foreach (var tag in point.Tags)
                {
                    if (tag.Key == MessageTypeTag && Equals(tag.Value, nameof(MeterProofMessage)))
                        matches = true;
                }

                if (matches)
                    sum += point.GetSumLong();
            }
        }

        return sum;
    }
}

public sealed record MeterProofMessage(string Text);

public sealed class MeterProofConsumer() : ConsumerBase<MeterProofMessage>(NullLogger.Instance)
{
    protected override Task ConsumeAsync(MeterProofMessage message, CancellationToken ct) => Task.CompletedTask;
}
