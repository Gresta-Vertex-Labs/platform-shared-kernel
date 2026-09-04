using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

/// <summary>Proves <see cref="FakeAuditActorContext"/>'s zero-config defaults and settable overrides.</summary>
public sealed class FakeAuditActorContextTests
{
    [Fact]
    public void Constructor_NoArguments_DefaultsToNonEmptyValues()
    {
        var context = new FakeAuditActorContext();

        Assert.False(string.IsNullOrWhiteSpace(context.ActorId));
        Assert.NotEqual(Guid.Empty, context.TenantId);
    }

    [Fact]
    public void Constructor_ExplicitValues_AreUsed()
    {
        var tenantId = Guid.NewGuid();

        var context = new FakeAuditActorContext("actor-42", tenantId);

        Assert.Equal("actor-42", context.ActorId);
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public void Properties_AreSettable()
    {
        var context = new FakeAuditActorContext { ActorId = "changed", TenantId = Guid.Empty };

        Assert.Equal("changed", context.ActorId);
        Assert.Equal(Guid.Empty, context.TenantId);
    }
}
