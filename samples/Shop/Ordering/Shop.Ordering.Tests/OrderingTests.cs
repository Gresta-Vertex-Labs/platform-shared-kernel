using FluentAssertions;
using Grpc.Core;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Testing;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Application;
using SharedKernel.Testing.Caching;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Communication;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Messaging;
using SharedKernel.Testing.Workflows;
using Shop.Contracts.Inventory;
using Shop.Contracts.Ordering;
using Shop.Ordering.Api;
using Shop.Ordering.Application;
using Shop.Ordering.Domain;
using Shop.Ordering.Infrastructure;
using Shop.Ordering.Infrastructure.Fulfilment;
using Shop.Ordering.Infrastructure.Inventory;
using Shop.TestSupport;
using Xunit;

namespace Shop.Ordering.Tests;

public sealed class OrderingArchitectureTests
{
    private static readonly ServiceShape Shape = new(
        DependencyGraph.Load("Shop.Ordering.Tests"),
        "Shop.Ordering"
    );

    [Fact]
    public void ProjectReferences_FollowTheLayering() =>
        Shape.ProjectReferencesFollowTheLayering("Shop.Contracts");

    [Fact]
    public void Domain_SeesFoundationAndModelOnly() => Shape.DomainSeesFoundationAndModelOnly();

    [Fact]
    public void Application_SeesContractsOnly() => Shape.ApplicationSeesContractsOnly();

    [Fact]
    public void Infrastructure_SeesAdaptersButNoHost() =>
        Shape.InfrastructureSeesAdaptersButNoHost();

    [Fact]
    public void Api_IsTheCompositionRoot() => Shape.HostIsTheCompositionRoot();

    [Fact]
    public void Production_NeverReferencesTestingPackages() =>
        Shape.ProductionNeverReferencesTestingPackages();

    [Fact]
    public void EveryKernelPackage_IsKnown() => Shape.EveryKernelPackageIsKnown();
}

public sealed class OrderingPipelineTests : IDisposable
{
    private static readonly TenantId Contoso = new(
        Guid.Parse("6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11")
    );

    private readonly ApplicationPipelineTestHarness _harness = new();
    private readonly FakeRepository<Order, OrderId> _orders;
    private readonly FakeAuditTrailWriter _audit;
    private readonly InMemoryEventPublisher _events = new();
    private readonly IInventoryReservations _inventory = Substitute.For<IInventoryReservations>();
    private readonly IPayments _payments = Substitute.For<IPayments>();

    public OrderingPipelineTests()
    {
        var services = _harness.Services;
        services.AddFakeApplicationBehaviorServices();
        services.AddSingleton<IRequestContext>(
            TestRequestContext
                .ForTenant(Contoso)
                .WithPermissions(
                    OrderingPermissions.Place,
                    OrderingPermissions.Read,
                    OrderingPermissions.Cancel
                )
        );
        services.AddSingleton<IClock>(new FakeClock());
        _orders = services.AddFakeRepository<Order, OrderId>();
        _audit = services.AddFakeAuditTrailWriter();
        services.AddScoped<SharedKernel.Messaging.Abstractions.EventPublisher.IEventPublisher>(_ =>
            _events
        );
        services.AddSingleton(_inventory);
        services.AddSingleton(_payments);
        services.AddSingleton(Substitute.For<IOrderStatusNotifier>());
        _harness
            .Configure(app => app.WithIdempotency().WithTransactions().WithAuditing())
            .Build<PlaceOrderCommand>();
    }

    [Fact]
    public async Task PlaceOrder_SameKeyTwice_PlacesOnce_PublishesOnce_AndReplaysTheFirstAnswer()
    {
        var first = await _harness.SendAsync(Place("key-1"));
        var second = await _harness.SendAsync(Place("key-1"));

        second.Value.Should().Be(first.Value);
        _orders.Items.Should().ContainSingle();
        _events
            .PublishedOf<OrderPlaced>()
            .Should()
            .ContainSingle()
            .Which.OrderId.Should()
            .Be(first.Value);
    }

    [Fact]
    public async Task PlaceOrder_IsAudited()
    {
        var placed = await _harness.SendAsync(Place("key-audit"));

        placed.IsSuccess.Should().BeTrue();
        _audit.Recorded.Should().Contain(entry => entry.Action == "order.placed");
    }

