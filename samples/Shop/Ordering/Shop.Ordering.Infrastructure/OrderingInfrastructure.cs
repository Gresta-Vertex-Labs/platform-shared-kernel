using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.HashStore.Extensions;
using SharedKernel.Communication;
using SharedKernel.Cryptography.Argon2;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Idempotency.EfCore.Extensions;
using SharedKernel.Idempotency.Redis.Extensions;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Persistence;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Validation.FluentValidation;
using SharedKernel.Workflows.Temporal.Hosting;
using Shop.Contracts.Inventory;
using Shop.Ordering.Application;
using Shop.Ordering.Infrastructure.Fulfilment;
using Shop.Ordering.Infrastructure.Inventory;
using Shop.Ordering.Infrastructure.Persistence;

namespace Shop.Ordering.Infrastructure;

/// <summary>Registers every adapter behind Ordering's ports.</summary>
public static class OrderingInfrastructure
{
    public static IHostApplicationBuilder AddOrderingInfrastructure(
        this IHostApplicationBuilder builder
    )
    {
        IConfiguration configuration = builder.Configuration;
        IServiceCollection services = builder.Services;

        services.AddClock();

        // 01.Core TOTP verification state in Redis (the host registers the cryptography builder itself).
        services.AddSingleton<ITotpReplayGuard, RedisTotpReplayGuard>();
        services.AddSingleton<ITotpAttemptThrottle, RedisTotpAttemptThrottle>();

        // 06.Persistence: row-level security, the signed audit ledger (sealed by its own role), field encryption.
        builder.AddSharedKernelPostgres<OrderingDbContext>(
            OrderingDatabase.ConnectionName,
            p => OrderingDatabase.Configure(p).UseServiceName("ordering-api").MigrateOnStartup()
        );
        services.AddSharedKernelNpgsql(
            configuration.GetSection(OrderingDatabase.AuditSealerSection),
            OrderingDatabase.AuditSealer
        );

        // 02.Caching + 18.Idempotency: request deduplication in Redis, message deduplication next to the orders.
        services.AddRedisConnection(configuration).AddRedisHashService();
        services.AddRedisIdempotency(purposes => purposes.ForRequests());
        // The EF Core store owns its context and takes no service provider, so it gets a data source of its own.
        var idempotencyDataSource = NpgsqlDataSource.Create(
            configuration.GetConnectionString(OrderingDatabase.ConnectionName)
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:ordering is not configured."
                )
        );
        services.AddSingleton(new IdempotencyDataSource(idempotencyDataSource));
        services.AddEfCoreIdempotency(
            db => db.UsePostgres(idempotencyDataSource),
            purposes => purposes.ForMessages()
        );

        // 07.Messaging: RabbitMQ, the EF Core outbox (events commit with the order), retries, deduplication, and the
        // publisher's tenant on every consumed message.
        services
            .AddSharedKernelMessaging(configuration)
            .UseRabbitMq(
                configuration.GetConnectionString("rabbitmq")
                    ?? throw new InvalidOperationException(
                        "ConnectionStrings:rabbitmq is not configured."
                    )
            )
            .WithEntityFrameworkOutbox<OrderingDbContext>()
            .WithRetry()
            .WithIdempotency()
            .WithInboundRequestContext()
            .AddConsumer<OrderPlacedConsumer>()
            .Build();

        // 17.Workflows: the fulfilment workflow and its activities, run by this service's worker.
        services
            .AddSharedKernelTemporalWorkflows(configuration)
            .AddWorkflow<OrderFulfilmentWorkflow>()
            .AddActivities<ReserveStockActivity>()
            .AddActivities<ConfirmOrderActivity>()
            .AddActivities<RejectOrderActivity>()
            .WithWorker(FulfilmentQueues.TaskQueue)
            .WithOpenTelemetry()
            .Build();

        // 11.Communication: Inventory over gRPC with mutual TLS (SharedKernel:Communication:Clients:inventory).
        services
            .AddSharedKernelCommunication(configuration)
            .AddGrpcClient<InventoryService.InventoryServiceClient>(
                GrpcInventoryReservations.ClientName
            );
        services.AddScoped<IInventoryReservations, GrpcInventoryReservations>();

        services.AddFluentValidationRequestValidators(typeof(PlaceOrderCommand).Assembly);
        services.AddScoped<OrderStorageInspector>();
        return builder;
    }

    /// <summary>
    /// 01.Core cryptography: the HMAC signer the audit ledger needs, and Argon2id. Returns the builder so the host can
    /// add authenticator step-up, which lives in a Host package.
    /// </summary>
    public static ICryptographyBuilder AddOrderingCryptography(
        this IHostApplicationBuilder builder
    ) =>
        builder
            .Services.AddSharedKernelCryptography(builder.Configuration)
            .AddArgon2id(builder.Configuration);
}

/// <summary>Owns the idempotency store's data source, so the host disposes it.</summary>
internal sealed class IdempotencyDataSource(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
