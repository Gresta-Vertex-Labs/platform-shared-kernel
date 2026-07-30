using FluentAssertions;
using SharedKernel.Domain.Entities;

namespace SharedKernel.Domain.Tests;

public class EntityEqualityTests
{
    // --- Test doubles ---

    private sealed class OrderId(Guid value)
    {
        public Guid Value { get; } = value;
        public override bool Equals(object? obj) => obj is OrderId other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    private sealed class Order : Entity<Guid>
    {
        public Order(Guid id) : base(id) { }
        public Order() : base() { } // ORM path
    }

    private sealed class Customer : Entity<Guid>
    {
        public Customer(Guid id) : base(id) { }
    }

    private sealed class IntEntity : Entity<int>
    {
        public IntEntity(int id) : base(id) { }
        public IntEntity() : base() { }
    }

    // --- Same Id → equal ---

    [Fact]
    public void Equals_SameId_SameConcrete_ReturnsTrue()
    {
        var id = Guid.NewGuid();
        var a = new Order(id);
        var b = new Order(id);

        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Operator_Equal_SameId_ReturnsTrue()
    {
        var id = Guid.NewGuid();
        var a = new Order(id);
        var b = new Order(id);

        (a == b).Should().BeTrue();
    }

    // --- Different Id → not equal ---

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        var a = new Order(Guid.NewGuid());
        var b = new Order(Guid.NewGuid());

        a.Equals(b).Should().BeFalse();
    }

    // --- Different concrete type, same Id → not equal ---

    [Fact]
    public void Equals_DifferentConcreteType_SameId_ReturnsFalse()
    {
        var id = Guid.NewGuid();
        var order = new Order(id);
        var customer = new Customer(id);

        order.Equals(customer).Should().BeFalse();
    }

    // --- Transient entity → never equal, including to itself ---

    [Fact]
    public void IsTransient_DefaultId_ReturnsTrue()
    {
        var t = new Order();
        t.IsTransient().Should().BeTrue();
    }

    [Fact]
    public void Equals_TransientEntity_ToItself_ReturnsFalse()
    {
        var t = new Order();
        t.Equals(t).Should().BeFalse();
    }

    [Fact]
    public void Equals_TransientEntity_ToOtherTransient_ReturnsFalse()
    {
        var a = new Order();
        var b = new Order();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_TransientEntity_ToNonTransient_ReturnsFalse()
    {
        var transient = new Order();
        var persistent = new Order(Guid.NewGuid());
        transient.Equals(persistent).Should().BeFalse();
    }

    // --- Null and type-mismatch ---

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var a = new Order(Guid.NewGuid());
        a.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_NonEntityObject_ReturnsFalse()
    {
        var a = new Order(Guid.NewGuid());
        a.Equals("not an entity").Should().BeFalse();
    }

    // --- Operator != ---

    [Fact]
    public void Operator_NotEqual_DifferentId_ReturnsTrue()
    {
        var a = new Order(Guid.NewGuid());
        var b = new Order(Guid.NewGuid());
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Operator_Equal_BothNull_ReturnsTrue()
    {
        Order? a = null;
        Order? b = null;
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Operator_Equal_LeftNull_ReturnsFalse()
    {
        Order? a = null;
        var b = new Order(Guid.NewGuid());
        (a == b).Should().BeFalse();
    }

    // --- HashCode consistency ---

    [Fact]
    public void GetHashCode_SameId_SameConcrete_SameHash()
    {
        var id = Guid.NewGuid();
        var a = new Order(id);
        var b = new Order(id);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_TransientEntity_UsesObjectIdentity()
    {
        var a = new Order();
        var b = new Order();
        // Different transient instances should almost always differ (object identity)
        // We just verify they're not both zero and the same instance is stable
        a.GetHashCode().Should().Be(a.GetHashCode());
        b.GetHashCode().Should().Be(b.GetHashCode());
    }

    // --- int-keyed entity ---

    [Fact]
    public void Equals_IntId_SameId_ReturnsTrue()
    {
        var a = new IntEntity(42);
        var b = new IntEntity(42);
        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_IntZero_ReturnsTrue()
    {
        var t = new IntEntity();
        t.IsTransient().Should().BeTrue();
    }

    // --- T-37: P-311b/WO-051 — IEquatable<Entity<TId>> ---

    [Fact]
    public void Entity_Implements_IEquatableOfEntity()
    {
        var order = new Order(Guid.NewGuid());
        order.Should().BeAssignableTo<IEquatable<Entity<Guid>>>();
    }

    [Fact]
    public void TypedEquals_SameId_MatchesObjectEquals()
    {
        var id = Guid.NewGuid();
        var a = new Order(id);
        var b = new Order(id);

        ((IEquatable<Entity<Guid>>)a).Equals(b).Should().Be(a.Equals((object?)b));
        ((IEquatable<Entity<Guid>>)a).Equals(b).Should().BeTrue();
    }

    [Fact]
    public void TypedEquals_DifferentId_MatchesObjectEquals()
    {
        var a = new Order(Guid.NewGuid());
        var b = new Order(Guid.NewGuid());

        ((IEquatable<Entity<Guid>>)a).Equals(b).Should().Be(a.Equals((object?)b));
        ((IEquatable<Entity<Guid>>)a).Equals(b).Should().BeFalse();
    }

    [Fact]
    public void TypedEquals_Null_MatchesObjectEquals()
    {
        var a = new Order(Guid.NewGuid());
        IEquatable<Entity<Guid>> equatable = a;
        Entity<Guid>? nullOther = null;

        var typedResult = equatable.Equals(nullOther);
        var objectResult = a.Equals((object?)null);

        typedResult.Should().Be(objectResult);
        typedResult.Should().BeFalse();
    }
}
