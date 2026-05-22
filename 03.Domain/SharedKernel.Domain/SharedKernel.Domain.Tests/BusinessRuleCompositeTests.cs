using FluentAssertions;
using SharedKernel.Domain.BusinessRules;

namespace SharedKernel.Domain.Tests;

public class BusinessRuleCompositeTests
{
    // --- Test doubles ---

    private sealed class AlwaysBroken : IBusinessRule
    {
        public string Message => "always broken";
        public bool IsBroken() => true;
    }

    private sealed class NeverBroken : IBusinessRule
    {
        public string Message => "never broken";
        public bool IsBroken() => false;
    }

    // --- AndBusinessRule ---

    [Fact]
    public void And_BothBroken_IsBroken_ReturnsTrue()
    {
        var rule = new AndBusinessRule(new AlwaysBroken(), new AlwaysBroken());
        rule.IsBroken().Should().BeTrue();
    }

    [Fact]
    public void And_LeftBroken_RightNotBroken_IsBroken_ReturnsTrue()
    {
        var rule = new AndBusinessRule(new AlwaysBroken(), new NeverBroken());
        rule.IsBroken().Should().BeTrue();
    }

    [Fact]
    public void And_LeftNotBroken_RightBroken_IsBroken_ReturnsTrue()
    {
        var rule = new AndBusinessRule(new NeverBroken(), new AlwaysBroken());
        rule.IsBroken().Should().BeTrue();
    }

    [Fact]
    public void And_NeitherBroken_IsBroken_ReturnsFalse()
    {
        var rule = new AndBusinessRule(new NeverBroken(), new NeverBroken());
        rule.IsBroken().Should().BeFalse();
    }

    [Fact]
    public void And_BothBroken_Message_AggregatesBothMessages()
    {
        var rule = new AndBusinessRule(new AlwaysBroken(), new AlwaysBroken());
        rule.Message.Should().Contain("always broken");
        rule.Message.Should().Contain("; ");
    }

    [Fact]
    public void And_OneBroken_Message_ContainsOnlyBrokenMessage()
    {
        var rule = new AndBusinessRule(new AlwaysBroken(), new NeverBroken());
        rule.Message.Should().Contain("always broken");
        rule.Message.Should().NotContain("; ");
    }

    [Fact]
    public void And_NeitherBroken_Message_IsEmpty()
    {
        var rule = new AndBusinessRule(new NeverBroken(), new NeverBroken());
        rule.Message.Should().BeEmpty();
    }

    // --- OrBusinessRule ---

    [Fact]
    public void Or_BothBroken_IsBroken_ReturnsTrue()
    {
        var rule = new OrBusinessRule(new AlwaysBroken(), new AlwaysBroken());
        rule.IsBroken().Should().BeTrue();
    }

    [Fact]
    public void Or_LeftBroken_RightNotBroken_IsBroken_ReturnsFalse()
    {
        var rule = new OrBusinessRule(new AlwaysBroken(), new NeverBroken());
        rule.IsBroken().Should().BeFalse();
    }

    [Fact]
    public void Or_NeitherBroken_IsBroken_ReturnsFalse()
    {
        var rule = new OrBusinessRule(new NeverBroken(), new NeverBroken());
        rule.IsBroken().Should().BeFalse();
    }

    [Fact]
    public void Or_Message_ContainsBothSubRuleMessages()
    {
        var rule = new OrBusinessRule(new AlwaysBroken(), new NeverBroken());
        rule.Message.Should().Contain("always broken");
        rule.Message.Should().Contain("never broken");
        rule.Message.Should().Contain(" or ");
    }

    // --- NotBusinessRule ---

    [Fact]
    public void Not_InnerBroken_IsBroken_ReturnsFalse()
    {
        var rule = new NotBusinessRule(new AlwaysBroken());
        rule.IsBroken().Should().BeFalse();
    }

    [Fact]
    public void Not_InnerNotBroken_IsBroken_ReturnsTrue()
    {
        var rule = new NotBusinessRule(new NeverBroken());
        rule.IsBroken().Should().BeTrue();
    }

    [Fact]
    public void Not_Message_ContainsNegationPrefix()
    {
        var rule = new NotBusinessRule(new AlwaysBroken());
        rule.Message.Should().Contain("Not:");
        rule.Message.Should().Contain("always broken");
    }

    // --- Extension methods ---

    [Fact]
    public void Extension_And_ProducesAndBusinessRule()
    {
        var result = new AlwaysBroken().And(new NeverBroken());
        result.Should().BeOfType<AndBusinessRule>();
        result.IsBroken().Should().BeTrue();
    }

    [Fact]
    public void Extension_Or_ProducesOrBusinessRule()
    {
        var result = new AlwaysBroken().Or(new NeverBroken());
        result.Should().BeOfType<OrBusinessRule>();
        result.IsBroken().Should().BeFalse();
    }

    [Fact]
    public void Extension_Not_ProducesNotBusinessRule()
    {
        var result = new AlwaysBroken().Not();
        result.Should().BeOfType<NotBusinessRule>();
        result.IsBroken().Should().BeFalse();
    }

    // --- Policy tests included here for coverage ---

    // (Policy tests are in a separate file; this covers the policy extension types too)
}