    [Fact]
    public async Task PlaceOrder_WithoutLines_FailsValidation()
    {
        var placed = await _harness.SendAsync(Place("key-empty") with { Lines = [] });

        placed.IsFailure.Should().BeTrue();
        placed.Error.Type.Should().Be(ErrorType.Validation);
        _orders.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelConfirmedOrder_RefundsThePayment_ReleasesTheStock_AndAnnouncesIt()
    {
        var id = (await _harness.SendAsync(Place("key-cancel"))).Value;
        (await _harness.SendAsync(new ConfirmOrderCommand(id, Guid.NewGuid())))
            .IsSuccess.Should()
            .BeTrue();
        _payments
            .RefundAsync(Arg.Any<OrderId>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _inventory
            .ReleaseAsync(Arg.Any<OrderId>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));

        var cancelled = await _harness.SendAsync(new CancelOrderCommand(id));

        cancelled.IsSuccess.Should().BeTrue();
        _ = _payments.Received(1).RefundAsync(new OrderId(id), Arg.Any<CancellationToken>());
        _ = _inventory.Received(1).ReleaseAsync(new OrderId(id), Arg.Any<CancellationToken>());
        _events.PublishedOf<OrderCancelled>().Should().ContainSingle();
    }

    [Fact]
    public async Task CancelPlacedOrder_IsAConflict_AndTouchesNoStock()
    {
        var id = (await _harness.SendAsync(Place("key-early-cancel"))).Value;

        var cancelled = await _harness.SendAsync(new CancelOrderCommand(id));

        cancelled.Error.Code.Should().Be("ordering.order.invalid_transition");
        _ = _inventory.DidNotReceiveWithAnyArgs().ReleaseAsync(default!, default);
    }

    [Fact]
    public async Task Cancel_WhenTheRefundFails_FailsWithoutReleasingTheStock()
    {
        var id = (await _harness.SendAsync(Place("key-refund-fails"))).Value;
        (await _harness.SendAsync(new ConfirmOrderCommand(id, Guid.NewGuid())))
            .IsSuccess.Should()
            .BeTrue();
        _payments
            .RefundAsync(Arg.Any<OrderId>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Unavailable("billing.unavailable", "Billing is down.")));

        var cancelled = await _harness.SendAsync(new CancelOrderCommand(id));

        cancelled.Error.Code.Should().Be("billing.unavailable");
        // The command fails, so its transaction rolls back (the fake repository cannot show that; Shop.E2E can).
        _ = _inventory.DidNotReceiveWithAnyArgs().ReleaseAsync(default!, default);
    }

    [Fact]
    public async Task Charge_TakesTheOrdersTotalAndEmail_AndADeclineStaysABusinessRule()
    {
        var id = (await _harness.SendAsync(Place("key-charge"))).Value;
        _payments
            .ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<Guid>.Failure(
                    Error.BusinessRule("billing.payment_declined", "The card was declined.")
                )
            );

        var charged = await _harness.SendAsync(new ChargeOrderCommand(id, "tok_declined"));

