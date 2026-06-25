using Bogus;
using SharedKernel.Testing.Fakers;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Fakers;

/// <summary>
/// Defines a non-parallelized xUnit test collection for <see cref="FakerSeedingTests"/>.
/// </summary>
/// <remarks>
/// <see cref="Bogus.Randomizer.Seed"/> is process-wide static mutable state by design (the documented
/// exception this convention exists to manage). xUnit runs test classes in different collections in
/// parallel by default, so without this collection another class's concurrently running test could
/// reassign <see cref="Bogus.Randomizer.Seed"/> between this test's own Apply/Generate calls and
/// produce a false failure unrelated to <see cref="FakerSeeding"/>'s actual correctness.
/// </remarks>
[CollectionDefinition(nameof(FakerSeedingTestCollection), DisableParallelization = true)]
public sealed class FakerSeedingTestCollection;

/// <summary>
/// Proves <see cref="FakerSeeding.Apply"/> produces deterministic, reproducible <see cref="Faker{T}"/>
/// output — a standalone determinism convention with no owning consuming-domain interface, proven
/// unconditionally in <c>SharedKernel.Testing.SelfTests</c> per D-54.
/// </summary>
[Collection(nameof(FakerSeedingTestCollection))]
public sealed class FakerSeedingTests
{
    private sealed class Person
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public int Age { get; set; }
    }

    private sealed class PersonFaker : Faker<Person>
    {
        public PersonFaker()
        {
            RuleFor(p => p.FullName, f => f.Person.FullName);
            RuleFor(p => p.Email, f => f.Person.Email);
            RuleFor(p => p.Age, f => f.Random.Int(18, 90));
        }
    }

    [Fact]
    public void Apply_SameSeed_ProducesIdenticalOutputAcrossIndependentCalls()
    {
        FakerSeeding.Apply(42);
        var firstRun = new PersonFaker().Generate(5);

        FakerSeeding.Apply(42);
        var secondRun = new PersonFaker().Generate(5);

        for (var i = 0; i < firstRun.Count; i++)
        {
            Assert.Equal(firstRun[i].FullName, secondRun[i].FullName);
            Assert.Equal(firstRun[i].Email, secondRun[i].Email);
            Assert.Equal(firstRun[i].Age, secondRun[i].Age);
        }
    }

    [Fact]
    public void Apply_DefaultSeed_IsFixedConstant()
    {
        FakerSeeding.Apply();
        var firstRun = new PersonFaker().Generate(3);

        FakerSeeding.Apply();
        var secondRun = new PersonFaker().Generate(3);

        for (var i = 0; i < firstRun.Count; i++)
        {
            Assert.Equal(firstRun[i].FullName, secondRun[i].FullName);
        }
    }

    [Fact]
    public void Apply_DifferentSeeds_ProduceDifferentOutput()
    {
        FakerSeeding.Apply(1);
        var firstRun = new PersonFaker().Generate(10);

        FakerSeeding.Apply(2);
        var secondRun = new PersonFaker().Generate(10);

        Assert.NotEqual(
            string.Join(",", firstRun.Select(p => p.FullName)),
            string.Join(",", secondRun.Select(p => p.FullName)));
    }

    [Fact]
    public void Apply_SetsRandomizerSeed()
    {
        FakerSeeding.Apply(123);

        // Calling Apply must not throw and must actually reassign Randomizer.Seed —
        // verified indirectly via reproducibility above; this check guards against a no-op stub.
        FakerSeeding.Apply(123);
        var run1 = new PersonFaker().Generate();

        FakerSeeding.Apply(999);
        var run2 = new PersonFaker().Generate();

        FakerSeeding.Apply(123);
        var run3 = new PersonFaker().Generate();

        Assert.NotEqual(run1.FullName, run2.FullName);
        Assert.Equal(run1.FullName, run3.FullName);
    }
}
