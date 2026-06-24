using SharedKernel.Testing.Domain;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Domain;

public sealed class DomainVersionAssertionsTests
{
    [Fact]
    public void ShouldHaveVersion_CorrectVersion_DoesNotThrow() =>
        DomainVersionAssertions.ShouldHaveVersion<TestVersionedEvent>(2);

    [Fact]
    public void ShouldHaveVersion_WrongVersion_Throws() =>
        Assert.Throws<InvalidOperationException>(() => DomainVersionAssertions.ShouldHaveVersion<TestVersionedEvent>(99));

    [Fact]
    public void ShouldHaveVersion_ImplicitDefaultVersion1_MatchesWhenExpected1() =>
        DomainVersionAssertions.ShouldHaveVersion<TestUnversionedEvent>(1);

    [Fact]
    public void ShouldBeVersioned_AttributePresent_DoesNotThrow() =>
        DomainVersionAssertions.ShouldBeVersioned<TestVersionedEvent>();

    [Fact]
    public void ShouldBeVersioned_AttributeAbsent_Throws() =>
        Assert.Throws<InvalidOperationException>(DomainVersionAssertions.ShouldBeVersioned<TestUnversionedEvent>);
}
