using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Conversions;

public sealed class StronglyTypedIdValueConverterTests
{
    [Fact]
    public void Converter_ToProvider_ReturnsUnderlyingValue()
    {
        // Arrange
        var id = new TestId(Guid.NewGuid());
        var converter = new StronglyTypedIdValueConverter<TestId, Guid>();

        // Act — use the converter's ConvertToProvider expression
        var toProvider = converter.ConvertToProvider;
        var result = toProvider!(id);

        // Assert
        result.Should().Be(id.Value);
    }

    [Fact]
    public void Converter_FromProvider_ReturnsStronglyTypedId()
    {
        // Arrange
        var guidValue = Guid.NewGuid();
        var converter = new StronglyTypedIdValueConverter<TestId, Guid>();

        // Act
        var fromProvider = converter.ConvertFromProvider;
        var result = fromProvider!(guidValue);

        // Assert
        result.Should().BeOfType<TestId>();
        ((TestId)result!).Value.Should().Be(guidValue);
    }

    [Fact]
    public async Task RoundTrip_EntityWithStronglyTypedId_PreservesIdValue()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var originalId = TestId.New();
        var aggregate = new TestAggregate(originalId, "ConverterTest", new SystemClock());

        // Act — write
        ctx.TestAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act — read back
        var loaded = await ctx.TestAggregates.FindAsync(originalId);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(originalId);
        loaded.Id.Value.Should().Be(originalId.Value);
    }

    [Fact]
    public async Task RoundTrip_QueryByStronglyTypedId_Works()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var id = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id, "QueryTest", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act — query using the strongly-typed ID
        var found = await ctx.TestAggregates
            .FirstOrDefaultAsync(e => e.Id == id);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(id);
    }
}
