using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Auditing;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>Signed checkpoints: A15 anchor re-authentication, A20 scope gating / verify-before-sign / pinned keys, truncation.</summary>
[Collection("AuditPostgres")]
public sealed class CheckpointTests(PostgreSqlContainerFixture fixture)
{
    private const string SigningKeyId = "checkpoints-2026";

    private static InMemorySigningKeyProvider SigningKeys(params string[] ids) =>
        new(ids.Select(id => SigningKey.FromECDsa(id, ECDsa.Create(ECCurve.NamedCurves.nistP256))));

    private static LedgerHost Host(string cs, InMemorySigningKeyProvider keys, Action<LedgerHostOptions>? configure = null) =>
        LedgerHost.Build(cs, o =>
        {
            o.CheckpointSigningKeyId = SigningKeyId;
            o.SigningKeys = keys;
            configure?.Invoke(o);
        });

    private static async Task AppendAndSealAsync(LedgerHost host, int count, int start = 1)
    {
        for (var i = start; i < start + count; i++)
        {
            await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(new AuditEntry
            {
                Action = "OrderRejected", ResourceType = "Order", ResourceId = $"o-{i}", Outcome = AuditOutcome.Failed,
            }));
        }

        await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());
    }

    private static Task<AuditChainCheckpoint> CheckpointAsync(LedgerHost host) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IAuditCheckpointService>().CreateCheckpointAsync("Order"));

    private static Task<AuditChainVerificationResult> VerifyFromAsync(LedgerHost host, AuditChainCheckpoint checkpoint, AuditChainCheckpoint? head = null) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainFromCheckpointAsync(checkpoint, head));

    [Fact]
    public async Task Checkpoint_AnchorsTheHead_IsStoredInTheSink_AndVerifies()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 3);

        var checkpoint = await CheckpointAsync(host);
        checkpoint.Sequence.Should().Be(3);
        checkpoint.TenantId.Should().Be(TestRequestContext.TenantA);
        (await host.Get<IAuditCheckpointSink>().GetLatestAsync(TestRequestContext.TenantA, "Order"))!.Id.Should().Be(checkpoint.Id);

        await AppendAndSealAsync(host, 2, start: 4);
        var result = await VerifyFromAsync(host, checkpoint);
        result.IsIntact.Should().BeTrue();
        result.RecordsChecked.Should().Be(3, "the anchor and the two later records");
        result.HeadSequence.Should().Be(5);
    }

    [Fact]
    public async Task AnchorRecordEdited_IsDetected_EvenThoughItsStoredMacIsUnchanged()
    {
        // A15: the old verifier only compared the anchor's stored hash with the checkpoint and never
        // re-authenticated the anchor's content.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 3);
        var checkpoint = await CheckpointAsync(host);

        await LedgerTestDatabase.TamperAsync(cs,
            $"UPDATE {AuditLedgerSchema.RecordsTable} SET actor_id = 'someone-else' WHERE id = (SELECT record_id FROM {AuditLedgerSchema.LinksTable} WHERE sequence = 3)");

        (await VerifyFromAsync(host, checkpoint)).Should().Match<AuditChainVerificationResult>(r =>
            r.Status == AuditVerificationStatus.Broken && r.FailureKind == AuditVerificationFailureKind.HashMismatch && r.FailedAtSequence == 3);
    }

    [Fact]
    public async Task AnchorLinkReplaced_IsAnchorMismatch()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 2);
        var checkpoint = await CheckpointAsync(host);

        await LedgerTestDatabase.TamperAsync(cs, $"DELETE FROM {AuditLedgerSchema.LinksTable} WHERE sequence = 2");

        (await VerifyFromAsync(host, checkpoint)).FailureKind.Should().Be(AuditVerificationFailureKind.AnchorMismatch);
    }

    [Fact]
    public async Task TailDeletedAfterALaterCheckpoint_IsTailTruncated()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 2);
        var early = await CheckpointAsync(host);
        await AppendAndSealAsync(host, 3, start: 3);
        var late = await CheckpointAsync(host);

        (await VerifyFromAsync(host, early, late)).IsIntact.Should().BeTrue();
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order"))).IsIntact
            .Should().BeTrue("the chain on its own cannot see a truncated tail");

        await LedgerTestDatabase.TamperAsync(cs, $"DELETE FROM {AuditLedgerSchema.LinksTable} WHERE sequence >= 4");

        (await VerifyFromAsync(host, early, late)).Should().Match<AuditChainVerificationResult>(r =>
            r.FailureKind == AuditVerificationFailureKind.TailTruncated && r.FailedAtSequence == 5);
    }

    [Fact]
    public async Task TailReplacedWithValidlyMacedRecords_IsAnchorMismatchAtTheExpectedHead()
    {
        // A leaked chain key lets an attacker rewrite the tail so every MAC and link verifies; only the
        // independently signed head checkpoint catches it.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 2);
        var early = await CheckpointAsync(host);
        await AppendAndSealAsync(host, 1, start: 3);
        var late = await CheckpointAsync(host);

        await LedgerTestDatabase.TamperAsync(cs, $"DELETE FROM {AuditLedgerSchema.LinksTable} WHERE sequence = 3");
        await TamperDetectionTests.ForgeAppendAsync(cs, sequence: 3, keyId: "k1", key: Convert.FromBase64String(TestKeys.K1));

        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order"))).IsIntact.Should().BeTrue();
        (await VerifyFromAsync(host, early, late)).Should().Match<AuditChainVerificationResult>(r =>
            r.FailureKind == AuditVerificationFailureKind.AnchorMismatch && r.FailedAtSequence == 3);
    }

    [Fact]
    public async Task ForgedSignature_IsRejected()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 2);
        var checkpoint = await CheckpointAsync(host);

        var forged = checkpoint with { Sequence = 1 };
        await host.Invoking(h => VerifyFromAsync(h, forged)).Should().ThrowAsync<ArgumentException>().WithMessage("*not authentic*");
    }

    [Fact]
    public async Task CheckpointSignedByAnUnpinnedKey_IsRejected_EvenWithAValidSignature()
    {
        // A20: the verifier must not trust the key id the checkpoint names.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId, "rogue");
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 1);
        var checkpoint = await CheckpointAsync(host);

        var signer = host.Get<IAsymmetricSignatureService>();
        var rogue = checkpoint with { SigningKeyId = "rogue" };
        rogue = rogue with { Signature = await signer.SignAsync(SharedKernel.Persistence.EfCore.Auditing.Format.AuditV3Format.EncodeCheckpoint(rogue), "rogue") };

        await host.Invoking(h => VerifyFromAsync(h, rogue)).Should().ThrowAsync<ArgumentException>().WithMessage("*AcceptedCheckpointSigningKeyIds*");
    }

    [Fact]
    public async Task PinnedPreviousSigningKey_StillVerifies()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys("checkpoints-2025", SigningKeyId);
        await using (var old = Host(cs, keys, o => o.CheckpointSigningKeyId = "checkpoints-2025"))
        {
            await AppendAndSealAsync(old, 1);
            await CheckpointAsync(old);
        }

        await using var host = Host(cs, keys, o => o.AcceptedCheckpointSigningKeyIds.Add("checkpoints-2025"));
        var previous = (await host.Get<IAuditCheckpointSink>().GetLatestAsync(TestRequestContext.TenantA, "Order"))!;
        (await VerifyFromAsync(host, previous)).IsIntact.Should().BeTrue();
    }

    [Fact]
    public async Task Checkpoint_OfABrokenChain_IsRefused()
    {
        // A20: verify before signing — never sign a tampered head.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 3);
        await CheckpointAsync(host);
        await AppendAndSealAsync(host, 2, start: 4);

        await LedgerTestDatabase.TamperAsync(cs,
            $"UPDATE {AuditLedgerSchema.RecordsTable} SET action = 'x' WHERE id = (SELECT record_id FROM {AuditLedgerSchema.LinksTable} WHERE sequence = 4)");

        var act = () => CheckpointAsync(host);
        (await act.Should().ThrowAsync<AuditChainIntegrityException>()).Which.Verification.FailedAtSequence.Should().Be(4);
    }

    [Fact]
    public async Task CheckpointOfAnotherTenant_RequiresACrossTenantScope()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 1);
        host.Context.TenantId = TestRequestContext.TenantB;

        var act = () => host.InScopeAsync(sp => sp.GetRequiredService<IAuditCheckpointService>().CreateCheckpointForChainAsync(TestRequestContext.TenantA, "Order"));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cross-tenant scope*");

        using (host.Scope.Enter("audit test"))
            (await act()).Sequence.Should().Be(1);
    }

    [Fact]
    public async Task VerifyingAnotherTenantsCheckpoint_RequiresACrossTenantScope()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 1);
        var checkpoint = await CheckpointAsync(host);
        host.Context.TenantId = TestRequestContext.TenantB;

        await host.Invoking(h => VerifyFromAsync(h, checkpoint)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SealerEmission_CheckpointsEveryChainThatMoved_Once()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 2);
        host.Context.TenantId = TestRequestContext.TenantB;
        await AppendAndSealAsync(host, 1);

        var writer = host.Get<AuditCheckpointWriter>();
        (await writer.EmitChangedAsync(DateTimeOffset.MinValue, CancellationToken.None)).Should().Be(2);
        (await writer.EmitChangedAsync(DateTimeOffset.MinValue, CancellationToken.None)).Should().Be(0, "no head moved");
        (await host.Get<IAuditCheckpointSink>().GetLatestAsync(TestRequestContext.TenantB, "Order"))!.Sequence.Should().Be(1);
    }

    [Fact]
    public async Task CheckpointsTable_IsAppendOnly()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys(SigningKeyId);
        await using var host = Host(cs, keys);
        await AppendAndSealAsync(host, 1);
        await CheckpointAsync(host);

        var act = () => LedgerTestDatabase.ExecuteAsync(cs, $"UPDATE {AuditLedgerSchema.CheckpointsTable} SET sequence = 99");
        await act.Should().ThrowAsync<global::Npgsql.PostgresException>().WithMessage("*append-only*");
    }
}
