using Microsoft.Extensions.Logging;
using SharedKernel.Application.Messaging;
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Dispatch;
using Shop.Contracts.Ordering;
using Shop.Ordering.Application;
using Shop.Ordering.Domain;
using Temporalio.Activities;
using Temporalio.Api.Enums.V1;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Shop.Ordering.Infrastructure.Fulfilment;

/// <summary>The workflow's input: the order and what it needs from the warehouse.</summary>
public sealed record FulfilmentRequest(Guid OrderId, List<FulfilmentLine> Lines);

public sealed record FulfilmentLine(string Sku, int Quantity);

/// <summary>Where fulfilment workers listen.</summary>
public static class FulfilmentQueues
{
    public const string TaskQueue = "ordering-fulfilment";
}

/// <summary>
/// Fulfils one order: hold the stock in Inventory, then confirm the order; when Inventory refuses (not enough stock, an
/// unknown SKU), reject it instead. Durable: a crash at any point resumes from the last completed step on any worker.
/// </summary>
[Workflow]
public sealed class OrderFulfilmentWorkflow : WorkflowBase
{
    [WorkflowRun]
    public async Task<string> RunAsync(FulfilmentRequest request)
    {
        Guid reservation;
        try
        {
            reservation = await ExecuteAsync<ReserveStockActivity, FulfilmentRequest, Guid>(
                request
            );
        }
        catch (ActivityFailureException failure)
            when (failure.InnerException
                    is ApplicationFailureException { NonRetryable: true } refused
            )
        {
            // Business refusals are non-retryable by the kernel's failure mapping: the order cannot be fulfilled.
            await ExecuteAsync<RejectOrderActivity, RejectOrderCommand, object?>(
                new RejectOrderCommand(request.OrderId, refused.ErrorType ?? "inventory.refused")
            );
            return "rejected";
        }

        await ExecuteAsync<ConfirmOrderActivity, ConfirmOrderCommand, object?>(
            new ConfirmOrderCommand(request.OrderId, reservation)
        );
        return "confirmed";
    }
}

/// <summary>Holds the order's stock in Inventory over gRPC; the workflow's tenant travels with the call.</summary>
public sealed class ReserveStockActivity(
    IInventoryReservations inventory,
    ILogger<ReserveStockActivity> logger,
    IClock clock
) : ActivityBase(logger, clock)
{
    // Temporal passes the workflow's arguments only: a CancellationToken parameter would count as a second argument
    // (the kernel README's recipe shows one). The activity's token comes from its execution context.
    [Activity(nameof(ReserveStockActivity))]
    public async Task<Guid> ReserveAsync(FulfilmentRequest request)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var reserved = await inventory.ReserveAsync(
            new OrderId(request.OrderId),
            [.. request.Lines.Select(line => (line.Sku, line.Quantity))],
            ct
        );
        if (reserved.IsFailure)
        {
            throw Fail(reserved.Error);
        }

        return reserved.Value;
    }
}

public sealed class ConfirmOrderActivity(
    ISender sender,
    ILogger<ConfirmOrderActivity> logger,
    IClock clock
) : CommandActivity<ConfirmOrderCommand>(sender, logger, clock)
{
    [Activity(nameof(ConfirmOrderActivity))]
    public override Task ExecuteAsync(
        ConfirmOrderCommand command,
        CancellationToken ct = default
    ) => base.ExecuteAsync(command, ct);
}

public sealed class RejectOrderActivity(
    ISender sender,
    ILogger<RejectOrderActivity> logger,
    IClock clock
) : CommandActivity<RejectOrderCommand>(sender, logger, clock)
{
    [Activity(nameof(RejectOrderActivity))]
    public override Task ExecuteAsync(RejectOrderCommand command, CancellationToken ct = default) =>
        base.ExecuteAsync(command, ct);
}

/// <summary>
/// Starts fulfilment when an order is placed. The workflow id is derived from the order, so a redelivered event finds
/// the workflow already started and is acknowledged rather than starting a second one.
/// </summary>
public sealed class OrderPlacedConsumer(
    IWorkflowDispatcher workflows,
    IRequestContext caller,
    ILogger<OrderPlacedConsumer> logger
) : ConsumerBase<EventEnvelope<OrderPlaced>>(logger)
{
    protected override async Task ConsumeAsync(
        EventEnvelope<OrderPlaced> envelope,
        CancellationToken ct
    )
    {
        var placed = envelope.Data;
        var started = await workflows.StartAsync<OrderFulfilmentWorkflow, FulfilmentRequest>(
            new FulfilmentRequest(
                placed.OrderId,
                [.. placed.Lines.Select(l => new FulfilmentLine(l.Sku, l.Quantity))]
            ),
            new WorkflowStartOptions
            {
                TaskQueue = FulfilmentQueues.TaskQueue,
                BusinessKey = placed.OrderId.ToString("D"),
                IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate,
                IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
            },
            TenantScope.FromNullable(caller.TenantId),
            ct
        );

        if (started.IsFailure && started.Error.Code != "workflow.already_started")
        {
            // Retried by the bus; after the retries the message goes to the error queue.
            throw new InvalidOperationException(
                $"Fulfilment of order {placed.OrderId} could not start: {started.Error.Message}"
            );
        }
    }
}
