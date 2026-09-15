using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Guards;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// AggregateRoot.TryCreate: every domain failure during construction becomes a failed
/// ValidationResult carrying all of its errors; anything else still throws.
/// </summary>
public class TryCreateTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 1, 1);
    }

    private sealed class NoEmptyNameRule(string name) : IBusinessRule
    {
        public string Code => "order.customer_name_required";
        public string Message => "Customer name must not be empty.";
        public bool IsBroken() => string.IsNullOrWhiteSpace(name);
    }

    private sealed class Order : AggregateRoot<Guid>
    {
        private Order(Guid id, string customerName, IClock clock) : base(id, clock)
        {
            CheckRule(new NoEmptyNameRule(customerName));
            CustomerName = customerName;
        }

        public string CustomerName { get; } = string.Empty;

        public static ValidationResult<Order> Create(Guid id, string customerName, IClock clock) =>
            TryCreate(() => new Order(id, customerName, clock));

        public static ValidationResult<Order> Throwing(Func<Order> factory) => TryCreate(factory);
    }

    [Fact]
    public void Success_ReturnsTheCreatedAggregate()
    {
        var result = Order.Create(Guid.NewGuid(), "Alice", new FixedClock());

        result.IsValid.Should().BeTrue();
        result.Value.CustomerName.Should().Be("Alice");
    }

    [Fact]
    public void BrokenRule_ReturnsFailure_WithTheRulesOwnCode()
    {
        var result = Order.Create(Guid.NewGuid(), "", new FixedClock());

        result.IsValid.Should().BeFalse();
        var error = result.Errors.Should().ContainSingle().Subject;
        error.Type.Should().Be(ErrorType.BusinessRule);
        error.Code.Should().Be("order.customer_name_required");
    }

    [Fact]
    public void GuardViolation_ReturnsFailure_InsteadOfThrowing()
    {
        // A null clock is rejected by Guard.Throw inside the base constructor, which throws DomainException.
        var result = Order.Create(Guid.NewGuid(), "Alice", null!);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Validation.Required);
    }

    [Fact]
    public void ValidationException_ReturnsEveryError()
    {
        var result = Order.Throwing(() => throw new ValidationException(
        [
            Error.Validation("a.required", "A is required."),
            Error.Validation("b.required", "B is required."),
        ]));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.Code).Should().Equal("a.required", "b.required");
    }

    [Fact]
    public void UnrelatedException_Propagates()
    {
        var act = () => Order.Throwing(() => throw new InvalidOperationException("defect"));

        act.Should().Throw<InvalidOperationException>().WithMessage("defect");
    }

    [Fact]
    public void GuardThrowInsideFactory_IsCaught()
    {
        var result = Order.Throwing(() =>
        {
            Guard.Throw.NullOrWhiteSpace("  ", "name");
            return null!;
        });

        result.IsValid.Should().BeFalse();
    }
}
