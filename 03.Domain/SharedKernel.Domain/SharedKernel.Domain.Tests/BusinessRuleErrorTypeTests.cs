using FluentAssertions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-16: P-043/WO-010 regression — BusinessRuleViolationException carries ErrorType.BusinessRule.
/// </summary>
public class BusinessRuleErrorTypeTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private sealed class AlwaysBrokenRule : IBusinessRule
    {
        public string Code => "test.rule";
        public string Message => "rule is broken";
        public bool IsBroken() => true;
    }

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate(Guid id, IClock clock) : base(id, clock) { }
        public TestAggregate() : base() { }
        public void Enforce(IBusinessRule rule) => CheckRule(rule);
    }

    [Fact]
    public void BusinessRuleViolationException_Error_HasBusinessRuleType()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FixedClock());
        var rule = new AlwaysBrokenRule();

        BusinessRuleViolationException? caught = null;
        try { aggregate.Enforce(rule); }
        catch (BusinessRuleViolationException ex) { caught = ex; }

        caught.Should().NotBeNull();
        caught!.Error.Type.Should().Be(ErrorType.BusinessRule,
            "BusinessRuleViolationException must use ErrorType.BusinessRule, not ErrorType.Unexpected");
    }

    [Fact]
    public void BusinessRuleViolationException_Error_CodeIsTheRulesOwnCode()
    {
        var ex = new BusinessRuleViolationException(new AlwaysBrokenRule());
        ex.Error.Code.Should().Be("test.rule");
    }

    [Fact]
    public void BusinessRuleViolationException_IsA_DomainException()
    {
        var ex = new BusinessRuleViolationException(new AlwaysBrokenRule());
        ex.Should().BeAssignableTo<SharedKernel.Core.Exceptions.DomainException>();
    }

    [Fact]
    public void CatchingDomainException_CatchesBusinessRuleViolationException()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FixedClock());

        var act = () =>
        {
            try { aggregate.Enforce(new AlwaysBrokenRule()); }
            catch (SharedKernel.Core.Exceptions.DomainException) { throw; }
        };

        act.Should().Throw<SharedKernel.Core.Exceptions.DomainException>();
    }
}
