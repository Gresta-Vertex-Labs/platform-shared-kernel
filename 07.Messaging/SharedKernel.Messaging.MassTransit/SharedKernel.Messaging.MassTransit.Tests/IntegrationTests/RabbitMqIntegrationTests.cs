using DotNet.Testcontainers.Builders;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;
using Testcontainers.RabbitMq;

namespace SharedKernel.Messaging.MassTransit.Tests.IntegrationTests;

/// <summary>
/// T-13: RabbitMQ Testcontainers integration test.
/// End-to-end publish and consume across a real RabbitMQ broker.
/// Marked with [Trait("Category", "Integration")] so CI can filter when Docker is unavailable.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task PublishAndConsume_RealBroker_MessageDelivered()
    {
        var connectionString = _container.GetConnectionString();

        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "integration-test-service")
            .UseRabbitMq(connectionString)
            .WithRetry()
            .AddConsumer<RabbitIntegrationConsumer>()
            .Build();

        await using var provider = services.BuildServiceProvider(true);

        // Start the MassTransit hosted service
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .ToList();

        foreach (var svc in hosted)
            await svc.StartAsync(CancellationToken.None);

        // Give the bus a moment to connect and bind the queue
        await Task.Delay(500);

        var bus = provider.GetRequiredService<IBus>();

        // Publish via IBus (raw MassTransit) — test the broker round-trip
        RabbitIntegrationConsumer.Reset();
        var message = new RabbitTestMessage("end-to-end-payload");
        await bus.Publish(message);

        // Wait up to 10 seconds for the message to be consumed
        var received = await RabbitIntegrationConsumer.WaitAsync(TimeSpan.FromSeconds(10));
        received.Should().BeTrue("message published to real RabbitMQ should be consumed");

        RabbitIntegrationConsumer.LastMessage.Should().NotBeNull();
        RabbitIntegrationConsumer.LastMessage!.Text.Should().Be("end-to-end-payload");

        foreach (var svc in hosted)
            await svc.StopAsync(CancellationToken.None);
    }
}

// ---------------------------------------------------------------------------
// Test consumer and message types
// ---------------------------------------------------------------------------

file sealed class RabbitIntegrationConsumer : ConsumerBase<RabbitTestMessage>
{
    private static TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static RabbitTestMessage? LastMessage { get; private set; }

    public static void Reset()
    {
        LastMessage = null;
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static Task<bool> WaitAsync(TimeSpan timeout)
    {
        return Task.WhenAny(_tcs.Task, Task.Delay(timeout).ContinueWith(_ => false))
            .ContinueWith(t => t.Result.Result);
    }

    public RabbitIntegrationConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(RabbitTestMessage message, CancellationToken ct)
    {
        LastMessage = message;
        _tcs.TrySetResult(true);
        return Task.CompletedTask;
    }
}

file sealed record RabbitTestMessage(string Text);
