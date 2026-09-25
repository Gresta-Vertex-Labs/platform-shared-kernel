using FluentAssertions;
using SharedKernel.Execution.Auditing;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class FakeAuditQueryServiceTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static async Task<FakeAuditTrailWriter> SeedAsync(FakeAuditActorContext context, params string[] resourceIds)
    {
        var writer = new FakeAuditTrailWriter(context);
        foreach (var id in resourceIds)
        {
            await writer.RecordAsync(new AuditEntry { Action = "Updated", ResourceType = "Order", ResourceId = id, Outcome = AuditOutcome.Succeeded });
        }

        return writer;
    }

    [Fact]
    public async Task QueryAsync_ReturnsOnlyTheCallersTenant()
    {
        var context = new FakeAuditActorContext(tenantId: TenantA);
        var writer = await SeedAsync(context, "o-1");
        context.TenantId = TenantB;
        await writer.RecordAsync(new AuditEntry { Action = "Updated", ResourceType = "Order", ResourceId = "o-1", Outcome = AuditOutcome.Succeeded });
        context.TenantId = TenantA;

        var page = await new FakeAuditQueryService(writer, context).QueryAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = "o-1" });

        page.Items.Should().ContainSingle().Which.TenantId.Should().Be(TenantA);
    }

    [Fact]
    public async Task QueryAsync_PagesWithCursor()
    {
        var context = new FakeAuditActorContext(tenantId: TenantA);
        var writer = await SeedAsync(context, "a", "b", "c");
        var service = new FakeAuditQueryService(writer, context);

        var first = await service.QueryAsync(new AuditRecordQuery { ResourceType = "Order", Limit = 2 });
        var second = await service.QueryAsync(new AuditRecordQuery { ResourceType = "Order", Limit = 2, Cursor = first.NextCursor });

        first.HasMore.Should().BeTrue();
        second.Items.Should().ContainSingle();
        first.Items.Concat(second.Items).Select(r => r.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task QueryAcrossTenantsAsync_RequiresAnActiveScope()
    {
        var context = new FakeAuditActorContext(tenantId: TenantA);
        var writer = await SeedAsync(context, "a");

        await new FakeAuditQueryService(writer, context)
            .Invoking(s => s.QueryAcrossTenantsAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = "a" }))
            .Should().ThrowAsync<InvalidOperationException>();

        var active = new ActiveScope();
        (await new FakeAuditQueryService(writer, context, active).QueryAcrossTenantsAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = "a" }))
            .Items.Should().ContainSingle();
    }

    [Fact]
    public async Task VerifyChainAsync_ContiguousChain_IsIntact_AndCountsErasedPayloads()
    {
        var context = new FakeAuditActorContext(tenantId: TenantA);
        var writer = await SeedAsync(context, "a", "b");
        writer.ErasePayload(writer.Records[0].Id);
        var service = new FakeAuditQueryService(writer, context);

        var result = await service.VerifyChainAsync("Order");
        result.IsIntact.Should().BeTrue();
        result.ErasedPayloads.Should().Be(1);

        (await service.VerifyChainAsync("Order", requirePayloads: true)).FailureKind.Should().Be(AuditVerificationFailureKind.PayloadErased);
    }

    [Fact]
    public async Task VerifyChainAsync_Gap_ReportsSequenceGap()
    {
        var context = new FakeAuditActorContext(tenantId: TenantA);
        var writer = await SeedAsync(context, "a", "b", "c");
        writer.Seed(writer.Records.Where(r => r.Sequence != 2));

        var result = await new FakeAuditQueryService(writer, context).VerifyChainAsync("Order");

        result.Status.Should().Be(AuditVerificationStatus.Broken);
        result.FailureKind.Should().Be(AuditVerificationFailureKind.SequenceGap);
        result.FailedAtSequence.Should().Be(2);
    }

    [Fact]
    public async Task VerifyChainFromCheckpointAsync_IsNotSupported()
    {
        var writer = new FakeAuditTrailWriter();
        var checkpoint = new AuditChainCheckpoint
        {
            Id = Guid.NewGuid(), ResourceType = "Order", Sequence = 1, HeadMac = [1], CreatedOn = DateTimeOffset.UtcNow, SigningKeyId = "k", Signature = [1],
        };

        await new FakeAuditQueryService(writer).Invoking(s => s.VerifyChainFromCheckpointAsync(checkpoint)).Should().ThrowAsync<NotSupportedException>();
    }

    private sealed class ActiveScope : ICrossTenantScope
    {
        public bool IsActive => true;

        public IDisposable Enter(string reason) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
