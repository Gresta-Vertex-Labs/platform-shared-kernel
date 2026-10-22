using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Extensions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.DomainServices;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.Policies;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// <c>TryCheckRule</c> on the three bases returns the same <see cref="Error"/> the throwing
/// <c>CheckRule</c> path carries, and never throws for a broken rule.
/// </summary>
public class TryCheckRuleTests
{
    // --- Test doubles ---

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private sealed class BrokenRule : IBusinessRule
    {
        public string Code => "test.rule";
        public string Message => "rule is broken";
        public bool IsBroken() => true;
    }

    private sealed class HoldingRule : IBusinessRule
    {
        public string Code => "test.rule";
        public string Message => "rule holds";
        public bool IsBroken() => false;
    }

    private sealed class CountingRule : IBusinessRule
    {
        public int IsBrokenCalls { get; private set; }
        public int CodeReads { get; private set; }
        public int MessageReads { get; private set; }

        public string Code
        {
            get
            {
                CodeReads++;
                return "test.rule";
            }
        }

        public string Message
        {
            get
            {
                MessageReads++;
                return "counted";
            }
        }

        public bool IsBroken()
        {
            IsBrokenCalls++;
            return true;
        }
    }

    private sealed class CompliantPolicy : IPolicy<int>
    {
        public bool IsCompliant(int subject) => true;
    }

    private sealed class NonCompliantPolicy : IPolicy<int>
    {
        public bool IsCompliant(int subject) => false;
    }

    private sealed class Ticket : AggregateRoot<Guid>
    {
        public Ticket() : base(Guid.NewGuid(), new FixedClock()) { }

        public Result Check(IBusinessRule rule) => TryCheckRule(rule);

        public void Enforce(IBusinessRule rule) => CheckRule(rule);

        public Result Close(IBusinessRule rule)
        {
            var check = TryCheckRule(rule);
            return check.IsFailure ? check : Result.Success();
        }

        public Result CloseWrapped(IBusinessRule rule) => ResultTry.Try(() => CheckRule(rule), ex => ((SharedKernelException)ex).Error);
    }

    private sealed class Quantity : ValueObject
    {
        public Quantity() => EnsureValid();

        public Result Check(IBusinessRule rule) => TryCheckRule(rule);

        public void Enforce(IBusinessRule rule) => CheckRule(rule);

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return 1;
        }

        protected override IEnumerable<Error>? Validate() => null;
    }

    private sealed class Transfers : DomainService
    {
        public Result Check(IBusinessRule rule) => TryCheckRule(rule);

        public void Enforce(IBusinessRule rule) => CheckRule(rule);
    }

    // --- Each base ---

    [Fact]
    public void Aggregate_SatisfiedRule_ReturnsSuccess() =>
        new Ticket().Check(new HoldingRule()).IsSuccess.Should().BeTrue();

    [Fact]
    public void Aggregate_BrokenRule_ReturnsBusinessRuleFailure()
    {
        var result = new Ticket().Check(new BrokenRule());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.BusinessRule);
        result.Error.Code.Should().Be("test.rule");
        result.Error.Message.Should().Be("rule is broken");
    }

    [Fact]
    public void Aggregate_NullRule_ThrowsArgumentNull() =>
        FluentActions.Invoking(() => new Ticket().Check(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void ValueObject_SatisfiedRule_ReturnsSuccess() =>
        new Quantity().Check(new HoldingRule()).IsSuccess.Should().BeTrue();

    [Fact]
    public void ValueObject_BrokenRule_ReturnsBusinessRuleFailure()
    {
        var result = new Quantity().Check(new BrokenRule());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.BusinessRule);
        result.Error.Code.Should().Be("test.rule");
        result.Error.Message.Should().Be("rule is broken");
    }

    [Fact]
    public void ValueObject_NullRule_ThrowsArgumentNull() =>
        FluentActions.Invoking(() => new Quantity().Check(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void DomainService_SatisfiedRule_ReturnsSuccess() =>
        new Transfers().Check(new HoldingRule()).IsSuccess.Should().BeTrue();

    [Fact]
    public void DomainService_BrokenRule_ReturnsBusinessRuleFailure()
    {
        var result = new Transfers().Check(new BrokenRule());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.BusinessRule);
        result.Error.Code.Should().Be("test.rule");
        result.Error.Message.Should().Be("rule is broken");
    }

    [Fact]
    public void DomainService_NullRule_ThrowsArgumentNull() =>
        FluentActions.Invoking(() => new Transfers().Check(null!)).Should().Throw<ArgumentNullException>();

    // --- Evaluation count ---

    [Fact]
    public void BrokenRule_IsEvaluatedOnce_AndCodeAndMessageReadOnce()
    {
        var rule = new CountingRule();

        new Ticket().Check(rule);

        rule.IsBrokenCalls.Should().Be(1);
        rule.CodeReads.Should().Be(1);
        rule.MessageReads.Should().Be(1);
    }

    // --- Parity with the throwing path ---

    public static TheoryData<string> ParityRules => new() { "simple", "and", "or", "not", "policy" };

    private static IBusinessRule BuildRule(string kind) => kind switch
    {
        "simple" => new BrokenRule(),
        "and" => new BrokenRule().And(new HoldingRule()),
        "or" => new BrokenRule().Or(new BrokenRule()),
        "not" => new HoldingRule().Not("test.not", "must not hold"),
        "policy" => new NonCompliantPolicy().ToRule(7, "test.policy"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(ParityRules))]
    public void BrokenRule_ResultError_EqualsViolationExceptionError_OnEveryBase(string kind)
    {
        var rule = BuildRule(kind);
        var thrown = FluentActions.Invoking(() => new Ticket().Enforce(rule))
            .Should().Throw<BusinessRuleViolationException>().Which.Error;

        new Ticket().Check(rule).Error.Should().Be(thrown);
        new Quantity().Check(rule).Error.Should().Be(thrown);
        new Transfers().Check(rule).Error.Should().Be(thrown);
        thrown.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void CompliantPolicyRule_ReturnsSuccess() =>
        new Ticket().Check(new CompliantPolicy().ToRule(7, "test.policy")).IsSuccess.Should().BeTrue();

    // --- Behavior method ---

    [Fact]
    public void BehaviorMethod_TryCheckRule_MatchesResultTryWrappedCheckRule_WithoutThrowing()
    {
        var ticket = new Ticket();
        var rule = new BrokenRule();

        Result viaTryCheck = default;
        FluentActions.Invoking(() => viaTryCheck = ticket.Close(rule)).Should().NotThrow();
        var viaWrapped = ticket.CloseWrapped(rule);

        viaTryCheck.IsFailure.Should().BeTrue();
        viaTryCheck.Error.Should().Be(viaWrapped.Error);
    }

    [Fact]
    public void BehaviorMethod_SatisfiedRule_ReturnsSuccess() =>
        new Ticket().Close(new HoldingRule()).IsSuccess.Should().BeTrue();
}
