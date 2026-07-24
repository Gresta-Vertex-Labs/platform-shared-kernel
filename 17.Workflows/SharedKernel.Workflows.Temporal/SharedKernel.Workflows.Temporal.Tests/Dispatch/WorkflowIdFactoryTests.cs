using FluentAssertions;
using SharedKernel.Workflows.Temporal.Dispatch;

namespace SharedKernel.Workflows.Temporal.Tests.Dispatch;

/// <summary>
/// T-02 — <see cref="IWorkflowIdFactory"/>: the tenant segment is always present; <see cref="TenantScope.None"/>
/// and a null/whitespace business key are rejected before any client call; the same inputs always
/// produce the same id.
/// </summary>
public sealed class WorkflowIdFactoryTests
{
    private readonly IWorkflowIdFactory _sut = new WorkflowIdFactory();

    [Fact]
    public void Create_ComposesTenantWorkflowTypeAndBusinessKey()
    {
        string id = _sut.Create("OrderWorkflow", "order-42", TenantScope.Of("tenant-a"));

        id.Should().Be("tenant-a:OrderWorkflow:order-42");
    }

    [Fact]
    public void Create_TenantSegmentIsAlwaysPresent()
    {
        string id = _sut.Create("OrderWorkflow", "order-42", TenantScope.Of("tenant-b"));

        id.Should().StartWith("tenant-b:");
    }

    [Fact]
    public void Create_SameInputs_AlwaysProduceSameId()
    {
        string first = _sut.Create("OrderWorkflow", "order-42", TenantScope.Of("tenant-a"));
        string second = _sut.Create("OrderWorkflow", "order-42", TenantScope.Of("tenant-a"));

        first.Should().Be(second, because: "an unstable id silently defeats the durable idempotency guarantee");
    }

    [Fact]
    public void Create_TenantScopeNone_ThrowsBeforeComposingAnything()
    {
        Action act = () => _sut.Create("OrderWorkflow", "order-42", TenantScope.None);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrWhitespaceBusinessKey_Throws(string? businessKey)
    {
        Action act = () => _sut.Create("OrderWorkflow", businessKey!, TenantScope.Of("tenant-a"));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrWhitespaceWorkflowTypeName_Throws(string? workflowTypeName)
    {
        Action act = () => _sut.Create(workflowTypeName!, "order-42", TenantScope.Of("tenant-a"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_DifferentTenants_ProduceDifferentIds()
    {
        string idA = _sut.Create("OrderWorkflow", "order-42", TenantScope.Of("tenant-a"));
        string idB = _sut.Create("OrderWorkflow", "order-42", TenantScope.Of("tenant-b"));

        idA.Should().NotBe(idB);
    }
}
