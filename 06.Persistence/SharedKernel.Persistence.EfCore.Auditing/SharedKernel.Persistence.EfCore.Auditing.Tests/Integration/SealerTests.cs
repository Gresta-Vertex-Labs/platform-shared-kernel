using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Execution.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>The sealer: commit-order safety, contiguous per-chain sequences, one sealer at a time, lag probe.</summary>
[Collection("AuditPostgres")]
public sealed class SealerTests(PostgreSqlContainerFixture fixture)
{
    private static AuditEntry Failed(string resourceId, string resourceType = "Order") => new()
    {
        Action = "OrderRejected",
        ResourceType = resourceType,
        ResourceId = resourceId,
        Outcome = AuditOutcome.Failed,
    };

    private static Task<AuditRecord> WriteAsync(LedgerHost host, AuditEntry entry) =>
        host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(entry));

    private static Task<AuditSealPassResult> SealAsync(LedgerHost host) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());

    private static Task<AuditChainVerificationResult> VerifyAsync(LedgerHost host, string resourceType = "Order") =>
        host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync(resourceType));

    /// <summary>Inserts a succeeded record inside an explicit transaction the test controls.</summary>
    private static async Task<(NpgsqlConnection, NpgsqlTransaction, AuditRecord)> WriteInOpenTransactionAsync(LedgerHost host, string resourceId)
    {
        var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        host.Ambient.Current = (connection, transaction);
        try
        {
            var record = await WriteAsync(host, Failed(resourceId) with { Outcome = AuditOutcome.Succeeded });
            return (connection, transaction, record);
        }
        finally
        {
            host.Ambient.Current = null;
        }
    }

    [Fact]
    public async Task Seal_ChainsRecordsPerChain_AndTheChainVerifies()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        for (var i = 0; i < 5; i++)
            await WriteAsync(host, Failed($"o-{i}"));
        await WriteAsync(host, Failed("c-1", "Customer"));

        var pass = await SealAsync(host);

        pass.Should().Be(new AuditSealPassResult(true, 6));
        var page = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().QueryAsync(new AuditRecordQuery { ResourceType = "Order" }));
        page.Items.Select(r => r.Sequence).Should().BeEquivalentTo(new long?[] { 1, 2, 3, 4, 5 });
        page.Items.Should().OnlyContain(r => r.IsSealed && r.KeyId == "k1");

        var result = await VerifyAsync(host);
        result.IsIntact.Should().BeTrue();
        result.RecordsChecked.Should().Be(5);
        result.HeadSequence.Should().Be(5);
        (await VerifyAsync(host, "Customer")).RecordsChecked.Should().Be(1);
    }

    [Fact]
    public async Task Seal_ALongRunningTransaction_ThatCommitsLate_IsSealedInCommitSafeOrder()
    {
        // The late transaction T1 gets its xid first, then T2 inserts and commits. A sealer that sealed
        // "whatever is visible" would chain T2's record first and then either skip T1's record forever
        // (watermark) or append it out of transaction order. The xid horizon holds T2 back until T1 ends.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);

        var (lateConnection, lateTransaction, lateRecord) = await WriteInOpenTransactionAsync(host, "late");
        var (earlyConnection, earlyTransaction, earlyRecord) = await WriteInOpenTransactionAsync(host, "early");
        await earlyTransaction.CommitAsync();
        await earlyConnection.DisposeAsync();

        (await SealAsync(host)).RecordsSealed.Should().Be(0, "the committed record is behind a still-running older transaction");
        (await host.Get<IAuditSealingProbe>().ProbeAsync()).UnsealedRecords.Should().Be(1);

        await lateTransaction.CommitAsync();
        await lateConnection.DisposeAsync();

        (await SealAsync(host)).RecordsSealed.Should().Be(2);
        var page = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().QueryAsync(new AuditRecordQuery { ResourceType = "Order" }));
        page.Items.Single(r => r.Id == lateRecord.Id).Sequence.Should().Be(1, "the older transaction's record comes first");
        page.Items.Single(r => r.Id == earlyRecord.Id).Sequence.Should().Be(2);
        (await VerifyAsync(host)).IsIntact.Should().BeTrue();
        (await host.Get<IAuditSealingProbe>().ProbeAsync()).UnsealedRecords.Should().Be(0);
    }

    [Fact]
    public async Task Seal_ARolledBackTransaction_LeavesNoGap()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);

        var (connection, transaction, _) = await WriteInOpenTransactionAsync(host, "doomed");
        await WriteAsync(host, Failed("kept"));
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        (await SealAsync(host)).RecordsSealed.Should().Be(1);
        (await VerifyAsync(host)).Should().Match<AuditChainVerificationResult>(r => r.IsIntact && r.RecordsChecked == 1);
    }

    [Fact]
    public async Task Seal_ManyConcurrentWriters_AndConcurrentSealers_ProduceOneContiguousIntactChain()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs, o => o.BatchSize = 7);

        var writers = Enumerable.Range(0, 60).Select(i => Task.Run(() => WriteAsync(host, Failed($"o-{i}"))));
        var sealers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 10; i++)
                await SealAsync(host);
        }));
        await Task.WhenAll(writers.Concat<Task>(sealers));
        await SealAsync(host);

        (await LedgerTestDatabase.ScalarAsync<long>(cs, $"SELECT count(*) FROM {AuditLedgerSchema.LinksTable}")).Should().Be(60);
        (await LedgerTestDatabase.ScalarAsync<long>(cs, $"SELECT max(sequence) FROM {AuditLedgerSchema.LinksTable}")).Should().Be(60);
        (await VerifyAsync(host)).Should().Match<AuditChainVerificationResult>(r => r.IsIntact && r.RecordsChecked == 60);
    }

    [Fact]
    public async Task Seal_WhileAnotherInstanceHoldsTheLock_SkipsThePass()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        await WriteAsync(host, Failed("o-1"));

        await using var holder = new NpgsqlConnection(cs);
        await holder.OpenAsync();
        await using var holderTransaction = await holder.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@k)", holder, holderTransaction))
        {
            lockCommand.Parameters.AddWithValue("k", Sealing.AuditSealingEngine.SealerLockKey);
            await lockCommand.ExecuteNonQueryAsync();
        }

        (await SealAsync(host)).Should().Be(new AuditSealPassResult(false, 0));

        await holderTransaction.RollbackAsync();
        (await SealAsync(host)).RecordsSealed.Should().Be(1);
    }

    [Fact]
    public async Task Probe_ReportsTheUnsealedTailAndItsAge()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        host.Clock.UtcNow = DateTimeOffset.UtcNow.AddMinutes(-10);
        await WriteAsync(host, Failed("o-1"));
        host.Clock.UtcNow = DateTimeOffset.UtcNow;
        await WriteAsync(host, Failed("o-2"));

        var health = await host.Get<IAuditSealingProbe>().ProbeAsync();

        health.UnsealedRecords.Should().Be(2);
        health.Lag.Should().BeGreaterThan(TimeSpan.FromMinutes(9));

        await SealAsync(host);
        (await host.Get<IAuditSealingProbe>().ProbeAsync()).Should().Be(new AuditSealingHealth(0, null, TimeSpan.Zero));
    }

    [Fact]
    public async Task HostedSealer_SealsInTheBackground_AndEmitsCheckpoints()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var signingKeys = new SharedKernel.Cryptography.Signing.InMemorySigningKeyProvider(
            [SharedKernel.Cryptography.Signing.SigningKey.FromECDsa("cp", System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256))]);
        await using var host = LedgerHost.Build(cs, o =>
        {
            o.SealerEnabled = true;
            o.CheckpointInterval = TimeSpan.FromMilliseconds(200);
            o.CheckpointSigningKeyId = "cp";
            o.SigningKeys = signingKeys;
        });

        host.Clock.Live = true;
        var hosted = host.Provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);
        try
        {
            await WriteAsync(host, Failed("o-1"));
            await WriteAsync(host, Failed("o-2"));

            var deadline = DateTime.UtcNow.AddSeconds(15);
            AuditChainCheckpoint? checkpoint = null;
            while (DateTime.UtcNow < deadline)
            {
                checkpoint = await host.Get<IAuditCheckpointSink>().GetLatestAsync(TestRequestContext.TenantA, "Order");
                if (checkpoint?.Sequence == 2)
                    break;
                await Task.Delay(100);
            }

            (await host.Get<IAuditSealingProbe>().ProbeAsync()).UnsealedRecords.Should().Be(0);
            checkpoint.Should().NotBeNull();
            checkpoint!.Sequence.Should().Be(2);
        }
        finally
        {
            foreach (var service in hosted)
                await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void SealerLockKey_IsTheNamespacedHash()
    {
        Sealing.AuditSealingEngine.SealerLockName.Should().Be("sk:audit:sealer");
        Sealing.AuditSealingEngine.SealerLockKey.Should().Be(
            SharedKernel.Persistence.Npgsql.Coordination.AdvisoryLockKeys.ToKey("sk:audit:sealer"));
    }
}
