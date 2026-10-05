using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Transports;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Messaging.MassTransit.EfCore.Integration.Tests;

[CollectionDefinition(Name)]
public sealed class OutboxPostgresCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
    public const string Name = "OutboxPostgres";
}

/// <summary>
/// The EF Core outbox end to end on PostgreSQL, the platform's database: an event published inside a
/// <c>SaveChanges</c> is stored as an outbox row, and MassTransit's bus outbox delivery service — which locks
/// outbox rows with provider-specific SQL — must then deliver it to a consumer.
/// </summary>
/// <remarks>
/// MassTransit's EF Core outbox locks with SQL Server syntax unless a lock provider is chosen, so without
/// <c>OutboxDatabase.PostgreSql</c> the delivery service fails on every poll and nothing is ever delivered.
/// The SQLite tests in <c>SharedKernel.Messaging.MassTransit.EfCore.Tests</c> never ran the delivery service,
/// which is how that went unnoticed.
/// </remarks>
[Collection(OutboxPostgresCollection.Name)]
public sealed class PostgresOutboxDeliveryTests(PostgreSqlContainerFixture fixture)
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task DefaultOptions_EventSavedThroughTheOutbox_IsDeliveredToTheConsumer()
    {
        await using var database = await fixture.Server.CreateDatabaseAsync();
        var received = new DeliveredEvents();
        var logs = new ErrorLogCollector();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs));
        services.AddSingleton(received);
        services.AddDbContext<PostgresOutboxDbContext>(o => o.UseNpgsql(database.AdminConnectionString));
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "outbox-postgres-test")
            .UseTransport(new InMemoryTestTransport())
            .AddConsumer<OrderPlacedConsumer>()
            .WithEntityFrameworkOutbox<PostgresOutboxDbContext>(o => o.QueryDelay = TimeSpan.FromMilliseconds(100))
            .Build();

        await using var provider = services.BuildServiceProvider(validateScopes: true);

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PostgresOutboxDbContext>().Database.EnsureCreatedAsync();

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None);

        try
        {
            var orderId = Guid.NewGuid();
            using (var scope = provider.CreateScope())
            {
                var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
                var db = scope.ServiceProvider.GetRequiredService<PostgresOutboxDbContext>();

                var published = await publisher.PublishAsync(
                    new OrderPlacedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, orderId),
                    CancellationToken.None);
                published.IsSuccess.Should().BeTrue();

                await db.SaveChangesAsync();
            }

            var delivered = await Task.WhenAny(received.First.Task, Task.Delay(DeliveryTimeout));

            delivered.Should().BeSameAs(
                received.First.Task,
                "the bus outbox delivery service must deliver the saved outbox row within {0}; errors logged: {1}",
                DeliveryTimeout,
                logs.Describe());
            (await received.First.Task).Should().Be(orderId);
        }
        finally
        {
            for (var i = hostedServices.Count - 1; i >= 0; i--)
                await hostedServices[i].StopAsync(CancellationToken.None);
        }
    }
}

/// <summary>MassTransit's in-memory transport, with the platform pipeline applied as a real transport would.</summary>
internal sealed class InMemoryTestTransport() : MessagingTransport("in-memory")
{
    public override void Configure(IBusRegistrationConfigurator configurator, MessagingTransportSettings settings)
        => configurator.UsingInMemory((context, bus) => settings.ConfigureBus(context, bus));
}

internal sealed class PostgresOutboxDbContext(DbContextOptions<PostgresOutboxDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

internal sealed class DeliveredEvents
{
    public TaskCompletionSource<Guid> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class OrderPlacedConsumer(DeliveredEvents delivered) : IConsumer<EventEnvelope<OrderPlacedEvent>>
{
    public Task Consume(ConsumeContext<EventEnvelope<OrderPlacedEvent>> context)
    {
        delivered.First.TrySetResult(context.Message.Data.OrderId);
        return Task.CompletedTask;
    }
}

[IntegrationEvent("tests.messaging.outbox.order-placed")]
internal sealed record OrderPlacedEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;

/// <summary>Collects warnings and errors so a failed delivery reports why.</summary>
internal sealed class ErrorLogCollector : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public ILogger CreateLogger(string categoryName) => new Collector(categoryName, _entries);

    public string Describe() => _entries.IsEmpty ? "(none)" : string.Join(" | ", _entries.Take(3));

    public void Dispose()
    {
    }

    private sealed class Collector(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                entries.Enqueue($"{category}: {formatter(state, exception)} {exception?.GetType().Name}: {exception?.Message}");
        }
    }
}
