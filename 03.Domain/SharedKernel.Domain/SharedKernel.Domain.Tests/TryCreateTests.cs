using FluentAssertions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-27: P-054/WO-011 — TryCreate&lt;T&gt; factory helper tests.
/// </summary>
public class TryCreateTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private sealed record OrderCreatedEvent : DomainEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed class AlwaysBrokenRule : IBusinessRule
    {
        public string Message => "rule is broken";
        public bool IsBroken() => true;
    }

    private sealed class Order : AggregateRoot<Guid>
    {
        public string CustomerName { get; private set; } = string.Empty;

        private Order(Guid id, string customerName, IClock clock) : base(id, clock)
        {
            CheckRule(new NoEmptyNameRule(customerName));
            CustomerName = customerName;
        }

        public Order() : base() { }

        public static Result<Order> Create(Guid id, string customerName, IClock clock)
            => TryCreate(() => new Order(id, customerName, clock));
    }

    private sealed class NoEmptyNameRule : IBusinessRule
    {
        private readonly string _name;
        public NoEmptyNameRule(string name) => _name = name;
        public string Message => "Customer name must not be empty.";
        public bool IsBroken() => string.IsNullOrWhiteSpace(_name);
    }

    // --- Success path ---

    [Fact]
    public void TryCreate_Success_ReturnsSuccessResult()
    {
        var result = Order.Create(Guid.NewGuid(), "Alice", new FixedClock());

        result.IsSuccess.Should().BeTrue();
        result.Value.CustomerName.Should().Be("Alice");
    }

    // --- BusinessRuleViolationException → Result.Failure ---

    [Fact]
    public void TryCreate_BusinessRuleViolationException_ReturnsFailure()
    {
        var result = Order.Create(Guid.NewGuid(), string.Empty, new FixedClock());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void TryCreate_BusinessRuleViolationException_ErrorCode_IsRuleViolated()
    {
        var result = Order.Create(Guid.NewGuid(), "", new FixedClock());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Domain.RuleViolated);
    }

    // --- ValidationException → Result.Failure ---

    private sealed class AlwaysInvalidValueOrder : AggregateRoot<Guid>
    {
        private AlwaysInvalidValueOrder() : base() { }

        public static Result<AlwaysInvalidValueOrder> CreateInvalid()
            => TryCreate<AlwaysInvalidValueOrder>(() =>
                throw new SharedKernel.Core.Exceptions.ValidationException(
                    [Error.Validation("test.error", "Always invalid")]));
    }

    [Fact]
    public void TryCreate_ValidationException_ReturnsFailure()
    {
        var result = AlwaysInvalidValueOrder.CreateInvalid();

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    // --- Existing aggregate tests unchanged ---

    [Fact]
    public void TryCreate_ExistingAggregateTests_Unaffected()
    {
        var clock = new FixedClock();
        var result = Order.Create(Guid.NewGuid(), "Bob", clock);
        result.IsSuccess.Should().BeTrue();
        result.Value.CustomerName.Should().Be("Bob");
    }
}
