#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.EventPublisher;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.Transports;

namespace SharedKernel.Messaging.MassTransit.EfCore.Tests.HarnessTests;

/// <summary>
/// T-12: Outbox round-trip test on SQLite.
/// Verifies: outbox row inserted before SaveChangesAsync commit.
///
/// Notes on SQLite limitations:
/// - The MassTransit outbox delivery worker requires RepeatableRead isolation and nested transactions
///   which SQLite does not support. Full end-to-end delivery is therefore not testable with SQLite.
/// - The outbox row-insertion test verifies the write side of the outbox pattern without starting
///   the delivery background worker.
/// - The direct-publish test verifies IEventPublisher wraps events correctly in EventEnvelope&lt;T&gt;.
/// </summary>
public sealed class OutboxRoundTripTests : IDisposable
{
    // Use a persistent SQLite connection so the in-memory database survives across scopes.
    private readonly SqliteConnection _keepAliveConnection;

    public OutboxRoundTripTests()
    {
        _keepAliveConnection = new SqliteConnection("Data Source=:memory:");
        _keepAliveConnection.Open();
    }

    public void Dispose() => _keepAliveConnection.Dispose();

    [Fact]
    public async Task OutboxEnabled_Publish_InsertsOutboxRowOnSaveChanges()
    {
        // Build provider with outbox but WITHOUT starting the TestHarness to avoid
        // the delivery worker triggering SQLite nested-transaction failures.
        await using var provider = BuildProviderForRowInsertionTest(_keepAliveConnection);

        // Create the schema
        using (var initScope = provider.CreateScope())
        {
            var db = initScope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        // Act: publish within a scope — MassTransit outbox stages the message during SaveChangesAsync
        using (var scope = provider.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            var db = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();

            var evt = new ItemShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

            // The outbox row is written atomically during SaveChangesAsync via MassTransit's
            // OutboxSaveChangesObserver intercepting the EF Core pipeline.
            await publisher.PublishAsync(evt, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        // Verify the outbox row was inserted (delivery worker would pick it up in production)
        using (var verifyScope = provider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var rowCount = await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS \"Value\" FROM \"OutboxMessage\"").SingleAsync();
            rowCount.Should().Be(1,
                "SaveChangesAsync must atomically write one outbox row for the published event");
        }
    }

    [Fact]
    public async Task OutboxEnabled_PublishedEvent_DirectPublishWorksThroughHarness()
    {
        // Verifies that IEventPublisher delivers via the in-memory TestHarness when no outbox is configured.
        // This confirms the MassTransitEventPublisher wraps events in EventEnvelope correctly.
        await using var provider = BuildProviderWithoutOutbox();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var expectedItemId = Guid.NewGuid();
        var evt = new ItemShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, expectedItemId);

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<ItemShippedEvent>>()).Should().BeTrue(
            "IEventPublisher must publish EventEnvelope<TEvent> to the MassTransit bus");

        var published = harness.Published.Select<EventEnvelope<ItemShippedEvent>>().First();
        published.Context.Message.Data.ItemId.Should().Be(expectedItemId);
        published.Context.Message.Type.Should().Be("tests.messaging.outbox.item-shipped");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal DI provider with the outbox EF entity registration and IEventPublisher.
    /// Does NOT register TestHarness (no bus is started) — avoids delivery-worker nested-tx issue.
    /// </summary>
    private static ServiceProvider BuildProviderForRowInsertionTest(SqliteConnection connection)
    {
        var services = new ServiceCollection();

        services.AddDbContext<OutboxTestDbContext>(opts =>
            opts.UseSqlite(connection));

        // The package's own registration, on SQLite; no hosted service is started, so the bus and the
        // delivery worker never run.
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "outbox-test-service")
            .UseTransport(new InMemoryTestTransport())
            .AddConsumer<ItemShippedConsumer>()
            .WithEntityFrameworkOutbox<OutboxTestDbContext>(o => o.Database = OutboxDatabase.Sqlite)
            .Build();

        return services.BuildServiceProvider(true);
    }

    /// <summary>
    /// Builds a provider without outbox — direct publish to in-memory test harness.
    /// </summary>
    private static ServiceProvider BuildProviderWithoutOutbox()
    {
        var services = new ServiceCollection();

        services.Configure<MessagingOptions>(o => o.ServiceName = "outbox-test-service");
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<ItemShippedConsumer>();
        });

        return services.BuildServiceProvider(true);
    }
}

// ---------------------------------------------------------------------------
// Test DbContext — NOT 'file' scoped so MassTransit type resolution works
// ---------------------------------------------------------------------------

internal sealed class OutboxTestDbContext : DbContext
{
    public OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

/// <summary>MassTransit's in-memory transport, with the platform pipeline applied as a real transport would.</summary>
internal sealed class InMemoryTestTransport() : MessagingTransport("in-memory")
{
    public override void Configure(IBusRegistrationConfigurator configurator, MessagingTransportSettings settings)
        => configurator.UsingInMemory((context, bus) => settings.ConfigureBus(context, bus));
}

// ---------------------------------------------------------------------------
// Consumer
// ---------------------------------------------------------------------------

internal sealed class ItemShippedConsumer : ConsumerBase<EventEnvelope<ItemShippedEvent>>
{
    public ItemShippedConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(EventEnvelope<ItemShippedEvent> message, CancellationToken ct)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Integration event
// ---------------------------------------------------------------------------

[IntegrationEvent("tests.messaging.outbox.item-shipped")]
internal sealed record ItemShippedEvent(Guid EventId, DateTimeOffset OccurredOn, Guid ItemId) : IIntegrationEvent;
