using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Tests.TestFixtures;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>
/// Proves <c>VerifyFullChainAsync</c> detects every out-of-band tamper class against real PostgreSQL,
/// via a RAW connection bypassing the application entirely (an attacker with database access but not
/// the out-of-database HMAC key) — this suite deliberately does NOT apply the immutability trigger, so
/// these mutations succeed at the database layer, isolating the APPLICATION-level (hash-chain)
/// detection layer from the DATABASE-level (trigger) prevention layer, which
/// <see cref="AuditChainMutationGuardPostgresTests"/> covers separately.
/// </summary>
[Collection("AuditPostgres")]
public sealed class AuditChainTamperDetectionPostgresTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public AuditChainTamperDetectionPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    private static async Task EnsureCreatedAsync(ServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
    }

    private static AuditEntry FailedEntry(string resourceType, string resourceId) => new()
    {
        Action = "Tested",
        ResourceType = resourceType,
        ResourceId = resourceId,
        Outcome = AuditOutcome.Failed,
        ErrorCode = "test.failure",
    };

    [Fact]
    public async Task VerifyFullChainAsync_FieldTamperedOutOfBand_ReportsBrokenAtThatSequence()
    {
        var connectionString = ConnectionString("sk_audit_tamper_field");
        var tenantId = Guid.NewGuid();
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        AuditRecord second;
        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            await writer.RecordAsync(FailedEntry("Order", "order-1"));
            second = await writer.RecordAsync(FailedEntry("Order", "order-2"));
        }

        // Out-of-band tamper: a raw connection, never going through IAuditTrailWriter, mutates a
        // column directly — exactly the class of attack a hash chain exists to detect. RecordHash is
        // deliberately left UNCHANGED, so it no longer matches a recomputation over the new content.
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = $"""UPDATE "{AuditSchema.TableName}" SET "{AuditSchema.Action}" = 'Tampered' WHERE "{AuditSchema.Id}" = @id""";
            command.Parameters.AddWithValue("id", second.Id);
            await command.ExecuteNonQueryAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync(tenantId, "Order");

        result.IsIntact.Should().BeFalse();
        result.BrokenAtRecordId.Should().Be(second.Id);
        result.BrokenAtSequence.Should().Be(second.Sequence);
    }

    [Fact]
    public async Task VerifyFullChainAsync_ForgedRewrite_UnkeyedRecomputedHash_StillDetectedAsBroken()
    {
        var connectionString = ConnectionString("sk_audit_tamper_forged");
        var tenantId = Guid.NewGuid();
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        AuditRecord second;
        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            await writer.RecordAsync(FailedEntry("Order", "order-1"));
            second = await writer.RecordAsync(FailedEntry("Order", "order-2"));
        }

        // A more sophisticated forgery: the attacker changes the content AND recomputes a NEW,
        // self-consistent-LOOKING hash for it (here, an arbitrary distinct hex string standing in for
        // "whatever unkeyed algorithm — e.g. plain SHA-256 — the attacker could compute without the
        // out-of-database HMAC key"). Verification must still catch it, because it recomputes via the
        // REAL keyed HMAC, which the attacker cannot reproduce without the key.
        var forgedHash = new string('f', 64);
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = $"""
                UPDATE "{AuditSchema.TableName}"
                SET "{AuditSchema.Action}" = 'Forged', "{AuditSchema.RecordHash}" = @forgedHash
                WHERE "{AuditSchema.Id}" = @id
                """;
            command.Parameters.AddWithValue("id", second.Id);
            command.Parameters.AddWithValue("forgedHash", forgedHash);
            await command.ExecuteNonQueryAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync(tenantId, "Order");

        result.IsIntact.Should().BeFalse();
        result.BrokenAtSequence.Should().Be(second.Sequence);
    }

    [Fact]
    public async Task VerifyFullChainAsync_MiddleRecordDeleted_ReportsGapAtThatSequence()
    {
        var connectionString = ConnectionString("sk_audit_tamper_delete_middle");
        var tenantId = Guid.NewGuid();
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        AuditRecord second, third;
        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            await writer.RecordAsync(FailedEntry("Order", "order-1"));
            second = await writer.RecordAsync(FailedEntry("Order", "order-2"));
            third = await writer.RecordAsync(FailedEntry("Order", "order-3"));
        }

        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = $"""DELETE FROM "{AuditSchema.TableName}" WHERE "{AuditSchema.Id}" = @id""";
            command.Parameters.AddWithValue("id", second.Id);
            await command.ExecuteNonQueryAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync(tenantId, "Order");

        result.IsIntact.Should().BeFalse();
        result.BrokenAtSequence.Should().Be(second.Sequence, "sequence 2 is the first missing/mismatched position once the row is deleted");
        result.BrokenAtRecordId.Should().Be(third.Id, "the NEXT surviving record (sequence 3) is what verification actually finds at that position");
    }

    [Fact]
    public async Task VerifyFullChainAsync_TailDeleted_LooksIntactOnItsOwn_DocumentedLimitation()
    {
        // Deliberately documents the honest limitation VerifyFullChainAsync's own remarks describe:
        // deleting the LAST record(s) of a chain is indistinguishable, on the chain's own evidence
        // alone, from "the chain never grew that far" — detecting it requires an independently-stored
        // checkpoint (see AuditCheckpointPostgresTests).
        var connectionString = ConnectionString("sk_audit_tamper_delete_tail");
        var tenantId = Guid.NewGuid();
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        AuditRecord second;
        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            await writer.RecordAsync(FailedEntry("Order", "order-1"));
            second = await writer.RecordAsync(FailedEntry("Order", "order-2"));
        }

        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = $"""DELETE FROM "{AuditSchema.TableName}" WHERE "{AuditSchema.Id}" = @id""";
            command.Parameters.AddWithValue("id", second.Id);
            await command.ExecuteNonQueryAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync(tenantId, "Order");

        result.IsIntact.Should().BeTrue("a chain missing its tail alone is indistinguishable from a chain that never grew further");
        result.RecordsChecked.Should().Be(1);
    }
}
