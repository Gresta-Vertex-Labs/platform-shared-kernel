using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Extensions;
using SharedKernel.Persistence.EfCore.Auditing.Tests.TestFixtures;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>
/// Proves signed chain-head checkpoints against real PostgreSQL: creation, self-verification, the
/// cheaper checkpoint-anchored verification path, and — the capability checkpoints specifically exist
/// for — detecting that records were deleted from a chain's TAIL after the checkpoint was made
/// (something <see cref="IAuditQueryService.VerifyFullChainAsync"/> alone cannot detect; see
/// <see cref="AuditChainTamperDetectionPostgresTests.VerifyFullChainAsync_TailDeleted_LooksIntactOnItsOwn_DocumentedLimitation"/>).
/// </summary>
[Collection("AuditPostgres")]
public sealed class AuditCheckpointPostgresTests
{
    private const string SigningKeyId = "checkpoint-test-key";
    private readonly PostgreSqlContainerFixture _fixture;

    public AuditCheckpointPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    private static ServiceProvider BuildWithCheckpoints(string connectionString, FakeAuditActorContext actorContext)
    {
        var signingKey = SigningKey.FromECDsa(SigningKeyId, ECDsa.Create(ECCurve.NamedCurves.nistP256));
        var signingKeyProvider = new InMemorySigningKeyProvider([signingKey]);

        return AuditTestHost.Build(connectionString, actorContext, configureServices: services =>
        {
            services.AddSingleton<ISigningKeyProvider>(signingKeyProvider);
            services.AddSingleton<IAsymmetricSignatureService, AsymmetricSignatureService>();
        }, configureBuilder: builder => builder.WithAuditChainCheckpoints(SigningKeyId));
    }

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
    public async Task CreateCheckpointAsync_AnchorsCurrentChainHead()
    {
        var connectionString = ConnectionString("sk_audit_checkpoint_create");
        var tenantId = Guid.NewGuid();
        await using var sp = BuildWithCheckpoints(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        AuditRecord second;
        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            await writer.RecordAsync(FailedEntry("Order", "order-1"));
            second = await writer.RecordAsync(FailedEntry("Order", "order-2"));
        }

        await using var checkpointScope = sp.CreateAsyncScope();
        var checkpointService = checkpointScope.ServiceProvider.GetRequiredService<IAuditCheckpointService>();
        var checkpoint = await checkpointService.CreateCheckpointAsync(tenantId, "Order");

        checkpoint.Sequence.Should().Be(second.Sequence);
        checkpoint.RecordHash.Should().Be(second.RecordHash);
        checkpoint.Signature.Should().NotBeEmpty();
    }

