using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Execution.Auditing;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.EfCore.Auditing.Format;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>What chain verification proves: tampering, gaps, re-sealing with a leaked key, key regression, unknown keys.</summary>
[Collection("AuditPostgres")]
public sealed class TamperDetectionTests(PostgreSqlContainerFixture fixture)
{
    private static AuditEntry Entry(int i) => new()
    {
        Action = "OrderRejected",
        ResourceType = "Order",
        ResourceId = $"o-{i}",
        Outcome = AuditOutcome.Failed,
        AfterSnapshot = $"{{\"n\":{i}}}",
    };

    private static async Task<LedgerHost> SealedChainAsync(string cs, int count, Action<LedgerHostOptions>? configure = null)
    {
        var host = LedgerHost.Build(cs, configure);
        for (var i = 1; i <= count; i++)
            await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(i)));
        await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());
        return host;
    }

    private static Task<AuditChainVerificationResult> VerifyAsync(LedgerHost host, bool requirePayloads = false) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order", requirePayloads));

    private static string RecordAt(long sequence) =>
        $"(SELECT record_id FROM {AuditLedgerSchema.LinksTable} WHERE resource_type = 'Order' AND sequence = {sequence})";

    [Fact]
    public async Task RecordFieldEditedWithoutTheKey_IsHashMismatch()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = await SealedChainAsync(cs, 4);

        await LedgerTestDatabase.TamperAsync(cs, $"UPDATE {AuditLedgerSchema.RecordsTable} SET action = 'OrderApproved' WHERE id = {RecordAt(3)}");

        var result = await VerifyAsync(host);
        result.Should().Match<AuditChainVerificationResult>(r =>
            r.Status == AuditVerificationStatus.Broken && r.FailureKind == AuditVerificationFailureKind.HashMismatch && r.FailedAtSequence == 3 && r.HeadSequence == 2);
    }

    [Fact]
    public async Task PayloadEdited_IsHashMismatch()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = await SealedChainAsync(cs, 3);

        await LedgerTestDatabase.TamperAsync(cs, $"UPDATE {AuditLedgerSchema.PayloadsTable} SET after_snapshot = '{{\"n\":999}}' WHERE record_id = {RecordAt(2)}");

        (await VerifyAsync(host)).Should().Match<AuditChainVerificationResult>(r =>
            r.FailureKind == AuditVerificationFailureKind.HashMismatch && r.FailedAtSequence == 2);
    }

    [Fact]
    public async Task LinkDeleted_IsSequenceGap()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = await SealedChainAsync(cs, 4);

        await LedgerTestDatabase.TamperAsync(cs, $"DELETE FROM {AuditLedgerSchema.LinksTable} WHERE resource_type = 'Order' AND sequence = 2");

        (await VerifyAsync(host)).Should().Match<AuditChainVerificationResult>(r =>
            r.FailureKind == AuditVerificationFailureKind.SequenceGap && r.FailedAtSequence == 2);
    }

    [Fact]
    public async Task KeyIdRelabeled_IsDetected()
    {
        // The key id is inside the MAC: pointing a link at another key with the same material still fails.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = await SealedChainAsync(cs, 2, o => o.Keys["k0"] = (TestKeys.K1, 0));

        await LedgerTestDatabase.TamperAsync(cs, $"UPDATE {AuditLedgerSchema.LinksTable} SET key_id = 'k0' WHERE resource_type = 'Order' AND sequence = 1");

        (await VerifyAsync(host)).Should().Match<AuditChainVerificationResult>(r =>
            r.Status == AuditVerificationStatus.Broken && r.FailedAtSequence == 1);
    }

    [Fact]
    public async Task KeyRotation_K1ThenK2_VerifiesIntact()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await (await SealedChainAsync(cs, 3)).DisposeAsync();

        await using var rotated = await SealedChainAsync(cs, 2, o =>
        {
            o.Keys["k2"] = (TestKeys.K2, 2);
            o.CurrentKeyId = "k2";
        });

        var result = await VerifyAsync(rotated);
        result.IsIntact.Should().BeTrue();
        result.RecordsChecked.Should().Be(5);
        (await LedgerTestDatabase.ScalarAsync<long>(cs, $"SELECT count(*) FROM {AuditLedgerSchema.LinksTable} WHERE key_id = 'k2'")).Should().Be(2);
    }

    [Fact]
    public async Task RetiredKeyRemovedFromTheKeyring_IsUnverifiable()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await (await SealedChainAsync(cs, 2)).DisposeAsync();

        await using var withoutK1 = LedgerHost.Build(cs, o =>
        {
            o.Keys.Clear();
            o.Keys["k2"] = (TestKeys.K2, 2);
            o.CurrentKeyId = "k2";
        });

        (await VerifyAsync(withoutK1)).Should().Match<AuditChainVerificationResult>(r =>
            r.Status == AuditVerificationStatus.Unverifiable && r.FailureKind == AuditVerificationFailureKind.UnknownKey && r.FailedAtSequence == 1);
    }

    [Fact]
    public async Task RecordForgedWithTheRetiredKey_AfterRotation_IsKeyRegression()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await (await SealedChainAsync(cs, 2)).DisposeAsync();
        await using var rotated = await SealedChainAsync(cs, 1, o =>
        {
            o.Keys["k2"] = (TestKeys.K2, 2);
            o.CurrentKeyId = "k2";
        });

        // An attacker holding the leaked K1 appends a perfectly MACed record after the K2 record.
        await ForgeAppendAsync(cs, sequence: 4, keyId: "k1", key: Convert.FromBase64String(TestKeys.K1));

        (await VerifyAsync(rotated)).Should().Match<AuditChainVerificationResult>(r =>
            r.Status == AuditVerificationStatus.Broken && r.FailureKind == AuditVerificationFailureKind.KeyRegression && r.FailedAtSequence == 4);
    }

    [Fact]
    public async Task LeakedRetiredKey_ReSealingAPreRotationRecord_IsLinkMismatch()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await (await SealedChainAsync(cs, 3)).DisposeAsync();
        await using var rotated = await SealedChainAsync(cs, 1, o =>
        {
            o.Keys["k2"] = (TestKeys.K2, 2);
            o.CurrentKeyId = "k2";
        });

        // Edit record 2 and recompute its own MAC with the leaked K1: record 2 now verifies on its own,
        // but record 3 still commits to record 2's original MAC.
        await LedgerTestDatabase.TamperAsync(cs, $"UPDATE {AuditLedgerSchema.RecordsTable} SET action = 'OrderApproved' WHERE id = {RecordAt(2)}");
        await ResealAsync(cs, sequence: 2, keyId: "k1", key: Convert.FromBase64String(TestKeys.K1));

        (await VerifyAsync(rotated)).Should().Match<AuditChainVerificationResult>(r =>
            r.Status == AuditVerificationStatus.Broken && r.FailureKind == AuditVerificationFailureKind.LinkMismatch && r.FailedAtSequence == 3);
    }

    [Fact]
    public async Task VerifyRecord_ChecksOneRecordAgainstItsSeal()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = await SealedChainAsync(cs, 2);
        var id = await LedgerTestDatabase.ScalarAsync<Guid>(cs, RecordAt(1)[1..^1]);

        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyRecordAsync(id)))!.IsIntact.Should().BeTrue();

        await LedgerTestDatabase.TamperAsync(cs, $"UPDATE {AuditLedgerSchema.RecordsTable} SET resource_id = 'other' WHERE id = '{id}'");
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyRecordAsync(id)))!.FailureKind
            .Should().Be(AuditVerificationFailureKind.HashMismatch);

        host.Context.TenantId = TestRequestContext.TenantB;
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyRecordAsync(id))).Should().BeNull("another tenant's record is invisible");
    }

    [Fact]
    public async Task VerifyRecord_UnsealedRecord_IsNotSealed()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var record = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(1)));

        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyRecordAsync(record.Id)))!
            .Should().Match<AuditRecordVerificationResult>(r => r.Status == AuditVerificationStatus.Unverifiable && r.FailureKind == AuditVerificationFailureKind.NotSealed);
    }

    /// <summary>Appends a forged record + link at <paramref name="sequence"/> that is correctly MACed under <paramref name="key"/>.</summary>
    internal static async Task ForgeAppendAsync(string cs, long sequence, string keyId, byte[] key)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var fields = new LedgerRecordFields
        {
            Id = Guid.CreateVersion7(),
            TenantId = TestRequestContext.TenantA,
            ResourceType = "Order",
            ResourceId = "forged",
            Action = "OrderApproved",
            Outcome = AuditOutcome.Succeeded,
            ActorId = "attacker",
            ActorKind = SharedKernel.Execution.Context.ActorKind.User,
            SourceService = "system",
            OccurredOn = AuditTimestamp.Truncate(DateTimeOffset.UtcNow),
            PayloadHash = AuditV3Format.ComputePayloadCommitment(salt, null, null),
        };

        var previous = await PreviousMacAsync(cs, sequence);
        var mac = new HmacSha256Signer().Sign(AuditV3Format.EncodeLinkMessage(fields, sequence, previous, true, keyId, AuditV3Format.HmacSha256), key);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await LedgerDb.InsertAsync(connection, null, new PendingLedgerRecord(fields, salt, null, null), CancellationToken.None);
        await using var link = new NpgsqlCommand(
            $"""
            INSERT INTO {AuditLedgerSchema.LinksTable} (record_id, tenant_id, resource_type, sequence, previous_mac, mac, key_id, algorithm, format_version, record_insert_xid, sealed_on)
            VALUES (@id, @t, 'Order', @s, @p, @m, @k, 'HMAC-SHA256', 3, (SELECT insert_xid FROM {AuditLedgerSchema.RecordsTable} WHERE id = @id), now())
            """, connection);
        link.Parameters.AddWithValue("id", fields.Id);
        link.Parameters.AddWithValue("t", TestRequestContext.TenantA.Value);
        link.Parameters.AddWithValue("s", sequence);
        link.Parameters.AddWithValue("p", previous);
        link.Parameters.AddWithValue("m", mac);
        link.Parameters.AddWithValue("k", keyId);
        await link.ExecuteNonQueryAsync();
    }

    /// <summary>Recomputes the MAC of the link at <paramref name="sequence"/> over the record's current content, as an attacker with the key would.</summary>
    internal static async Task ResealAsync(string cs, long sequence, string keyId, byte[] key)
    {
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        LedgerRecordFields fields;
        byte[]? previous;
        await using (var read = new NpgsqlCommand(
            $"SELECT {LedgerDb.RecordColumns}, l.previous_mac FROM {AuditLedgerSchema.RecordsTable} r JOIN {AuditLedgerSchema.LinksTable} l ON l.record_id = r.id WHERE l.resource_type = 'Order' AND l.sequence = @s", connection))
        {
            read.Parameters.AddWithValue("s", sequence);
            await using var reader = await read.ExecuteReaderAsync();
            await reader.ReadAsync();
            fields = LedgerDb.ReadFields(reader);
            previous = reader.IsDBNull(LedgerDb.RecordColumnCount) ? null : reader.GetFieldValue<byte[]>(LedgerDb.RecordColumnCount);
        }

        var mac = new HmacSha256Signer().Sign(
            AuditV3Format.EncodeLinkMessage(fields, sequence, previous ?? [], previous is not null, keyId, AuditV3Format.HmacSha256), key);
        await LedgerTestDatabase.TamperAsync(cs,
            $"UPDATE {AuditLedgerSchema.LinksTable} SET mac = decode('{Convert.ToHexStringLower(mac)}', 'hex'), key_id = '{keyId}' WHERE resource_type = 'Order' AND sequence = {sequence}");
    }

    private static async Task<byte[]> PreviousMacAsync(string cs, long sequence)
    {
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT mac FROM {AuditLedgerSchema.LinksTable} WHERE resource_type = 'Order' AND sequence = @s", connection);
        command.Parameters.AddWithValue("s", sequence - 1);
        return (byte[])(await command.ExecuteScalarAsync())!;
    }
}
