using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Auditing;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Telemetry;
using SharedKernel.Cryptography.Signing;
using System.Security.Cryptography;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>
/// The ledger's spans and instruments, recorded through the shared recorders: one source and one meter named
/// <c>SharedKernel.Persistence</c>, <c>persistence audit.{operation}</c> spans, <c>sharedkernel.persistence.audit.*</c>
/// instruments with a unit and durations in seconds, and failures recorded by the shared recorder (status Error,
/// <c>error.code</c>, the exception event; a cancellation is never an error).
/// </summary>
[Collection("AuditPostgres")]
public sealed class AuditTelemetryTests(PostgreSqlContainerFixture fixture)
{
    private const string SigningKeyId = "telemetry-2026";

    private static AuditEntry Entry(int i, string? idempotencyKey = null) => new()
    {
        Action = "OrderRejected",
        ResourceType = "Order",
        ResourceId = $"o-{i}",
        Outcome = AuditOutcome.Failed,
        AfterSnapshot = $"{{\"n\":{i}}}",
        IdempotencyKey = idempotencyKey,
    };

    private static async Task AppendAsync(LedgerHost host, int count, string? idempotencyKey = null)
    {
        for (var i = 1; i <= count; i++)
            await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(i, idempotencyKey)));
    }

    private static InMemorySigningKeyProvider SigningKeys() =>
        new([SigningKey.FromECDsa(SigningKeyId, ECDsa.Create(ECCurve.NamedCurves.nistP256))]);

    [Fact]
    public async Task Append_RecordsADurationInSeconds_AndACountedIdempotentDuplicate()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        using var metrics = MetricRecorder.StartRecording(PersistenceTelemetry.Name);

        await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(1, "idem-telemetry")));
        await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(Entry(1, "idem-telemetry")));

        metrics
            .ShouldHaveInstrument("sharedkernel.persistence.audit.append.duration", unit: "s", kind: InstrumentKind.Histogram)
            .ShouldHaveInstrument("sharedkernel.persistence.audit.append.idempotent_duplicates", unit: "{append}", kind: InstrumentKind.Counter)
            .ShouldHaveSum("sharedkernel.persistence.audit.append.idempotent_duplicates", 1);
        metrics.MeasurementsOf("sharedkernel.persistence.audit.append.duration").Should().HaveCount(2)
            .And.OnlyContain(m => m.Value >= 0 && m.Value < 30, "a duration is recorded in seconds, never milliseconds");
        metrics.ShouldFollowMetricConvention();
    }

    [Fact]
    public async Task SealingPass_RecordsASpan_RecordsRecordsDurationAndLag_InSeconds()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        await AppendAsync(host, 3);
        using var metrics = MetricRecorder.StartRecording(PersistenceTelemetry.Name);
        using var spans = ActivityRecorder.StartRecording(PersistenceTelemetry.Name);

        var pass = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());

        pass.RecordsSealed.Should().Be(3);
        spans.RecordedActivities.Where(a => a.OperationName == AuditSealSpanName).Should().ContainSingle()
            .Which.ShouldHaveKind(ActivityKind.Internal)
            .ShouldHaveSource(PersistenceTelemetry.Name)
            .ShouldHaveTag("persistence.audit.sealed_records", 3)
            .ShouldHaveOutcome(WellKnownOutcomes.Success)
            .ShouldBeRecordedSuccess();
        metrics
            .ShouldHaveInstrument("sharedkernel.persistence.audit.seal.records", unit: "{record}")
            .ShouldHaveSum("sharedkernel.persistence.audit.seal.records", 3)
            .ShouldHaveInstrument("sharedkernel.persistence.audit.seal.duration", unit: "s", kind: InstrumentKind.Histogram)
            .ShouldHaveInstrument("sharedkernel.persistence.audit.seal.lag", unit: "s", kind: InstrumentKind.Histogram)
            .ShouldFollowMetricConvention();
        metrics.MeasurementsOf("sharedkernel.persistence.audit.seal.duration").Should().ContainSingle()
            .Which.Value.Should().BeLessThan(30, "a duration is recorded in seconds");
    }

    [Fact]
    public async Task SealingPass_ThatFails_RecordsTheExceptionOnTheSpan_AndRethrows()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs, o => o.ConfigureServices = s => s.AddSingleton<IAuditRecordAuthenticator, ThrowingAuthenticator>());
        await AppendAsync(host, 1);
        using var spans = ActivityRecorder.StartRecording(PersistenceTelemetry.Name);

        var act = () => host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());

        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        spans.RecordedActivities.Where(a => a.OperationName == AuditSealSpanName).Should().ContainSingle()
            .Which.ShouldHaveOutcome(WellKnownOutcomes.Exception)
            .ShouldBeRecordedException(thrown.GetType());
    }

    [Fact]
    public async Task Verification_OfAnIntactChain_RecordsASuccessSpan_AndOfABrokenOne_CountsTheFailureKind()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        await AppendAsync(host, 3);
        await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());
        using var metrics = MetricRecorder.StartRecording(PersistenceTelemetry.Name);
        using var spans = ActivityRecorder.StartRecording(PersistenceTelemetry.Name);

        var intact = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order"));
        intact.IsIntact.Should().BeTrue();

        await LedgerTestDatabase.TamperAsync(cs, $"UPDATE {AuditLedgerSchema.RecordsTable} SET action = 'OrderApproved' WHERE resource_id = 'o-2'");
        var broken = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order"));
        broken.IsIntact.Should().BeFalse();

        var verifySpans = spans.RecordedActivities.Where(a => a.OperationName == AuditVerifySpanName).ToList();
        verifySpans.Should().HaveCount(2);
        verifySpans[0].ShouldHaveTag("persistence.audit.resource_type", "Order").ShouldHaveOutcome(WellKnownOutcomes.Success).ShouldBeRecordedSuccess();
        verifySpans[1].ShouldHaveTag("persistence.audit.resource_type", "Order")
            .ShouldHaveTag("persistence.audit.failure_kind", AuditVerificationFailureKind.HashMismatch.ToString())
            .ShouldHaveOutcome(WellKnownOutcomes.Failure);
        metrics
            .ShouldHaveInstrument("sharedkernel.persistence.audit.chain.verification_failures", unit: "{verification}", kind: InstrumentKind.Counter)
            .ShouldHaveSum(
                "sharedkernel.persistence.audit.chain.verification_failures",
                1,
                new KeyValuePair<string, object?>("persistence.audit.failure_kind", AuditVerificationFailureKind.HashMismatch.ToString()));
    }

    [Fact]
    public async Task Verification_WhoseCallerCancels_RecordsACancellation_NeverAnError()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        await AppendAsync(host, 1);
        await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());
        using var spans = ActivityRecorder.StartRecording(PersistenceTelemetry.Name);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order", false, cancelled.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
        spans.RecordedActivities.Where(a => a.OperationName == AuditVerifySpanName).Should().ContainSingle()
            .Which.ShouldHaveOutcome(WellKnownOutcomes.Canceled)
            .ShouldBeRecordedCancellation();
    }

    [Fact]
    public async Task Checkpoint_RecordsASpan_AndCountsTheEmission_AndOverABrokenChainRecordsTheErrorCode()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        using var keys = SigningKeys();
        await using var host = LedgerHost.Build(cs, o =>
        {
            o.CheckpointSigningKeyId = SigningKeyId;
            o.SigningKeys = keys;
        });
        await AppendAsync(host, 3);
        await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());
        using var metrics = MetricRecorder.StartRecording(PersistenceTelemetry.Name);
        using var spans = ActivityRecorder.StartRecording(PersistenceTelemetry.Name);

        await host.InScopeAsync(sp => sp.GetRequiredService<IAuditCheckpointService>().CreateCheckpointAsync("Order"));

        spans.RecordedActivities.Where(a => a.OperationName == AuditCheckpointSpanName).Should().ContainSingle()
            .Which.ShouldHaveOutcome(WellKnownOutcomes.Success).ShouldBeRecordedSuccess();
        metrics
            .ShouldHaveInstrument("sharedkernel.persistence.audit.checkpoint.emitted", unit: "{checkpoint}", kind: InstrumentKind.Counter)
            .ShouldHaveSum("sharedkernel.persistence.audit.checkpoint.emitted", 1);

        await LedgerTestDatabase.TamperAsync(cs, $"UPDATE {AuditLedgerSchema.RecordsTable} SET action = 'OrderApproved' WHERE resource_id = 'o-3'");
        spans.Clear();
        var act = () => host.InScopeAsync(sp => sp.GetRequiredService<IAuditCheckpointService>().CreateCheckpointAsync("Order"));
        await act.Should().ThrowAsync<AuditChainIntegrityException>();

        spans.RecordedActivities.Where(a => a.OperationName == AuditCheckpointSpanName).Should().ContainSingle()
            .Which.ShouldHaveOutcome(WellKnownOutcomes.Exception)
            .ShouldBeRecordedException(typeof(AuditChainIntegrityException), PersistenceErrorCodes.Audit.ChainNotIntact);
    }

    [Fact]
    public async Task PayloadErasure_CountsTheErasedPayloads()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        await AppendAsync(host, 2);
        await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());
        using var metrics = MetricRecorder.StartRecording(PersistenceTelemetry.Name);

        var record = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>()
            .QueryAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = "o-1" }));
        await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().ErasePayloadAsync(record.Items[0].Id, "data subject request"));

        metrics
            .ShouldHaveInstrument("sharedkernel.persistence.audit.payload.erased", unit: "{payload}", kind: InstrumentKind.Counter)
            .ShouldHaveSum("sharedkernel.persistence.audit.payload.erased", 1);
    }

    private sealed class ThrowingAuthenticator : IAuditRecordAuthenticator
    {
        public string Algorithm => "HMAC-SHA256";

        public ValueTask<AuditKeyDescriptor> GetCurrentKeyAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The key source is unavailable.");

        public ValueTask<AuditKeyDescriptor?> FindKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The key source is unavailable.");

        public ValueTask<byte[]> ComputeMacAsync(string keyId, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The key source is unavailable.");

        public ValueTask<bool> VerifyMacAsync(string keyId, ReadOnlyMemory<byte> message, ReadOnlyMemory<byte> mac, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The key source is unavailable.");
    }

    private const string AuditSealSpanName = "persistence audit.seal";
    private const string AuditVerifySpanName = "persistence audit.verify";
    private const string AuditCheckpointSpanName = "persistence audit.checkpoint";
}
