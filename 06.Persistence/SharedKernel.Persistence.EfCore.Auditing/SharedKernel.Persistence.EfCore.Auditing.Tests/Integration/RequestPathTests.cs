using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>The request path: one plain INSERT, transaction rules, validation, identity, idempotency, A16/A19.</summary>
[Collection("AuditPostgres")]
public sealed class RequestPathTests(PostgreSqlContainerFixture fixture)
{
    private static AuditEntry Entry(AuditOutcome outcome = AuditOutcome.Succeeded, string resourceId = "order-1", string? idempotencyKey = null) => new()
    {
        Action = "OrderApproved",
        ResourceType = "Order",
        ResourceId = resourceId,
        Outcome = outcome,
        BeforeSnapshot = "{\"status\":\"pending\"}",
        AfterSnapshot = "{\"status\":\"approved\"}",
        IdempotencyKey = idempotencyKey,
    };

    private static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> OpenAmbientAsync(LedgerHost host)
    {
        var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        host.Ambient.Current = (connection, transaction);
        return (connection, transaction);
    }

    private static Task<long> CountAsync(string cs, string table = AuditLedgerSchema.RecordsTable) =>
        LedgerTestDatabase.ScalarAsync<long>(cs, $"SELECT count(*) FROM {table}");

    [Fact]
    public async Task Succeeded_InsideAmbientTransaction_CommitsWithIt()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var (connection, transaction) = await OpenAmbientAsync(host);

