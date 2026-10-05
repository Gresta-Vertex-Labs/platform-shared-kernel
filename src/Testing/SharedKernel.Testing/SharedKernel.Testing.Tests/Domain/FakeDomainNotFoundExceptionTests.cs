using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Domain;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Domain;

public sealed class FakeDomainNotFoundExceptionTests
{
    [Fact]
    public void For_ProducesException_WithCorrectAggregateTypeAndId()
    {
        var id = Guid.NewGuid();

        var exception = FakeDomainNotFoundException.For<TestAggregate>(id);

        Assert.Equal(typeof(TestAggregate), exception.AggregateType);
        Assert.Equal(id, exception.AggregateId);
    }

    [Fact]
    public void For_ErrorType_IsNotFound()
    {
        var exception = FakeDomainNotFoundException.For<TestAggregate>(Guid.NewGuid());

        Assert.Equal(ErrorType.NotFound, exception.Error.Type);
    }

    [Fact]
    public void For_MessageFormat_IncludesTypeNameAndId()
    {
        var id = Guid.NewGuid();
        var exception = FakeDomainNotFoundException.For<TestAggregate>(id);

        Assert.Contains(nameof(TestAggregate), exception.Message);
        Assert.Contains(id.ToString(), exception.Message);
    }
}
