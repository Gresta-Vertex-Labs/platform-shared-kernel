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
}
