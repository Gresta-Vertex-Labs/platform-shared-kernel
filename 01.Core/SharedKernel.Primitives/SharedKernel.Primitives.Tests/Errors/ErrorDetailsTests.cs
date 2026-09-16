using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Primitives.Tests.Errors;

public sealed class ErrorDetailsTests
{
    [Fact]
    public void SingleErrorFactories_HaveNoDetails()
    {
        Assert.Empty(Error.Validation("name", "Name is required.").Details);
        Assert.Empty(Error.NotFound("order.not_found", "Order was not found.").Details);
        Assert.Empty(Error.None.Details);
    }

    [Fact]
    public void Validation_WithErrors_CarriesEveryError()
    {
        var name = Error.Validation("name", "Name is required.");
        var email = Error.Validation("email", "Email is invalid.");

        var error = Error.Validation([name, email]);

        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(ErrorCodes.Validation.Failed, error.Code);
        Assert.Equal("2 validation errors occurred.", error.Message);
        Assert.Equal([name, email], error.Details);
    }

    [Fact]
    public void Validation_WithOneError_UsesSingularMessage()
    {
        var error = Error.Validation([Error.Validation("name", "Name is required.")]);

        Assert.Equal("One validation error occurred.", error.Message);
    }

    [Fact]
    public void Validation_SnapshotsTheSuppliedList()
    {
        var errors = new List<Error> { Error.Validation("name", "Name is required.") };

        var error = Error.Validation(errors);
        errors.Clear();

        Assert.Single(error.Details);
    }

    [Fact]
    public void Validation_RejectsNullEmptyOrNullEntry()
    {
        Assert.Throws<ArgumentNullException>(() => Error.Validation((IReadOnlyList<Error>)null!));
        Assert.Throws<ArgumentException>(() => Error.Validation(Array.Empty<Error>()));
        Assert.Throws<ArgumentException>(() => Error.Validation([Error.Validation("a", "b"), null!]));
    }

    [Fact]
    public void Equality_ComparesDetailsByValue()
    {
        var first = Error.Validation([Error.Validation("name", "Name is required.")]);
        var second = Error.Validation([Error.Validation("name", "Name is required.")]);
        var different = Error.Validation([Error.Validation("email", "Name is required.")]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void Equality_ErrorWithDetails_DiffersFromSameErrorWithout()
    {
        var aggregate = Error.Validation([Error.Validation("name", "Name is required.")]);
        var plain = Error.Validation(aggregate.Code, aggregate.Message);

        Assert.NotEqual(aggregate, plain);
    }

    [Fact]
    public void Result_CarriesAggregateError()
    {
        var result = Result<int>.Failure(Error.Validation([Error.Validation("name", "Name is required.")]));

        Assert.True(result.IsFailure);
        Assert.Single(result.Error.Details);
    }
}
