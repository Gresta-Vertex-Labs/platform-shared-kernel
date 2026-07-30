using FluentAssertions;
using SharedKernel.Domain.Policies;

namespace SharedKernel.Domain.Tests;

public class PolicyCompositeTests
{
    // --- Test doubles ---

    private sealed record Order(bool IsActive, decimal Total);

    private sealed class ActiveOrderPolicy : IPolicy<Order>
    {
        public bool IsCompliant(Order subject) => subject.IsActive;
    }

    private sealed class HighValueOrderPolicy : IPolicy<Order>
    {
        public bool IsCompliant(Order subject) => subject.Total > 500m;
    }

    // --- AndPolicy ---

    [Fact]
    public void And_BothCompliant_ReturnsTrue()
    {
        var policy = new AndPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());
        policy.IsCompliant(new Order(true, 1000m)).Should().BeTrue();
    }

    [Fact]
    public void And_LeftNotCompliant_ReturnsFalse()
    {
        var policy = new AndPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());
        policy.IsCompliant(new Order(false, 1000m)).Should().BeFalse();
    }

    [Fact]
    public void And_RightNotCompliant_ReturnsFalse()
    {
        var policy = new AndPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());
        policy.IsCompliant(new Order(true, 100m)).Should().BeFalse();
    }

    [Fact]
    public void And_NeitherCompliant_ReturnsFalse()
    {
        var policy = new AndPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());
        policy.IsCompliant(new Order(false, 100m)).Should().BeFalse();
    }

    // --- OrPolicy ---

    [Fact]
    public void Or_BothCompliant_ReturnsTrue()
    {
        var policy = new OrPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());
        policy.IsCompliant(new Order(true, 1000m)).Should().BeTrue();
    }

    [Fact]
    public void Or_OnlyLeftCompliant_ReturnsTrue()
    {
        var policy = new OrPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());
        policy.IsCompliant(new Order(true, 100m)).Should().BeTrue();
    }

    [Fact]
    public void Or_OnlyRightCompliant_ReturnsTrue()
    {
        var policy = new OrPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());
        policy.IsCompliant(new Order(false, 1000m)).Should().BeTrue();
    }

    [Fact]
    public void Or_NeitherCompliant_ReturnsFalse()
    {
        var policy = new OrPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());
        policy.IsCompliant(new Order(false, 100m)).Should().BeFalse();
    }

    // --- NotPolicy ---

    [Fact]
    public void Not_InnerCompliant_ReturnsFalse()
    {
        var policy = new NotPolicy<Order>(new ActiveOrderPolicy());
        policy.IsCompliant(new Order(true, 0m)).Should().BeFalse();
    }

    [Fact]
    public void Not_InnerNotCompliant_ReturnsTrue()
    {
        var policy = new NotPolicy<Order>(new ActiveOrderPolicy());
        policy.IsCompliant(new Order(false, 0m)).Should().BeTrue();
    }

    // --- Extension methods ---

    [Fact]
    public void Extension_And_ProducesAndPolicy()
    {
        var result = new ActiveOrderPolicy().And(new HighValueOrderPolicy());
        result.Should().BeOfType<AndPolicy<Order>>();
    }

    [Fact]
    public void Extension_Or_ProducesOrPolicy()
    {
        var result = new ActiveOrderPolicy().Or(new HighValueOrderPolicy());
        result.Should().BeOfType<OrPolicy<Order>>();
    }

    [Fact]
    public void Extension_Not_ProducesNotPolicy()
    {
        var result = new ActiveOrderPolicy().Not();
        result.Should().BeOfType<NotPolicy<Order>>();
    }

    // --- T-38: P-312/WO-051 — IPolicy<T>.Explain default interface member ---

    private sealed class PolicyWithoutExplainOverride : IPolicy<Order>
    {
        // Deliberately does not override Explain — proves zero-breaking-change DIM addition.
        public bool IsCompliant(Order subject) => subject.IsActive;
    }

    [Fact]
    public void Explain_DefaultDim_Compliant_ReturnsEmptyString()
    {
        IPolicy<Order> policy = new PolicyWithoutExplainOverride();

        policy.Explain(new Order(true, 0m)).Should().BeEmpty();
    }

    [Fact]
    public void Explain_DefaultDim_NonCompliant_ReturnsGenericMessage()
    {
        IPolicy<Order> policy = new PolicyWithoutExplainOverride();

        policy.Explain(new Order(false, 0m))
            .Should().Be($"Policy '{nameof(PolicyWithoutExplainOverride)}' is not satisfied.");
    }

    [Fact]
    public void PolicyImplementingOnlyIsCompliant_StillCompiles_AndUsesDefaultExplanation()
    {
        // Zero-breaking-change proof: a pre-existing IPolicy<T> implementor that only declares
        // IsCompliant compiles unmodified and receives the default DIM explanation (accessible
        // through the interface type — default interface members are not promoted to the
        // implementing class's own public surface).
        IPolicy<Order> policy = new ActiveOrderPolicy();

        policy.IsCompliant(new Order(true, 0m)).Should().BeTrue();
        policy.Explain(new Order(false, 0m)).Should().NotBeEmpty();
    }

    // --- AndPolicy.Explain ---

    [Fact]
    public void AndPolicy_Explain_BothCompliant_ReturnsEmptyString()
    {
        var policy = new AndPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());

        policy.Explain(new Order(true, 1000m)).Should().BeEmpty();
    }

    [Fact]
    public void AndPolicy_Explain_LeftNotCompliant_AggregatesLeftExplanation()
    {
        var policy = new AndPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());

        var explanation = policy.Explain(new Order(false, 1000m));

        explanation.Should().Contain(nameof(ActiveOrderPolicy));
        explanation.Should().NotContain(nameof(HighValueOrderPolicy));
    }

    [Fact]
    public void AndPolicy_Explain_NeitherCompliant_AggregatesBothWithSemicolon()
    {
        var policy = new AndPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());

        var explanation = policy.Explain(new Order(false, 100m));

        explanation.Should().Contain("; ");
        explanation.Should().Contain(nameof(ActiveOrderPolicy));
        explanation.Should().Contain(nameof(HighValueOrderPolicy));
    }

    // --- OrPolicy.Explain ---

    [Fact]
    public void OrPolicy_Explain_OneCompliant_ReturnsEmptyString()
    {
        var policy = new OrPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());

        policy.Explain(new Order(true, 100m)).Should().BeEmpty();
    }

    [Fact]
    public void OrPolicy_Explain_NeitherCompliant_AggregatesBothWithSemicolon()
    {
        var policy = new OrPolicy<Order>(new ActiveOrderPolicy(), new HighValueOrderPolicy());

        var explanation = policy.Explain(new Order(false, 100m));

        explanation.Should().Contain("; ");
        explanation.Should().Contain(nameof(ActiveOrderPolicy));
        explanation.Should().Contain(nameof(HighValueOrderPolicy));
    }

    // --- NotPolicy.Explain ---

    [Fact]
    public void NotPolicy_Explain_InnerNotCompliant_ReturnsEmptyString()
    {
        var policy = new NotPolicy<Order>(new ActiveOrderPolicy());

        policy.Explain(new Order(false, 0m)).Should().BeEmpty();
    }

    [Fact]
    public void NotPolicy_Explain_InnerUnexpectedlyCompliant_ReturnsFixedGenericMessage()
    {
        var policy = new NotPolicy<Order>(new ActiveOrderPolicy());

        var explanation = policy.Explain(new Order(true, 0m));

        explanation.Should().NotBeEmpty();
        explanation.Should().Contain(nameof(NotPolicy<Order>));
    }
}
