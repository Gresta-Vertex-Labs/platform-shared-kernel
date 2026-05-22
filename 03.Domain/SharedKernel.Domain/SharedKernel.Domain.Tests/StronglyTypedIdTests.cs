using FluentAssertions;
using SharedKernel.Domain.StronglyTypedIds;

namespace SharedKernel.Domain.Tests;

public class StronglyTypedIdTests
{
    // --- Test doubles ---

    private sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
    private sealed record ProductId(int Value) : StronglyTypedId<int>(Value);

    // --- Implicit operator ---

    [Fact]
    public void ImplicitOperator_UnwrapsToGuid()
    {
        var guid = Guid.NewGuid();
        var id = new OrderId(guid);

        Guid unwrapped = id; // implicit cast

        unwrapped.Should().Be(guid);
    }

    [Fact]
    public void ImplicitOperator_IntId_UnwrapsToInt()
    {
        var id = new ProductId(42);

        int unwrapped = id;

        unwrapped.Should().Be(42);
    }

    // --- ToString ---

    [Fact]
    public void ToString_ReturnsValueToString()
    {
        var guid = Guid.NewGuid();
        var id = new OrderId(guid);

        id.ToString().Should().Be(guid.ToString());
    }

    [Fact]
    public void ToString_IntId_ReturnsIntString()
    {
        var id = new ProductId(99);
        id.ToString().Should().Be("99");
    }

    // --- Value equality (record semantics) ---

    [Fact]
    public void Equals_SameValue_ReturnsTrue()
    {
        var guid = Guid.NewGuid();
        var a = new OrderId(guid);
        var b = new OrderId(guid);

        a.Should().Be(b);
    }

    [Fact]
    public void Equals_DifferentValue_ReturnsFalse()
    {
        var a = new OrderId(Guid.NewGuid());
        var b = new OrderId(Guid.NewGuid());

        a.Should().NotBe(b);
    }

    [Fact]
    public void GetHashCode_SameValue_SameHash()
    {
        var guid = Guid.NewGuid();
        var a = new OrderId(guid);
        var b = new OrderId(guid);

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    // --- IStronglyTypedId contract ---

    [Fact]
    public void Value_ReturnsWrappedValue()
    {
        var guid = Guid.NewGuid();
        var id = new OrderId(guid);

        id.Value.Should().Be(guid);
    }
}
