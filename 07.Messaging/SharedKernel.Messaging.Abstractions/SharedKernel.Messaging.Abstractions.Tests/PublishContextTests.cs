using FluentAssertions;
using SharedKernel.Messaging.Abstractions.EventPublisher;

namespace SharedKernel.Messaging.Abstractions.Tests;

/// <summary>
/// Tests for <see cref="PublishContext"/> fluent builder API.
/// Covers T-01: WithCorrelationId, WithCausationId, WithHeader; null/empty key ArgumentException; duplicate key overwrite.
/// </summary>
public sealed class PublishContextTests
{
    [Fact]
    public void WithCorrelationId_SetsCorrelationId()
    {
        var id = Guid.NewGuid();
        var ctx = new PublishContext().WithCorrelationId(id);

        ctx.CorrelationId.Should().Be(id);
    }

    [Fact]
    public void WithCausationId_SetsCausationId()
    {
        var id = Guid.NewGuid();
        var ctx = new PublishContext().WithCausationId(id);

        ctx.CausationId.Should().Be(id);
    }

    [Fact]
    public void WithTenantId_SetsTenantId()
    {
        var id = Guid.NewGuid();
        var ctx = new PublishContext().WithTenantId(id);

        ctx.TenantId.Should().Be(id);
    }

    [Fact]
    public void WithPartitionKey_SetsPartitionKey()
    {
        var ctx = new PublishContext().WithPartitionKey("order-123");

        ctx.PartitionKey.Should().Be("order-123");
    }

    [Fact]
    public void WithPartitionKey_NullKey_ThrowsArgumentException()
    {
        var act = () => new PublishContext().WithPartitionKey(null!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("partitionKey");
    }

    [Fact]
    public void WithPartitionKey_EmptyKey_ThrowsArgumentException()
    {
        var act = () => new PublishContext().WithPartitionKey(string.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("partitionKey");
    }

    [Fact]
    public void WithHeader_AddsHeaderToCollection()
    {
        var ctx = new PublishContext().WithHeader("x-tenant", "tenant-abc");

        ctx.Headers.Should().ContainKey("x-tenant").WhoseValue.Should().Be("tenant-abc");
    }

    [Fact]
    public void WithHeader_DuplicateKey_OverwritesSilently()
    {
        var ctx = new PublishContext()
            .WithHeader("x-tenant", "first")
            .WithHeader("x-tenant", "second");

        ctx.Headers["x-tenant"].Should().Be("second");
        ctx.Headers.Count.Should().Be(1);
    }

    [Fact]
    public void WithHeader_MultipleDistinctKeys_AllPresent()
    {
        var ctx = new PublishContext()
            .WithHeader("key-a", "value-a")
            .WithHeader("key-b", "value-b");

        ctx.Headers.Should().ContainKey("key-a").WhoseValue.Should().Be("value-a");
        ctx.Headers.Should().ContainKey("key-b").WhoseValue.Should().Be("value-b");
        ctx.Headers.Count.Should().Be(2);
    }

    [Fact]
    public void WithHeader_NullKey_ThrowsArgumentException()
    {
        var act = () => new PublishContext().WithHeader(null!, "value");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("key");
    }

    [Fact]
    public void WithHeader_EmptyKey_ThrowsArgumentException()
    {
        var act = () => new PublishContext().WithHeader(string.Empty, "value");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("key");
    }

    [Fact]
    public void FluentChain_ReturnsThisInstance()
    {
        var id = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var ctx = new PublishContext();

        var returned = ctx
            .WithCorrelationId(id)
            .WithCausationId(causationId)
            .WithTenantId(tenantId)
            .WithPartitionKey("order-123")
            .WithHeader("x-flag", "true");

        returned.Should().BeSameAs(ctx);
    }

    [Fact]
    public void DefaultState_AllPropertiesAreNull()
    {
        var ctx = new PublishContext();

        ctx.CorrelationId.Should().BeNull();
        ctx.CausationId.Should().BeNull();
        ctx.TenantId.Should().BeNull();
        ctx.PartitionKey.Should().BeNull();
        ctx.Headers.Should().BeEmpty();
    }
}