        await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry()));
        (await CountAsync(cs)).Should().Be(0, "nothing is visible before the business transaction commits");

        await transaction.CommitAsync();
        await connection.DisposeAsync();

        (await CountAsync(cs)).Should().Be(1);
        (await CountAsync(cs, AuditLedgerSchema.PayloadsTable)).Should().Be(1);
        (await CountAsync(cs, AuditLedgerSchema.LinksTable)).Should().Be(0, "the request path never seals");
    }

    [Fact]
    public async Task Succeeded_RollsBackWithTheAmbientTransaction()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var (connection, transaction) = await OpenAmbientAsync(host);

        await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry()));
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        (await CountAsync(cs)).Should().Be(0);
    }

    [Fact]
    public async Task Succeeded_WithoutAmbientTransaction_ThrowsAndWritesNothing()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);

        var act = () => host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry()));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ambient database transaction*");
        (await CountAsync(cs)).Should().Be(0);
    }

    [Fact]
    public async Task Failed_PersistsOnItsOwn_EvenWhileTheBusinessTransactionRollsBack()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var (connection, transaction) = await OpenAmbientAsync(host);

        await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed) with { ErrorCode = "order.rejected" }));
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        (await CountAsync(cs)).Should().Be(1);
    }

    [Fact]
    public async Task Failed_OnTheSameChain_AsAnOpenSucceededTransaction_DoesNotWait()
    {
        // A18: the old writer's Failed entry waited (up to 5 s) for the chain lock held by the open
        // business transaction. The plain INSERT takes no chain lock at all.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var (connection, transaction) = await OpenAmbientAsync(host);
        await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry()));

        var stopwatch = Stopwatch.StartNew();
        await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed)));
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));

        await transaction.CommitAsync();
        await connection.DisposeAsync();
        (await CountAsync(cs)).Should().Be(2);
    }

    [Fact]
    public async Task OversizedField_IsRejectedBeforeAnySql_AndTheBusinessTransactionStillCommits()
    {
        // A19: the old writer sent the oversized value, PostgreSQL raised 22001 inside the business
        // transaction and aborted it.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await LedgerTestDatabase.ExecuteAsync(cs, "CREATE TABLE business_rows (id int PRIMARY KEY)");
        await using var host = LedgerHost.Build(cs);
        var (connection, transaction) = await OpenAmbientAsync(host);

        await using (var insert = new NpgsqlCommand("INSERT INTO business_rows VALUES (1)", connection, transaction))
            await insert.ExecuteNonQueryAsync();

        var act = () => host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(resourceId: new string('x', AuditFieldLimits.ResourceId + 1))));
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*ResourceId*");

        await transaction.CommitAsync();
        await connection.DisposeAsync();
        (await CountAsync(cs, "business_rows")).Should().Be(1, "the audit validation never touched the business transaction");
    }

    [Fact]
    public async Task Identity_TraceAndServiceName_AreCapturedFromTheContext()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        host.Context.ClientId = "client-9";
        host.Context.SessionId = "session-9";
        host.Context.ImpersonatorId = "support-1";

        using var activity = new Activity("audited-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.SetBaggage(SharedKernel.Primitives.Propagation.WellKnownBaggageKeys.CorrelationId, "corr-77");

        var record = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed)));
        var stored = (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().QueryAsync(new AuditRecordQuery { ResourceType = "Order" }))).Items.Single();

        stored.Id.Should().Be(record.Id);
        stored.ActorId.Should().Be("user-1");
        stored.ActorKind.Should().Be(ActorKind.User);
        stored.ClientId.Should().Be("client-9");
        stored.SessionId.Should().Be("session-9");
        stored.ImpersonatorId.Should().Be("support-1");
        stored.SourceService.Should().Be("system", "the default PersistenceServiceOptions.ServiceName");
        stored.TraceId.Should().Be(activity.TraceId.ToHexString());
        stored.CorrelationId.Should().Be("corr-77");
        stored.BeforeSnapshot.Should().Be("{\"status\":\"pending\"}");
        stored.OccurredOn.Should().Be(record.OccurredOn);
        (stored.OccurredOn.UtcTicks % 10).Should().Be(0, "timestamps are whole microseconds");
    }

    [Fact]
    public async Task NoTenant_WithoutAnExplicitSystemScope_IsRejected()
    {
        // A16: an unresolved tenant used to land silently on the "system" chain.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        host.Context.TenantId = null;

        var act = () => host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed)));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*system audit chain*");

        host.Context.IsAuthenticated = false;
        await act.Should().ThrowAsync<InvalidOperationException>("an anonymous caller is not a system scope");
        (await CountAsync(cs)).Should().Be(0);
    }

    [Fact]
    public async Task NoTenant_UnderSystemIdentityOrCrossTenantScope_WritesTheSystemChain()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        host.Context.TenantId = null;
        host.Context.ActorKind = ActorKind.System;

        var system = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed)));
        system.TenantId.Should().BeNull();

        host.Context.ActorKind = ActorKind.User;
        using (host.Scope.Enter())
        {
            var scoped = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed)));
            scoped.TenantId.Should().BeNull();
        }
    }

    [Fact]
    public async Task IdempotencyKey_ReturnsTheStoredRecord_AndRejectsADifferentEvent()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);

        var first = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed, idempotencyKey: "idem-1")));
        var again = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed, idempotencyKey: "idem-1")));
        again.Id.Should().Be(first.Id);

        var other = () => host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed, resourceId: "order-2", idempotencyKey: "idem-1")));
        await other.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different event*");
        (await CountAsync(cs)).Should().Be(1);
    }

    [Fact]
    public async Task IdempotencyKey_DuplicateInsideAmbientTransaction_DoesNotAbortIt()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);

        var (firstConnection, firstTransaction) = await OpenAmbientAsync(host);
        var original = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(idempotencyKey: "idem-2")));
        await firstTransaction.CommitAsync();
        await firstConnection.DisposeAsync();

        var (connection, transaction) = await OpenAmbientAsync(host);
        var duplicate = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(idempotencyKey: "idem-2")));
        duplicate.Id.Should().Be(original.Id);

        // The transaction is still usable after the conflict (ON CONFLICT DO NOTHING, no 23505).
        await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(resourceId: "order-3")));
        await transaction.CommitAsync();
        await connection.DisposeAsync();
        (await CountAsync(cs)).Should().Be(2);
    }

    [Fact]
    public async Task SameIdempotencyKey_Concurrently_IsWrittenOnce()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);

        var writes = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(AuditOutcome.Failed, idempotencyKey: "burst")))));
        var records = await Task.WhenAll(writes);

        records.Select(r => r.Id).Distinct().Should().ContainSingle();
        (await CountAsync(cs)).Should().Be(1);
    }

    [Fact]
    public async Task InsertXid_CannotBeForgedByTheWriter()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await LedgerTestDatabase.ExecuteAsync(cs,
            $"""
            INSERT INTO {AuditLedgerSchema.RecordsTable} (id, tenant_id, resource_type, resource_id, action, outcome, actor_id, actor_kind,
                source_service, occurred_on, payload_hash, format_version, insert_xid)
            VALUES (gen_random_uuid(), NULL, 'X', 'x', 'a', 0, 'u', 0, 's', now(), decode(repeat('00', 32), 'hex'), 3, 1)
            """);

        (await LedgerTestDatabase.ScalarAsync<long>(cs, $"SELECT insert_xid FROM {AuditLedgerSchema.RecordsTable}"))
            .Should().BeGreaterThan(1, "the BEFORE INSERT trigger stamps the real transaction id");
    }
}
