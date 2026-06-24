using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Fakers;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Fakers;

public sealed class SingleValueObjectFakerTests
{
    private sealed class TestEmail : SingleValueObject<string>
    {
        public TestEmail(string value) : base(value) { }

        protected override IEnumerable<Error>? Validate() => null;
    }

    private sealed class TestEmailFaker : SingleValueObjectFaker<TestEmail, string>;

    [Fact]
    public void WithValue_EveryGeneratedInstance_WrapsFixedValue()
    {
        var faker = new TestEmailFaker().WithValue("fixed@example.com");

        var generated = faker.Generate(3);

        Assert.All(generated, e => Assert.Equal("fixed@example.com", e.Value));
    }

    [Fact]
    public void WithRandomValue_UsesGeneratorDelegate()
    {
        var faker = new TestEmailFaker().WithRandomValue(f => f.Internet.Email());

        var generated = faker.Generate();

        Assert.Contains('@', generated.Value);
    }

    [Fact]
    public void WithValue_NullValue_Throws()
    {
        var faker = new TestEmailFaker();

        Assert.Throws<ArgumentNullException>(() => faker.WithValue(null!));
    }

    [Fact]
    public void WithRandomValue_NullGenerator_Throws()
    {
        var faker = new TestEmailFaker();

        Assert.Throws<ArgumentNullException>(() => faker.WithRandomValue(null!));
    }

    [Fact]
    public void WithValue_ReturnsSameFakerInstance_ForFluentChaining()
    {
        var faker = new TestEmailFaker();
        var result = faker.WithValue("a@b.com");

        Assert.Same(faker, result);
    }
}
