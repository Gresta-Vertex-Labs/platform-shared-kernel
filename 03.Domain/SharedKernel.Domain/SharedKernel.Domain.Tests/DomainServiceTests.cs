using FluentAssertions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.DomainServices;
using SharedKernel.Domain.Exceptions;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-20: P-047/WO-011 — DomainService.CheckRule tests.
/// </summary>
public class DomainServiceTests
{
    private sealed class AlwaysBrokenRule : IBusinessRule
    {
        public string Message => "rule is broken";
        public bool IsBroken() => true;
    }

    private sealed class NeverBrokenRule : IBusinessRule
    {
        public string Message => "rule is fine";
        public bool IsBroken() => false;
    }

    private sealed class TestDomainService : DomainService
    {
        public void EnforceRule(IBusinessRule rule) => CheckRule(rule);
    }

    [Fact]
    public void DomainService_CheckRule_BrokenRule_ThrowsBusinessRuleViolationException()
    {
        var service = new TestDomainService();
        var rule = new AlwaysBrokenRule();

        var act = () => service.EnforceRule(rule);

        act.Should().Throw<BusinessRuleViolationException>()
            .Which.Rule.Should().BeOfType<AlwaysBrokenRule>();
    }

    [Fact]
    public void DomainService_CheckRule_NonBrokenRule_DoesNotThrow()
    {
        var service = new TestDomainService();

        var act = () => service.EnforceRule(new NeverBrokenRule());

        act.Should().NotThrow();
    }

    [Fact]
    public void DomainService_HasNoMandatoryConstructorParameters()
    {
        // DomainService subclasses can be instantiated without injecting anything.
        var service = new TestDomainService();
        service.Should().NotBeNull();
    }

    [Fact]
    public void DomainService_Implements_IDomainService()
    {
        var service = new TestDomainService();
        service.Should().BeAssignableTo<Abstractions.IDomainService>();
    }
}