        // The workflow tells a refusal from an outage by this type: business rules are not retried.
        charged.Error.Type.Should().Be(ErrorType.BusinessRule);
        _ = _payments
            .Received(1)
            .ChargeAsync(
                new PaymentRequest(
                    new OrderId(id),
                    25.00m,
                    "EUR",
                    "customer@contoso.example",
                    "tok_declined"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    public void Dispose() => _harness.Dispose();

    private static PlaceOrderCommand Place(string key) =>
        new(
            "customer@contoso.example",
            "1 Main Street",
            "EUR",
            [new PlaceOrderLine("SKU-1", 2, 12.50m)],
            "tok_visa",
            key
        );
}

public sealed class GrpcInventoryReservationsTests
{
    [Fact]
    public async Task InventoryRefusal_ArrivesAsTheSameBusinessRuleError()
    {
        var client = Substitute.For<InventoryService.InventoryServiceClient>();
        client
            .ReserveStockAsync(
                Arg.Any<ReserveStockRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                GrpcCalls.Failure<ReserveStockReply>(
                    Error.BusinessRule("inventory.insufficient_stock", "Not enough.")
                )
            );

        var reserved = await new GrpcInventoryReservations(client).ReserveAsync(
            OrderId.New(),
            [("SKU-1", 3)],
            CancellationToken.None
        );

        reserved.Error.Type.Should().Be(ErrorType.BusinessRule);
        reserved.Error.Code.Should().Be("inventory.insufficient_stock");
    }

    [Fact]
    public async Task Reservation_ReturnsTheReservationId()
    {
        var id = Guid.NewGuid();
        var client = Substitute.For<InventoryService.InventoryServiceClient>();
        client
            .ReserveStockAsync(
                Arg.Any<ReserveStockRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(GrpcCalls.Success(new ReserveStockReply { ReservationId = id.ToString() }));

        var reserved = await new GrpcInventoryReservations(client).ReserveAsync(
            OrderId.New(),
            [("SKU-1", 1)],
            CancellationToken.None
        );

        reserved.Value.Should().Be(id);
    }
}

public sealed class TotpStoreTests
{
    private readonly IServiceProvider _services = new ServiceCollection()
        .AddFakeRedisServices()
        .BuildServiceProvider();

    [Fact]
    public async Task ReplayGuard_AcceptsEachTimeStepOnce()
    {
        var guard = new RedisTotpReplayGuard(
            _services.GetRequiredService<SharedKernel.Caching.Redis.HashStore.IRedisHashService>()
        );

        (await guard.TryAcceptTimeStepAsync("alice", 100, TimeSpan.FromMinutes(2)))
            .Should()
            .BeTrue();
        (await guard.TryAcceptTimeStepAsync("alice", 100, TimeSpan.FromMinutes(2)))
            .Should()
            .BeFalse("a code is used once");
        (await guard.TryAcceptTimeStepAsync("alice", 101, TimeSpan.FromMinutes(2)))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task Throttle_StopsAfterFiveAttempts()
    {
        var throttle = new RedisTotpAttemptThrottle(
            _services.GetRequiredService<SharedKernel.Caching.Redis.HashStore.IRedisHashService>()
        );

        for (int i = 0; i < 5; i++)
        {
            (await throttle.IsThrottledAsync("alice")).Should().BeFalse();
            await throttle.RecordAttemptAsync("alice");
        }

        (await throttle.IsThrottledAsync("alice")).Should().BeTrue();
    }

    [Fact]
    public async Task StepUpStore_RemembersTheSessionsStepUp()
    {
        var store = new RedisTotpStepUpStore(
            _services.GetRequiredService<SharedKernel.Caching.Redis.HashStore.IRedisHashService>()
        );
        var at = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

        await store.RecordAsync("alice", "session-1", at, at.AddMinutes(5), CancellationToken.None);

        (await store.GetLastVerifiedAsync("alice", "session-1", CancellationToken.None))
            .Should()
            .Be(at);
        (await store.GetLastVerifiedAsync("alice", "session-2", CancellationToken.None))
            .Should()
            .BeNull();
    }
}

public sealed class OrderPlacedConsumerTests
{
    private static readonly TenantId Contoso = new(
        Guid.Parse("6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11")
    );

    [Fact]
    public async Task PlacedOrder_StartsOneFulfilment_WithItsLinesAndPaymentToken_ForItsTenant()
    {
        var workflows = new InMemoryWorkflowDispatcher();
        var consumer = new OrderPlacedConsumer(
            workflows,
            TestRequestContext.ForTenant(Contoso),
            NullLogger<OrderPlacedConsumer>.Instance
        );
        var placed = new OrderPlaced(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            Guid.NewGuid(),
            [new OrderLineContract("SKU-1", 2, 12.50m)],
            25.00m,
            "EUR",
            "tok_visa"
        );
        var context = Substitute.For<ConsumeContext<EventEnvelope<OrderPlaced>>>();
        context.Message.Returns(EventEnvelope.Wrap(placed, "ordering"));

        await consumer.Consume(context);
        // A redelivery finds the workflow already started and is acknowledged, not started twice.
        await consumer.Consume(context);

        var started = workflows.ShouldHaveStartedOnce<OrderFulfilmentWorkflow>();
        started.TenantScope.Should().Be(TenantScope.For(Contoso));
        started
            .Args.Should()
            .BeEquivalentTo(
                new FulfilmentRequest(placed.OrderId, [new FulfilmentLine("SKU-1", 2)], "tok_visa")
            );
    }
}
