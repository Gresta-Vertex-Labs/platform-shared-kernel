using SharedKernel.Contracts.Events;

namespace SharedKernel.Contracts.Tests;

public sealed class IntegrationEventDescriptorTests
{
    [Fact]
    public void For_ReadsNameAndVersionFromTheAttribute()
    {
        var descriptor = IntegrationEventDescriptor.For<OrderPlaced>();

        descriptor.Name.Should().Be("orders.order-placed");
        descriptor.Version.Should().Be(2);
        descriptor.EventType.Should().Be<OrderPlaced>();
        descriptor.ToString().Should().Be("orders.order-placed v2");
    }

    [Fact]
    public void For_VersionDefaultsToOne() =>
        IntegrationEventDescriptor.For<OrderShipped>().Version.Should().Be(1);

    [Fact]
    public void For_IsCachedPerType() =>
        IntegrationEventDescriptor.For<OrderPlaced>().Should().BeSameAs(IntegrationEventDescriptor.For(typeof(OrderPlaced)));

    [Theory]
    [InlineData(typeof(Unnamed), "has no [IntegrationEvent")]
    [InlineData(typeof(UpperCaseName), "declares the name 'Orders.OrderPlaced'")]
    [InlineData(typeof(DoubleSeparator), "declares the name 'orders..placed'")]
    [InlineData(typeof(VersionZero), "declares version 0")]
    [InlineData(typeof(DerivedWithoutAttribute), "has no [IntegrationEvent")]
    [InlineData(typeof(IIntegrationEvent), "is not a concrete type")]
    [InlineData(typeof(string), "does not implement IIntegrationEvent")]
    public void For_RejectsInvalidDeclarations(Type eventType, string expectedProblem)
    {
        var act = () => IntegrationEventDescriptor.For(eventType);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{eventType.FullName}'*{expectedProblem}*");
    }

    [Fact]
    public void For_RejectsASecondTypeClaimingTheSameNameAndVersion()
    {
        IntegrationEventDescriptor.For<DuplicateA>();

        var act = () => IntegrationEventDescriptor.For<DuplicateB>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*orders.duplicate*already declares*");
        IntegrationEventDescriptor.For<DuplicateA>().Name.Should().Be("orders.duplicate");
    }

    [Fact]
    public void For_Null_Throws() =>
        FluentActions.Invoking(() => IntegrationEventDescriptor.For(null!)).Should().Throw<ArgumentNullException>();
}
