using SharedKernel.Testing.Domain;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Domain;

public sealed class SpecificationTestBuilderTests
{
    [Fact]
    public void ExpectCount_CorrectMatchCount_AssertPasses()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);
        var entities = new[] { new TestSpecEntity(1), new TestSpecEntity(10), new TestSpecEntity(20) };

        SpecificationTestBuilder<TestSpecEntity>.For(spec)
            .Against(entities)
            .ExpectCount(2)
            .Assert();
    }

    [Fact]
    public void ExpectCount_WrongMatchCount_AssertThrows()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);
        var entities = new[] { new TestSpecEntity(1), new TestSpecEntity(10) };

        var builder = SpecificationTestBuilder<TestSpecEntity>.For(spec).Against(entities).ExpectCount(5);

        Assert.Throws<InvalidOperationException>(builder.Assert);
    }

    [Fact]
    public void ExpectMatch_AllMatchingEntitiesSatisfyPredicate_AssertPasses()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);
        var entities = new[] { new TestSpecEntity(10), new TestSpecEntity(20) };

        SpecificationTestBuilder<TestSpecEntity>.For(spec)
            .Against(entities)
            .ExpectMatch(e => e.Value % 10 == 0)
            .Assert();
    }

    [Fact]
    public void ExpectMatch_FailingPredicate_AssertThrows()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);
        var entities = new[] { new TestSpecEntity(10), new TestSpecEntity(11) };

        var builder = SpecificationTestBuilder<TestSpecEntity>.For(spec)
            .Against(entities)
            .ExpectMatch(e => e.Value % 10 == 0);

        Assert.Throws<InvalidOperationException>(builder.Assert);
    }

    [Fact]
    public void For_NullSpec_Throws() =>
        Assert.Throws<ArgumentNullException>(() => SpecificationTestBuilder<TestSpecEntity>.For(null!));

    [Fact]
    public void Against_NullEntities_Throws()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);
        var builder = SpecificationTestBuilder<TestSpecEntity>.For(spec);

        Assert.Throws<ArgumentNullException>(() => builder.Against(null!));
    }

    [Fact]
    public void NoExpectations_AssertDoesNotThrow()
    {
        var spec = new TestSpecEntitySpec(threshold: 5);

        SpecificationTestBuilder<TestSpecEntity>.For(spec)
            .Against([new TestSpecEntity(1)])
            .Assert();
    }
}
