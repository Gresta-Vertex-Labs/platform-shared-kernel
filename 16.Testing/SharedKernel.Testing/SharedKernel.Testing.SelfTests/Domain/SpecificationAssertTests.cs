using SharedKernel.Testing.Domain;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Domain;

public sealed class SpecificationAssertTests
{
    [Fact]
    public void Satisfies_MatchingEntity_DoesNotThrow()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);
        SpecificationAssert.Satisfies(spec, new TestSpecEntity(10));
    }

    [Fact]
    public void Satisfies_NonMatchingEntity_Throws()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);
        Assert.Throws<InvalidOperationException>(() => SpecificationAssert.Satisfies(spec, new TestSpecEntity(1)));
    }

    [Fact]
    public void Satisfies_NullCriteria_AlwaysSatisfies()
    {
        var spec = new AllMatchSpec();
        SpecificationAssert.Satisfies(spec, new TestSpecEntity(-100));
    }

    [Fact]
    public void DoesNotSatisfy_NonMatchingEntity_DoesNotThrow()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);
        SpecificationAssert.DoesNotSatisfy(spec, new TestSpecEntity(1));
    }

    [Fact]
    public void DoesNotSatisfy_MatchingEntity_Throws()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);
        Assert.Throws<InvalidOperationException>(() => SpecificationAssert.DoesNotSatisfy(spec, new TestSpecEntity(10)));
    }

    [Fact]
    public void Satisfies_NullSpec_Throws() =>
        Assert.Throws<ArgumentNullException>(() => SpecificationAssert.Satisfies<TestSpecEntity>(null!, new TestSpecEntity(1)));
}
