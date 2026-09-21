using SharedKernel.Application.Auditing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Chain;
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
            var writer = scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>();
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
        var result = await queryService.VerifyFullChainAsync("Order");

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
            var writer = scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>();
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
        var result = await queryService.VerifyFullChainAsync("Order");

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
            var writer = scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>();
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
        var result = await queryService.VerifyFullChainAsync("Order");

        result.IsIntact.Should().BeFalse();
        result.BrokenAtSequence.Should().Be(second.Sequence, "sequence 2 is the first missing/mismatched position once the row is deleted");
        result.BrokenAtRecordId.Should().Be(third.Id, "the NEXT surviving record (sequence 3) is what verification actually finds at that position");
    }

    [Fact]
    public async Task VerifyFullChainAsync_HashAlgorithmRelabeledOutOfBand_ReportsBroken()
    {
        // H6 regression: HashAlgorithm used to be excluded from the hashed payload, so relabeling it
        // (with RecordHash left untouched) was undetectable — it never affected which key was looked
        // up (only KeyId does), and it never entered the recomputation, so the recomputed hash still
        // matched. It is now part of the hashed fields, so any relabeling changes the recomputed hash.
        var connectionString = ConnectionString("sk_audit_tamper_hash_algorithm_relabel");
        var tenantId = Guid.NewGuid();
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        AuditRecord written;
        await using (var scope = sp.CreateAsyncScope())
        {
            written = await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = $"""UPDATE "{AuditSchema.TableName}" SET "{AuditSchema.HashAlgorithm}" = 'HMAC-SHA256-RELABELED' WHERE "{AuditSchema.Id}" = @id""";
            command.Parameters.AddWithValue("id", written.Id);
            await command.ExecuteNonQueryAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync("Order");

        result.IsIntact.Should().BeFalse();
        result.BrokenAtRecordId.Should().Be(written.Id);
    }

    [Fact]
    public async Task VerifyFullChainAsync_KeyIdRelabeledToADifferentIdWithIdenticalKeyMaterial_ReportsBroken()
    {
        // H6 regression: KeyId used to be excluded from the hashed payload and resolved from the
        // record's own unauthenticated column. Relabeling KeyId to a DIFFERENT id that happens to
        // resolve to the IDENTICAL key material (e.g. two id labels aliased to one secret during a
        // rotation window) recomputed the exact same HMAC output either way, pre-fix — a routine-
        // looking relabel was silently undetectable. KeyId is now part of the hashed fields, so the
        // relabel itself changes the recomputed hash regardless of the material being identical.
        var connectionString = ConnectionString("sk_audit_tamper_keyid_relabel");
        var tenantId = Guid.NewGuid();
        await using var sp = AuditTestHost.Build(
            connectionString, new FakeAuditActorContext(tenantId: tenantId),
            configureServices: services => services.AddSingleton<IAuditChainKeyProvider>(new TwoIdsSameMaterialKeyProvider()));
        await EnsureCreatedAsync(sp);

        AuditRecord written;
        await using (var scope = sp.CreateAsyncScope())
        {
            written = await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        written.KeyId.Should().Be(TwoIdsSameMaterialKeyProvider.CurrentKeyId);

        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = $"""UPDATE "{AuditSchema.TableName}" SET "{AuditSchema.KeyId}" = @newKeyId WHERE "{AuditSchema.Id}" = @id""";
            command.Parameters.AddWithValue("newKeyId", TwoIdsSameMaterialKeyProvider.OtherKeyId);
            command.Parameters.AddWithValue("id", written.Id);
            await command.ExecuteNonQueryAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync("Order");

        result.IsIntact.Should().BeFalse("KeyId is part of the hashed payload — relabeling it must be detected even when the two ids resolve to identical key material");
        result.BrokenAtRecordId.Should().Be(written.Id);
    }

    /// <summary>Two distinct key ids that both resolve to the SAME underlying key material — isolates "the id was relabeled" from "the material changed".</summary>
    private sealed class TwoIdsSameMaterialKeyProvider : IAuditChainKeyProvider
    {
        public const string CurrentKeyId = "keyA";
        public const string OtherKeyId = "keyB";
        private static readonly byte[] Material = Enumerable.Repeat((byte)0x99, 32).ToArray();

        public AuditChainKey GetCurrentKey() => new(CurrentKeyId, Material);

        public bool TryGetKey(string keyId, out AuditChainKey key)
        {
            if (keyId is CurrentKeyId or OtherKeyId)
            {
                key = new AuditChainKey(keyId, Material);
                return true;
            }

            key = default;
            return false;
        }
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
            var writer = scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>();
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
        var result = await queryService.VerifyFullChainAsync("Order");

        result.IsIntact.Should().BeTrue("a chain missing its tail alone is indistinguishable from a chain that never grew further");
        result.RecordsChecked.Should().Be(1);
    }
}
