using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>Ledger reads: tenant isolation, keyset paging, audited exports and cross-tenant reads, index use (A17).</summary>
[Collection("AuditPostgres")]
public sealed class QueryTests(PostgreSqlContainerFixture fixture)
{
    private static Task<AuditRecord> WriteAsync(LedgerHost host, string resourceId, string resourceType = "Order") =>
        host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(new AuditEntry
        {
            Action = "OrderRejected", ResourceType = resourceType, ResourceId = resourceId, Outcome = AuditOutcome.Failed,
        }));

    private static Task<T> QueryAsync<T>(LedgerHost host, Func<IAuditQueryService, Task<T>> action) =>
        host.InScopeAsync(sp => action(sp.GetRequiredService<IAuditQueryService>()));

    [Fact]
    public async Task Query_ReturnsOnlyTheCallersTenant()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        await WriteAsync(host, "o-1");
        host.Context.TenantId = TestRequestContext.TenantB;
        await WriteAsync(host, "o-1");

        var page = await QueryAsync(host, q => q.QueryAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = "o-1" }));
        page.Items.Should().ContainSingle().Which.TenantId.Should().Be(TestRequestContext.TenantB);

        var byActor = await QueryAsync(host, q => q.QueryAsync(new AuditRecordQuery { ActorId = "user-1" }));
        byActor.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Query_WithoutATenant_OutsideASystemScope_IsRejected()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        host.Context.TenantId = null;

        await host.Invoking(h => QueryAsync(h, q => q.QueryAsync(new AuditRecordQuery { ResourceType = "Order" })))
            .Should().ThrowAsync<InvalidOperationException>();
        await host.Invoking(h => QueryAsync(h, q => q.VerifyChainAsync("Order")))
            .Should().ThrowAsync<InvalidOperationException>();

        host.Context.ActorKind = ActorKind.System;
        (await QueryAsync(host, q => q.QueryAsync(new AuditRecordQuery { ResourceType = "Order" }))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_PagesWithAnOpaqueCursor_InBothDirections()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var start = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            host.Clock.UtcNow = start.AddSeconds(i);
            await WriteAsync(host, $"o-{i}");
        }

        foreach (var descending in new[] { false, true })
        {
            var seen = new List<string>();
            string? cursor = null;
            do
            {
                var page = await QueryAsync(host, q => q.QueryAsync(new AuditRecordQuery { ResourceType = "Order", Limit = 2, Cursor = cursor, Descending = descending }));
                seen.AddRange(page.Items.Select(r => r.ResourceId));
                cursor = page.NextCursor;
            }
            while (cursor is not null);

            var expected = Enumerable.Range(0, 5).Select(i => $"o-{i}");
            seen.Should().Equal(descending ? expected.Reverse() : expected);
        }
    }

    [Fact]
    public async Task Query_RejectsUnsupportedShapes()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);

        await host.Invoking(h => QueryAsync(h, q => q.QueryAsync(new AuditRecordQuery())))
            .Should().ThrowAsync<ArgumentException>("an unindexed tenant-wide scan is not offered");
        await host.Invoking(h => QueryAsync(h, q => q.QueryAsync(new AuditRecordQuery { ResourceType = "Order", Limit = AuditQueryLimits.MaxPageSize + 1 })))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await host.Invoking(h => QueryAsync(h, q => q.QueryAsync(new AuditRecordQuery { ResourceType = "Order", Cursor = "garbage" })))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task QueryAcrossTenants_RequiresAScope_AndAuditsItself()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        await WriteAsync(host, "shared");
        host.Context.TenantId = TestRequestContext.TenantB;
        await WriteAsync(host, "shared");

        var query = new AuditRecordQuery { ResourceType = "Order", ResourceId = "shared" };
        await host.Invoking(h => QueryAsync(h, q => q.QueryAcrossTenantsAsync(query))).Should().ThrowAsync<InvalidOperationException>();

        using (host.Scope.Enter())
        {
            (await QueryAsync(host, q => q.QueryAcrossTenantsAsync(query))).Items.Should().HaveCount(2);

            var audit = await QueryAsync(host, q => q.QueryAcrossTenantsAsync(new AuditRecordQuery
            {
                ResourceType = AuditLedgerActions.LedgerResourceType, ResourceId = "Order/shared",
            }));
            audit.Items.Should().ContainSingle().Which.Should().Match<AuditRecord>(r =>
                r.Action == AuditLedgerActions.CrossTenantQueried && r.TenantId == null && r.ActorId == "user-1");
        }
    }

    [Fact]
    public async Task Export_StreamsTheCallersChainInTimeOrder_AndAuditsItself()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);
        for (var i = 0; i < 3; i++)
        {
            host.Clock.UtcNow = start.AddSeconds(i);
            await WriteAsync(host, $"o-{i}");
        }

        host.Clock.UtcNow = DateTimeOffset.UtcNow;
        var exported = await host.InScopeAsync(async sp =>
        {
            var rows = new List<AuditRecord>();
            await foreach (var record in sp.GetRequiredService<IAuditQueryService>().ExportRangeAsync("Order", start, start.AddSeconds(1)))
                rows.Add(record);
            return rows;
        });

        exported.Select(r => r.ResourceId).Should().Equal("o-0", "o-1");
        var audit = await QueryAsync(host, q => q.QueryAsync(new AuditRecordQuery { ResourceType = AuditLedgerActions.LedgerResourceType }));
        audit.Items.Should().ContainSingle().Which.Should().Match<AuditRecord>(r =>
            r.Action == AuditLedgerActions.Exported && r.TenantId == TestRequestContext.TenantA && r.ResourceId == "Order");
    }

    [Theory]
    [InlineData("SELECT id FROM audit_records WHERE tenant_id = '{0}' AND resource_type = 'Order' AND resource_id = 'o-1' ORDER BY occurred_on, id LIMIT 10", "ix_audit_records_resource")]
    [InlineData("SELECT id FROM audit_records WHERE tenant_id = '{0}' AND actor_id = 'user-1' ORDER BY occurred_on, id LIMIT 10", "ix_audit_records_actor")]
    [InlineData("SELECT id FROM audit_records WHERE tenant_id = '{0}' AND resource_type = 'Order' ORDER BY occurred_on, id LIMIT 10", "ix_audit_records_chain_time")]
    [InlineData("SELECT sequence FROM audit_chain_links WHERE tenant_id = '{0}' AND resource_type = 'Order' ORDER BY sequence DESC LIMIT 1", "ux_audit_chain_links_chain_sequence")]
    [InlineData("SELECT sequence FROM audit_chain_links WHERE tenant_id IS NULL AND resource_type = 'Order' ORDER BY sequence LIMIT 10", "ux_audit_chain_links_chain_sequence")]
    public async Task EveryQueryShape_IsServedByAnIndex(string sql, string index)
    {
        // A17: the old indexes led with a generated chain_key that no query filtered on.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using (var set = new NpgsqlCommand("SET enable_seqscan = off", connection))
            await set.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand(
            $"EXPLAIN {string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, TestRequestContext.TenantA)}", connection);

        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));
        }

        string.Join('\n', plan).Should().Contain(index);
    }
}
