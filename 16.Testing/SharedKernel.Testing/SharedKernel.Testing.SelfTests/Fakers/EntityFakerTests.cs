using SharedKernel.Domain.Entities;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Fakers;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Fakers;

public sealed class EntityFakerTests
{
    private sealed class TestId(Guid value)
    {
        public Guid Value { get; } = value;
    }

    private sealed class TestEntity : Entity<Guid>
    {
        public TestEntity(Guid id, string name)
            : base(id) => Name = name;

        public string Name { get; }
    }

    private sealed class TestEntityFaker : EntityFaker<TestEntity, Guid>
    {
        public TestEntityFaker()
        {
            CustomInstantiator(f => new TestEntity(f.Random.Guid(), f.Person.FullName));
        }
    }

    [Fact]
    public void WithClock_SetsClockProperty_AccessibleToSubclass()
    {
        var clock = new FakeClock();
        var faker = new ClockExposingFaker().WithClock(clock);

        Assert.Same(clock, ((ClockExposingFaker)faker).ExposedClock);
    }

    [Fact]
    public void WithClock_NullClock_Throws()
    {
        var faker = new TestEntityFaker();

        Assert.Throws<ArgumentNullException>(() => faker.WithClock(null!));
    }

    [Fact]
    public void WithClock_ReturnsSameFakerInstance_ForFluentChaining()
    {
        var faker = new TestEntityFaker();
        var result = faker.WithClock(new FakeClock());

        Assert.Same(faker, result);
    }

    [Fact]
    public void Generate_ProducesConcreteEntity_ViaCustomInstantiator()
    {
        var faker = new TestEntityFaker();
        var entity = faker.Generate();

        Assert.NotNull(entity);
        Assert.False(entity.Id == Guid.Empty);
        Assert.False(string.IsNullOrWhiteSpace(entity.Name));
    }

    private sealed class ClockExposingFaker : EntityFaker<TestEntity, Guid>
    {
        public ClockExposingFaker() => CustomInstantiator(f => new TestEntity(f.Random.Guid(), f.Person.FullName));

        public IClock? ExposedClock => Clock;
    }
}
