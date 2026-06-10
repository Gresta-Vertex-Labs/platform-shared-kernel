using FluentAssertions;
using MassTransit;
using MassTransit.Courier.Contracts;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Messaging.MassTransit.RoutingSlips;
using ISkRoutingSlipBuilder = SharedKernel.Messaging.Abstractions.RoutingSlips.IRoutingSlipBuilder;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// RS-08 / RS-09: TestHarness routing slip tests.
/// RS-08 verifies a two-activity routing slip executes both activities in order with the
/// correct arguments. RS-09 verifies that when the second activity faults, the first
/// activity's compensation runs and <see cref="RoutingSlipFaulted"/> is published.
/// </summary>
public sealed class RoutingSlipTests
{
    // -------------------------------------------------------------------------
    // RS-08: Two-activity routing slip — ordered execution with correct arguments
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteRoutingSlip_WithTwoActivities_ExecutesBothInOrderWithCorrectArguments()
    {
        var recorder = new ExecutionRecorder();

        await using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddActivity<ReserveInventoryActivity, ReserveInventoryArguments, ReserveInventoryLog>();
                cfg.AddActivity<ChargeCardActivity, ChargeCardArguments, ChargeCardLog>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var reserveAddress = harness.GetExecuteActivityAddress<ReserveInventoryActivity, ReserveInventoryArguments>();
        var chargeAddress = harness.GetExecuteActivityAddress<ChargeCardActivity, ChargeCardArguments>();

        var orderId = Guid.NewGuid();

        ISkRoutingSlipBuilder slipBuilder = new MassTransitRoutingSlipBuilder();
        var slip = slipBuilder
            .AddActivity("reserve-inventory", reserveAddress, new ReserveInventoryArguments { OrderId = orderId, Sku = "SKU-1", Quantity = 2 })
            .AddActivity("charge-card", chargeAddress, new ChargeCardArguments { OrderId = orderId, Amount = 49.99m })
            .Build();

        var bus = new MassTransitMessageBus(
            harness.Bus,
            harness.Bus,
            provider,
            new Dictionary<Type, string>(),
            new ConventionSendEndpointResolver(
                Microsoft.Extensions.Options.Options.Create(
                    new SharedKernel.Messaging.Abstractions.Options.MessagingOptions { ServiceName = "test-service" })));

        await bus.ExecuteRoutingSlipAsync(slip, CancellationToken.None);

        await harness.InactivityTask;

        (await harness.Published.Any<RoutingSlipCompleted>()).Should().BeTrue(
            "the routing slip must complete successfully when both activities succeed");

        recorder.ExecutedSteps.Should().HaveCount(2,
            "both activities must execute exactly once");

        recorder.ExecutedSteps[0].Should().Be("reserve-inventory:SKU-1:2",
            "the first activity must execute first with the correct arguments");

        recorder.ExecutedSteps[1].Should().Be(
            $"charge-card:{49.99m.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "the second activity must execute second with the correct arguments");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // RS-09: Compensation chain — second activity faults, first activity compensates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteRoutingSlip_WhenSecondActivityFaults_CompensatesFirstActivityAndPublishesFaulted()
    {
        var recorder = new ExecutionRecorder();

        await using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddActivity<ReserveInventoryActivity, ReserveInventoryArguments, ReserveInventoryLog>();
                cfg.AddActivity<FailingChargeCardActivity, ChargeCardArguments, ChargeCardLog>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var reserveAddress = harness.GetExecuteActivityAddress<ReserveInventoryActivity, ReserveInventoryArguments>();
        var chargeAddress = harness.GetExecuteActivityAddress<FailingChargeCardActivity, ChargeCardArguments>();

        var orderId = Guid.NewGuid();

        ISkRoutingSlipBuilder slipBuilder = new MassTransitRoutingSlipBuilder();
        var slip = slipBuilder
            .AddActivity("reserve-inventory", reserveAddress, new ReserveInventoryArguments { OrderId = orderId, Sku = "SKU-2", Quantity = 1 })
            .AddActivity("charge-card-failing", chargeAddress, new ChargeCardArguments { OrderId = orderId, Amount = 10m })
            .Build();

        var bus = new MassTransitMessageBus(
            harness.Bus,
            harness.Bus,
            provider,
            new Dictionary<Type, string>(),
            new ConventionSendEndpointResolver(
                Microsoft.Extensions.Options.Options.Create(
                    new SharedKernel.Messaging.Abstractions.Options.MessagingOptions { ServiceName = "test-service" })));

        await bus.ExecuteRoutingSlipAsync(slip, CancellationToken.None);

        await harness.InactivityTask;

        (await harness.Published.Any<RoutingSlipFaulted>()).Should().BeTrue(
            "the routing slip must publish RoutingSlipFaulted when an activity faults");

        recorder.CompensatedSteps.Should().Contain("reserve-inventory:SKU-2:1",
            "the first (already-completed) activity must be compensated when a downstream activity faults");

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Test instrumentation — shared singleton recorder for activity execution order.
// Not using 'file' modifier — see CLAUDE.md test rules (MassTransit type matching).
// ---------------------------------------------------------------------------

/// <summary>Records execution and compensation order for routing slip activities under test.</summary>
internal sealed class ExecutionRecorder
{
    private readonly List<string> _executedSteps = [];
    private readonly List<string> _compensatedSteps = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<string> ExecutedSteps
    {
        get { lock (_gate) { return [.. _executedSteps]; } }
    }

    public IReadOnlyList<string> CompensatedSteps
    {
        get { lock (_gate) { return [.. _compensatedSteps]; } }
    }

    public void RecordExecuted(string step)
    {
        lock (_gate) { _executedSteps.Add(step); }
    }

    public void RecordCompensated(string step)
    {
        lock (_gate) { _compensatedSteps.Add(step); }
    }
}

// ---------------------------------------------------------------------------
// ReserveInventory activity — succeeds, records a compensation log.
// ---------------------------------------------------------------------------

internal sealed class ReserveInventoryArguments
{
    public Guid OrderId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

internal sealed class ReserveInventoryLog
{
    public string Sku { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

internal sealed class ReserveInventoryActivity : RoutingSlipActivityBase<ReserveInventoryArguments, ReserveInventoryLog>
{
    private readonly ExecutionRecorder _recorder;

    public ReserveInventoryActivity(ExecutionRecorder recorder)
        : base(NullLogger<ReserveInventoryActivity>.Instance)
    {
        _recorder = recorder;
    }

    protected override Task<ExecutionResult> ExecuteAsync(ReserveInventoryArguments arguments, CancellationToken ct)
    {
        _recorder.RecordExecuted($"reserve-inventory:{arguments.Sku}:{arguments.Quantity}");

        return Task.FromResult(Complete(new ReserveInventoryLog
        {
            Sku = arguments.Sku,
            Quantity = arguments.Quantity,
        }));
    }

    protected override Task<CompensationResult> CompensateAsync(ReserveInventoryLog log, CancellationToken ct)
    {
        _recorder.RecordCompensated($"reserve-inventory:{log.Sku}:{log.Quantity}");
        return Task.FromResult(CompensationComplete());
    }
}

// ---------------------------------------------------------------------------
// ChargeCard activity — succeeds.
// ---------------------------------------------------------------------------

internal sealed class ChargeCardArguments
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
}

internal sealed class ChargeCardLog
{
    public decimal Amount { get; init; }
}

internal sealed class ChargeCardActivity : RoutingSlipActivityBase<ChargeCardArguments, ChargeCardLog>
{
    private readonly ExecutionRecorder _recorder;

    public ChargeCardActivity(ExecutionRecorder recorder)
        : base(NullLogger<ChargeCardActivity>.Instance)
    {
        _recorder = recorder;
    }

    protected override Task<ExecutionResult> ExecuteAsync(ChargeCardArguments arguments, CancellationToken ct)
    {
        _recorder.RecordExecuted($"charge-card:{arguments.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return Task.FromResult(Complete(new ChargeCardLog { Amount = arguments.Amount }));
    }

    protected override Task<CompensationResult> CompensateAsync(ChargeCardLog log, CancellationToken ct)
    {
        _recorder.RecordCompensated($"charge-card:{log.Amount}");
        return Task.FromResult(CompensationComplete());
    }
}

// ---------------------------------------------------------------------------
// FailingChargeCard activity — always faults during ExecuteAsync (RS-09).
// ---------------------------------------------------------------------------

internal sealed class FailingChargeCardActivity : RoutingSlipActivityBase<ChargeCardArguments, ChargeCardLog>
{
    public FailingChargeCardActivity()
        : base(NullLogger<FailingChargeCardActivity>.Instance)
    {
    }

    protected override Task<ExecutionResult> ExecuteAsync(ChargeCardArguments arguments, CancellationToken ct)
        => throw new InvalidOperationException("Card declined.");

    protected override Task<CompensationResult> CompensateAsync(ChargeCardLog log, CancellationToken ct)
        => Task.FromResult(CompensationComplete());
}
