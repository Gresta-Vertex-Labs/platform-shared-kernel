using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-22: P-049/WO-011 — Exception hierarchy tests.
/// </summary>
public class ExceptionHierarchyTests
{
    private sealed class AlwaysBrokenRule : IBusinessRule
    {
        public string Code => "test.rule";
        public string Message => "rule is broken";
        public bool IsBroken() => true;
    }

    // --- BusinessRuleViolationException hierarchy ---

    [Fact]
    public void BusinessRuleViolationException_IsA_DomainException()
    {
        var ex = new BusinessRuleViolationException(new AlwaysBrokenRule());

        ex.Should().BeAssignableTo<DomainException>();
    }

    [Fact]
    public void BusinessRuleViolationException_IsA_SharedKernelException()
    {
        var ex = new BusinessRuleViolationException(new AlwaysBrokenRule());

        ex.Should().BeAssignableTo<SharedKernelException>();
    }

    // --- DomainNotFoundException hierarchy and behavior ---

    [Fact]
    public void DomainNotFoundException_IsA_DomainException()
    {
        var ex = new DomainNotFoundException(typeof(string), "some-id");

        ex.Should().BeAssignableTo<DomainException>();
    }

    [Fact]
    public void DomainNotFoundException_Message_HasCorrectFormat()
    {
        var ex = new DomainNotFoundException(typeof(string), "abc-123");

        ex.Message.Should().Be("String 'abc-123' was not found.");
    }

    [Fact]
    public void DomainNotFoundException_Error_HasNotFoundType()
    {
        var ex = new DomainNotFoundException(typeof(string), 99);

        ex.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void DomainNotFoundException_AggregateType_IsCorrect()
    {
        var ex = new DomainNotFoundException(typeof(int), 42);

        ex.AggregateType.Should().Be(typeof(int));
    }

    [Fact]
    public void DomainNotFoundException_AggregateId_IsCorrect()
    {
        var aggregateId = Guid.NewGuid();
        var ex = new DomainNotFoundException(typeof(object), aggregateId);

        ex.AggregateId.Should().Be(aggregateId);
    }

    // --- Catching DomainException catches both domain exception types ---

    [Fact]
    public void CatchDomainException_CatchesBothDomainExceptions()
    {
        DomainException? caught1 = null;
        DomainException? caught2 = null;

        try { throw new BusinessRuleViolationException(new AlwaysBrokenRule()); }
        catch (DomainException ex) { caught1 = ex; }

        try { throw new DomainNotFoundException(typeof(string), "x"); }
        catch (DomainException ex) { caught2 = ex; }

        caught1.Should().BeOfType<BusinessRuleViolationException>();
        caught2.Should().BeOfType<DomainNotFoundException>();
    }
}
