using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.MassTransit.Extensions;
using Testcontainers.RabbitMq;

namespace SharedKernel.Messaging.MassTransit.Tests.IntegrationTests;

/// <summary>
/// OD-07: RabbitMQ Testcontainers integration test — publishes messages sharing one
/// <c>PartitionKey</c> interleaved with messages under a different key against a real broker, and
/// asserts a single consumer instance observes each key's own messages in publish order.
/// </summary>
/// <remarks>
/// Ordering is asserted per-key, not across the combined interleaved stream — the platform's own
/// documented guarantee (see <c>07.Messaging/CLAUDE.md</c> "Ordered delivery via partition key") holds
/// only "among messages sharing the same PartitionKey and consumed by a single active consumer
/// instance", never a total order across unrelated keys. The consumer's endpoint is pinned to
/// <c>ConcurrentMessageLimit = 1</c> so ConsumeAsync completions cannot race and reorder relative to
/// RabbitMQ's own FIFO delivery order.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class OrderedDeliveryIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task PublishAsync_WithPartitionKey_PreservesPerKeyPublishOrder_AcrossRealBroker()
    {
        const int messagesPerKey = 5;
        var connectionString = _container.GetConnectionString();

        var services = new ServiceCollection();
        services.AddLogging(); // MassTransit's default health-check hosted service needs ILogger<T>
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "ordered-delivery-test-service")
            .UseRabbitMq(connectionString)
            .WithRetry()
            .AddConsumer<OdRecordingConsumer, OdRecordingConsumerDefinition>()
            .Build();

        await using var provider = services.BuildServiceProvider(true);

        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var svc in hosted)
            await svc.StartAsync(CancellationToken.None);

        // Give the bus a moment to connect and bind the queue.
        await Task.Delay(500);

        // IMessageBus is a scoped service — resolve it from an explicit scope, not the root provider.
        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        OdRecordingConsumer.Reset(expectedCount: messagesPerKey * 2);

        // Interleave two partition keys' messages at publish time.
        for (var i = 0; i < messagesPerKey; i++)
        {
            await bus.PublishAsync(
                new OdMessage("order-a", i),
                ctx => ctx.WithPartitionKey("order-a"),
                CancellationToken.None);
            await bus.PublishAsync(
                new OdMessage("order-b", i),
                ctx => ctx.WithPartitionKey("order-b"),
                CancellationToken.None);
        }

        var completed = await OdRecordingConsumer.WaitAsync(TimeSpan.FromSeconds(20));
        completed.Should().BeTrue("all published messages should be consumed by the real broker");

        var keyASequence = OdRecordingConsumer.Received
            .Where(m => m.Key == "order-a")
            .Select(m => m.Sequence)
            .ToList();
        var keyBSequence = OdRecordingConsumer.Received
            .Where(m => m.Key == "order-b")
            .Select(m => m.Sequence)
            .ToList();

        keyASequence.Should().Equal(
            Enumerable.Range(0, messagesPerKey), "order-a's own messages must be observed in publish order");
        keyBSequence.Should().Equal(
            Enumerable.Range(0, messagesPerKey), "order-b's own messages must be observed in publish order");

        foreach (var svc in hosted)
            await svc.StopAsync(CancellationToken.None);
    }
}

// ---------------------------------------------------------------------------
// Test message, consumer, and definition — internal, no 'file' modifier
// ---------------------------------------------------------------------------

internal sealed record OdMessage(string Key, int Sequence);

internal sealed class OdRecordingConsumer : IConsumer<OdMessage>
{
    private static readonly object _lock = new();
    private static TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static int _expected;

    public static List<OdMessage> Received { get; } = [];

    public static void Reset(int expectedCount)
    {
        lock (_lock)
        {
            Received.Clear();
            _expected = expectedCount;
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public static Task<bool> WaitAsync(TimeSpan timeout)
    {
        return Task.WhenAny(_tcs.Task, Task.Delay(timeout).ContinueWith(_ => false))
            .ContinueWith(t => t.Result.Result);
    }

    public Task Consume(ConsumeContext<OdMessage> context)
    {
        lock (_lock)
        {
            Received.Add(context.Message);
            if (Received.Count >= _expected)
                _tcs.TrySetResult(true);
        }
        return Task.CompletedTask;
    }
}

/// Pins this consumer's endpoint to single-threaded consumption so ConsumeAsync completions
/// cannot race and reorder relative to RabbitMQ's own FIFO delivery order (P-344/WO-054, OD-07).
internal sealed class OdRecordingConsumerDefinition : SharedKernel.Messaging.MassTransit.Consumers.ConsumerDefinitionBase<OdRecordingConsumer>
{
    protected override int? ConcurrentMessageLimit => 1;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OdRecordingConsumer> consumerConfigurator,
        IBusRegistrationContext context)
    {
        // No additional configuration needed — the base class applies ConcurrentMessageLimit.
    }
}