    [Fact]
    public async Task VerifyChainFromCheckpointAsync_UntamperedChain_ReportsIntact()
    {
        var connectionString = ConnectionString("sk_audit_checkpoint_verify_intact");
        var tenantId = Guid.NewGuid();
        await using var sp = BuildWithCheckpoints(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            await writer.RecordAsync(FailedEntry("Order", "order-1"));
            await writer.RecordAsync(FailedEntry("Order", "order-2"));
        }

        AuditChainCheckpoint checkpoint;
        await using (var scope = sp.CreateAsyncScope())
        {
            checkpoint = await scope.ServiceProvider.GetRequiredService<IAuditCheckpointService>().CreateCheckpointAsync(tenantId, "Order");
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            await writer.RecordAsync(FailedEntry("Order", "order-3"));
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyChainFromCheckpointAsync(checkpoint, expectedHead: null);

        result.IsIntact.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyChainFromCheckpointAsync_CheckpointedRecordDeleted_ReportsBroken()
    {
        var connectionString = ConnectionString("sk_audit_checkpoint_anchor_deleted");
        var tenantId = Guid.NewGuid();
        await using var sp = BuildWithCheckpoints(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        AuditRecord anchor;
        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            anchor = await writer.RecordAsync(FailedEntry("Order", "order-1"));
        }

        AuditChainCheckpoint checkpoint;
        await using (var scope = sp.CreateAsyncScope())
        {
            checkpoint = await scope.ServiceProvider.GetRequiredService<IAuditCheckpointService>().CreateCheckpointAsync(tenantId, "Order");
        }

        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = $"""DELETE FROM "{AuditSchema.TableName}" WHERE "{AuditSchema.Id}" = @id""";
            command.Parameters.AddWithValue("id", anchor.Id);
            await command.ExecuteNonQueryAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyChainFromCheckpointAsync(checkpoint, expectedHead: null);

        result.IsIntact.Should().BeFalse();
        result.BrokenAtSequence.Should().Be(checkpoint.Sequence);
    }

    [Fact]
    public async Task VerifyChainFromCheckpointAsync_ForgedSignature_Throws()
    {
        var connectionString = ConnectionString("sk_audit_checkpoint_forged_signature");
        var tenantId = Guid.NewGuid();
        await using var sp = BuildWithCheckpoints(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        AuditRecord written;
        await using (var scope = sp.CreateAsyncScope())
        {
            written = await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        var forgedCheckpoint = new AuditChainCheckpoint
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ResourceType = "Order",
            Sequence = written.Sequence,
            RecordHash = written.RecordHash,
            CreatedOn = DateTimeOffset.UtcNow,
            SigningKeyId = SigningKeyId,
            Signature = new byte[64], // never genuinely signed
        };

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();

        var act = async () => await queryService.VerifyChainFromCheckpointAsync(forgedCheckpoint, expectedHead: null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task VerifyChainFromCheckpointAsync_TailDeletedAfterExpectedHeadCheckpoint_DetectsTruncation()
    {
        var connectionString = ConnectionString("sk_audit_checkpoint_tail_truncation");
        var tenantId = Guid.NewGuid();
        await using var sp = BuildWithCheckpoints(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        AuditChainCheckpoint earlyCheckpoint;
        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            await writer.RecordAsync(FailedEntry("Order", "order-1"));
            earlyCheckpoint = await scope.ServiceProvider.GetRequiredService<IAuditCheckpointService>().CreateCheckpointAsync(tenantId, "Order");
        }

        AuditRecord fourth;
        AuditChainCheckpoint laterCheckpoint;
        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            await writer.RecordAsync(FailedEntry("Order", "order-2"));
            await writer.RecordAsync(FailedEntry("Order", "order-3"));
            fourth = await writer.RecordAsync(FailedEntry("Order", "order-4"));
            laterCheckpoint = await scope.ServiceProvider.GetRequiredService<IAuditCheckpointService>().CreateCheckpointAsync(tenantId, "Order");
        }

        laterCheckpoint.Sequence.Should().Be(fourth.Sequence);

        // Delete everything after the EARLY checkpoint (sequences 2-4) — an attacker restoring an
        // older backup, or deleting the tail after the fact. VerifyFullChainAsync alone (and even
        // verifying from earlyCheckpoint with no expectedHead) would report this as "intact" — it
        // looks exactly like a chain that simply never grew past sequence 1. Supplying the LATER,
        // independently-signed checkpoint as the expected head is what makes the truncation provable.
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = $"""DELETE FROM "{AuditSchema.TableName}" WHERE "{AuditSchema.Sequence}" > @afterSequence""";
            command.Parameters.AddWithValue("afterSequence", earlyCheckpoint.Sequence);
            await command.ExecuteNonQueryAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();

        var withoutExpectedHead = await queryService.VerifyChainFromCheckpointAsync(earlyCheckpoint, expectedHead: null);
        withoutExpectedHead.IsIntact.Should().BeTrue("without an expected head, a truncated tail is indistinguishable from a chain that stopped there naturally");

        var withExpectedHead = await queryService.VerifyChainFromCheckpointAsync(earlyCheckpoint, laterCheckpoint);
        withExpectedHead.IsIntact.Should().BeFalse("the later, independently-signed checkpoint proves the chain once reached sequence 4 — it no longer does");
        withExpectedHead.BrokenAtSequence.Should().Be(laterCheckpoint.Sequence);
    }
}
