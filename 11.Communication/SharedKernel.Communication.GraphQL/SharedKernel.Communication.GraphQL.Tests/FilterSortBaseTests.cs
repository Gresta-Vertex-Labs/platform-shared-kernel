using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using SharedKernel.Communication.GraphQL.Types;

namespace SharedKernel.Communication.GraphQL.Tests;

/// <summary>
/// Tests that <see cref="FilterBase{T}"/> and <see cref="SortBase{T}"/> are correct
/// abstract base classes that consuming services can extend.
/// </summary>
public sealed class FilterSortBaseTests
{
    private sealed class TestEntity
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    private sealed class TestEntityFilterType : FilterBase<TestEntity>
    {
        protected override void Configure(IFilterInputTypeDescriptor<TestEntity> descriptor)
        {
            descriptor.Field(e => e.Name);
            descriptor.Field(e => e.Age);
        }
    }

    private sealed class TestEntitySortType : SortBase<TestEntity>
    {
        protected override void Configure(ISortInputTypeDescriptor<TestEntity> descriptor)
        {
            descriptor.Field(e => e.Name);
            descriptor.Field(e => e.Age);
        }
    }

    [Fact]
    public void FilterBase_IsAbstract()
    {
        typeof(FilterBase<TestEntity>).IsAbstract.Should().BeTrue(
            "FilterBase<T> must be abstract — direct instantiation is forbidden");
    }

    [Fact]
    public void SortBase_IsAbstract()
    {
        typeof(SortBase<TestEntity>).IsAbstract.Should().BeTrue(
            "SortBase<T> must be abstract — direct instantiation is forbidden");
    }

    [Fact]
    public void FilterBase_ExtendsFilterInputTypeT()
    {
        typeof(FilterBase<TestEntity>).BaseType
            .Should().NotBeNull()
            .And.Subject.Should().Be(typeof(FilterInputType<TestEntity>),
                "FilterBase<T> must extend FilterInputType<T>");
    }

    [Fact]
    public void SortBase_ExtendsSortInputTypeT()
    {
        typeof(SortBase<TestEntity>).BaseType
            .Should().NotBeNull()
            .And.Subject.Should().Be(typeof(SortInputType<TestEntity>),
                "SortBase<T> must extend SortInputType<T>");
    }

    [Fact]
    public void ConcreteFilterType_CanBeInstantiated()
    {
        // Concrete subclasses can be instantiated (they are valid HotChocolate input types).
        var act = () => new TestEntityFilterType();
        act.Should().NotThrow();
    }

    [Fact]
    public void ConcreteSortType_CanBeInstantiated()
    {
        var act = () => new TestEntitySortType();
        act.Should().NotThrow();
    }
}
