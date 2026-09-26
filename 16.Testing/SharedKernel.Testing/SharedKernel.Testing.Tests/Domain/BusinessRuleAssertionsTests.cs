using SharedKernel.Testing.Domain;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Domain;

public sealed class BusinessRuleAssertionsTests
{
    [Fact]
    public void ShouldBeBroken_BrokenRule_DoesNotThrow() => new AlwaysBrokenRule().ShouldBeBroken();

    [Fact]
    public void ShouldBeBroken_NotBrokenRule_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new NeverBrokenRule().ShouldBeBroken());

    [Fact]
    public void ShouldNotBeBroken_NotBrokenRule_DoesNotThrow() => new NeverBrokenRule().ShouldNotBeBroken();

    [Fact]
    public void ShouldNotBeBroken_BrokenRule_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new AlwaysBrokenRule().ShouldNotBeBroken());

    [Fact]
    public void ShouldBeBroken_NullRule_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((SharedKernel.Domain.BusinessRules.IBusinessRule)null!).ShouldBeBroken());
}
