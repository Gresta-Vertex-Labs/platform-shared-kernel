using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Execution.Auditing;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Application.Messaging;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.Npgsql.Options;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.ServiceDefaults.Persistence.Tests.TestFixtures;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.Integration;

/// <summary>
/// Proves, against real PostgreSQL, the production composition
/// <c>AddSharedKernelApplicationBehaviors().AddAuditingBehavior().AddTransactionBehavior().Build()</c>
/// over the EF Core unit of work and audit trail — with no adapter or bridge in between (P-558): a
/// <c>Succeeded</c> audit record commits atomically with the business write it attests to, and a
/// business write that fails at commit time leaves no <c>Succeeded</c> record but a <c>Failed</c> one.
/// </summary>
/// <remarks>
/// Every write happens through <see cref="ISender.Send{TResponse}(MediatR.IRequest{TResponse}, CancellationToken)"/>,
/// exactly as a real service would call it.
/// </remarks>
[Collection("AuditWiringPostgres")]
public sealed class AuditTransactionWiringPostgresTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public AuditTransactionWiringPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private sealed record CreateOrderCommand(Guid OrderId, string Name)
        : ICommand, IAuditableRequest<Result>
    {
        public string Action => "OrderCreated";
        public string ResourceType => "Order";
        public string ResourceId => OrderId.ToString();
        public string? BeforeSnapshot => null;
        public string? GetAfterSnapshot(Result response) => response.IsSuccess ? "{\"created\":true}" : null;
    }

    private sealed class CreateOrderCommandHandler(AuditWiringTestDbContext context) : ICommandHandler<CreateOrderCommand>
    {
        public Task<Result> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
        {
            // Stages the row on the change tracker — deliberately NOT saved here. Whether it, and the
            // audit entry AuditingBehavior stages next, ever reach the database is entirely
            // TransactionBehavior's decision, made after this handler returns.
            context.Orders.Add(new WiringTestOrder(request.OrderId, request.Name));
            return Task.FromResult(Result.Success());
        }
    }

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    private static async Task<ServiceProvider> BuildAndCreateAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var actor = new FakeAuditActorContext();
        services.AddSingleton<IRequestContext>(actor);
        services.AddSingleton<IHmacSigner, HmacSha256Signer>();

        var configurationValues = new Dictionary<string, string?>
        {
            [$"{AuditLedgerOptions.SectionName}:CurrentKeyId"] = "k1",
            [$"{AuditLedgerOptions.SectionName}:Keys:k1:Order"] = "1",
            [$"{AuditLedgerOptions.SectionName}:Sealer:Enabled"] = "false",
            [$"{AuditLedgerOptions.SectionName}:SelfCheck"] = "Off",
            [$"{AuditLedgerOptions.SectionName}:Keys:k1:Material"] =
                Convert.ToBase64String(Enumerable.Repeat((byte)0x24, 32).ToArray()),
            [$"SharedKernel:Persistence:audit-wiring:{nameof(NpgsqlPersistenceOptions.ConnectionString)}"] = connectionString,
            // Testcontainers' Postgres image has no TLS configured — the documented, explicit opt-down
            // NpgsqlPersistenceOptionsValidator requires, matching real local-dev/CI usage.
            [$"SharedKernel:Persistence:audit-wiring:{nameof(NpgsqlPersistenceOptions.SslMode)}"] = "Disable",
            [$"SharedKernel:Persistence:audit-wiring:{nameof(NpgsqlPersistenceOptions.AcknowledgeInsecureSslMode)}"] = "true",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configurationValues).Build();

        // The one-line composition: data source, EF Core, unit of work, audit trail.
        services.AddSharedKernelPostgres<AuditWiringTestDbContext>(configuration, "audit-wiring", p => p
            // Test-harness-only: every test builds its own fresh DbContext model.
            .ConfigureDbContext((_, options) => options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)))
            .UseAuditTrail());

        // No bridge: EfUnitOfWork and EfAuditTrailWriter implement the shared IUnitOfWork and
        // IAuditTrailWriter (SharedKernel.Execution) the behaviors consume directly.

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<AuditTransactionWiringPostgresTests>());

        // The documented composition named in this wave's brief — nothing else.
        services.AddSharedKernelApplicationBehaviors()
            .AddAuditingBehavior()
            .AddTransactionBehavior()
            .Build();

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AuditWiringTestDbContext>().Database;
        await database.EnsureCreatedAsync();
        // The ledger tables are not part of the EF model; they are created by the migration helper's DDL.
        await database.ExecuteSqlRawAsync(AuditLedgerSchema.CreateScript);

        return provider;
    }

    [Fact]
    public async Task SuccessfulAuditedCommand_ThroughRealPipeline_BusinessRowAndAuditRecordBothCommitTogether()
    {
        var connectionString = ConnectionString("sk_wiring_success_commit");
        await using var provider = await BuildAndCreateAsync(connectionString);

        var orderId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new CreateOrderCommand(orderId, "wired-ok"));
            result.IsSuccess.Should().BeTrue();
        }

        await using var verifyScope = provider.CreateAsyncScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<AuditWiringTestDbContext>();

        (await context.Orders.CountAsync(o => o.Id == orderId)).Should().Be(1);

        var auditRecords = (await verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>()
            .QueryAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = orderId.ToString() })).Items;
        auditRecords.Should().ContainSingle();
        auditRecords[0].Outcome.Should().Be(AuditOutcome.Succeeded);
    }

    [Fact]
    public async Task FailedBusinessWrite_ThroughRealPipeline_NoSucceededRecord_AndAFailedRecordIsWrittenAfterRollback()
    {
        // The command handler stages a business row whose Name exceeds the column's max length, so
        // the unit of work's save fails (22001) inside the transaction, BEFORE the pre-commit hook
        // that would write the Succeeded record ever runs. The transaction rolls back; the outer half
        // of AuditingBehavior — outside the transaction — then records the failure on its own
        // connection. This is the commit-time-failure case the old in-transaction placement could
        // never record (P-558, finding A18).
        var connectionString = ConnectionString("sk_wiring_failure_rollback");
        await using var provider = await BuildAndCreateAsync(connectionString);

        var orderId = Guid.NewGuid();
        var tooLongName = new string('x', 50);

        await using (var scope = provider.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var act = async () => await sender.Send(new CreateOrderCommand(orderId, tooLongName));
            // 22001 is classified by the always-registered SQLSTATE classifier (P-558).
            await act.Should().ThrowAsync<SharedKernel.Core.Exceptions.ValidationException>();
        }

        await using var verifyScope = provider.CreateAsyncScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<AuditWiringTestDbContext>();

        (await context.Orders.CountAsync(o => o.Id == orderId)).Should().Be(
            0, "the business write failed at SaveChangesAsync and must have rolled back");

        var auditRecords = (await verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>()
            .QueryAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = orderId.ToString() })).Items;
        auditRecords.Should().ContainSingle("no Succeeded attestation may exist for a write that never landed");
        auditRecords[0].Outcome.Should().Be(AuditOutcome.Failed);
        auditRecords[0].ErrorCode.Should().Contain(nameof(SharedKernel.Core.Exceptions.ValidationException));
    }

    [Fact]
    public async Task RejectedAuditedCommand_ThroughRealPipeline_FailedAuditRecordPersistsIndependently()
    {
        // Regression coverage for the OTHER half of the contract: a plain Result.Failure (no DB write
        // ever attempted) still records a Failed-outcome audit entry, and that entry is NOT contingent
        // on the (here, empty) business transaction — see EfAuditTrailWriter's own remarks on why a
        // Failed outcome always commits independently.
        var connectionString = ConnectionString("sk_wiring_rejected_failure_persists");
        await using var provider = await BuildAndCreateAsync(connectionString);

        var orderId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new RejectOrderCommand(orderId));
            result.IsFailure.Should().BeTrue();
        }

        await using var verifyScope = provider.CreateAsyncScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<AuditWiringTestDbContext>();

        var auditRecords = (await verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>()
            .QueryAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = orderId.ToString() })).Items;
        auditRecords.Should().ContainSingle();
        auditRecords[0].Outcome.Should().Be(AuditOutcome.Failed);
        auditRecords[0].ErrorCode.Should().Be("order.rejected");
    }

    private sealed record RejectOrderCommand(Guid OrderId) : ICommand, IAuditableRequest<Result>
    {
        public string Action => "OrderCreationRejected";
        public string ResourceType => "Order";
        public string ResourceId => OrderId.ToString();
        public string? BeforeSnapshot => null;
        public string? GetAfterSnapshot(Result response) => null;
    }

    private sealed class RejectOrderCommandHandler : ICommandHandler<RejectOrderCommand>
    {
        public Task<Result> Handle(RejectOrderCommand request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure(Error.Validation("order.rejected", "Rejected for testing.")));
    }
}
