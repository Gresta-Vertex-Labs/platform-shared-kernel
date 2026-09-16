using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Primitives.Tests.Errors;

/// <summary>
/// Proves <see cref="Error"/> round-trips through <see cref="JsonSerializer"/> using the default
/// reflection-based serializer — no <c>JsonSerializerContext</c>, no custom converter — since a
/// process boundary (an HTTP <c>ProblemDetails</c> response, an idempotency-replay payload) is
/// exactly how this type travels today.
/// </summary>
public sealed class ErrorJsonTests
{
    [Fact]
    public void PlainError_RoundTrips()
    {
        var error = Error.NotFound("order.not_found", "Order was not found.");

        var json = JsonSerializer.Serialize(error);
        var roundTripped = JsonSerializer.Deserialize<Error>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(error, roundTripped);
        Assert.Equal(error.Code, roundTripped.Code);
        Assert.Equal(error.Message, roundTripped.Message);
        Assert.Equal(error.Type, roundTripped.Type);
        Assert.Empty(roundTripped.Details);
    }

    [Fact]
    public void AggregateValidationError_WithSeveralDetails_RoundTrips()
    {
        var error = Error.Validation(
            [
                Error.Validation("name", "Name is required."),
                Error.Validation("email", "Email is invalid."),
                Error.Validation("age", "Age must be positive."),
            ]
        );

        var json = JsonSerializer.Serialize(error);
        var roundTripped = JsonSerializer.Deserialize<Error>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(error, roundTripped);
        Assert.Equal(3, roundTripped.Details.Count);
        Assert.Equal(error.Details, roundTripped.Details);
    }

    [Fact]
    public void NestedDetails_RoundTrip()
    {
        // A Details entry that itself carries Details — proves the round trip is recursive, not
        // just one level deep.
        var innerAggregate = Error.Validation([Error.Validation("name", "Name is required.")]);
        var outer = Error.Validation([innerAggregate, Error.NotFound("order.not_found", "Not found.")]);

        var json = JsonSerializer.Serialize(outer);
        var roundTripped = JsonSerializer.Deserialize<Error>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(outer, roundTripped);
        Assert.Equal(2, roundTripped.Details.Count);
        Assert.Single(roundTripped.Details[0].Details);
        Assert.Equal(innerAggregate.Details[0], roundTripped.Details[0].Details[0]);
    }

    [Fact]
    public void RoundTrippedErrors_CompareEqualToTheOriginal_AndToEachOther()
    {
        var original = Error.Validation(
            [Error.Validation("name", "Name is required."), Error.Validation("email", "Email is invalid.")]
        );

        var json = JsonSerializer.Serialize(original);
        var first = JsonSerializer.Deserialize<Error>(json);
        var second = JsonSerializer.Deserialize<Error>(json);

        Assert.Equal(original, first);
        Assert.Equal(original, second);
        Assert.Equal(first, second);
        Assert.Equal(original.GetHashCode(), first!.GetHashCode());
    }

    [Fact]
    public void AbsentDetailsMember_DeserializesToEmptyList_NeverNull()
    {
        var json = """{"Code":"order.not_found","Message":"Order was not found.","Type":3}""";

        var error = JsonSerializer.Deserialize<Error>(json);

        Assert.NotNull(error);
        Assert.NotNull(error.Details);
        Assert.Empty(error.Details);
    }

    [Fact]
    public void NullDetailsMember_DeserializesToEmptyList_NeverNull()
    {
        var json = """{"Code":"order.not_found","Message":"Order was not found.","Type":3,"Details":null}""";

        var error = JsonSerializer.Deserialize<Error>(json);

        Assert.NotNull(error);
        Assert.NotNull(error.Details);
        Assert.Empty(error.Details);
    }

    [Fact]
    public void EmptyDetailsArray_DeserializesToEmptyList()
    {
        var json = """{"Code":"order.not_found","Message":"Order was not found.","Type":3,"Details":[]}""";

        var error = JsonSerializer.Deserialize<Error>(json);

        Assert.NotNull(error);
        Assert.Empty(error.Details);
    }

    [Fact]
    public void ErrorNone_RoundTrips()
    {
        var json = JsonSerializer.Serialize(Error.None);
        var roundTripped = JsonSerializer.Deserialize<Error>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(Error.None, roundTripped);
        Assert.Equal(ErrorType.None, roundTripped.Type);
        Assert.Empty(roundTripped.Details);
    }

    [Fact]
    public void ErrorType_SerializesAsUnderlyingIntegerByDefault()
    {
        // No global JsonStringEnumConverter is registered anywhere in this package — confirms the
        // default STJ enum behavior (numeric) is what a caller gets unless they opt into string
        // enums themselves via JsonSerializerOptions.
        var error = Error.Forbidden(ErrorCodes.Forbidden.Default, "Not permitted.");

        var json = JsonSerializer.Serialize(error);

        Assert.Contains("\"Type\":7", json);

        var roundTripped = JsonSerializer.Deserialize<Error>(json);
        Assert.Equal(ErrorType.Forbidden, roundTripped!.Type);
    }

    [Fact]
    public void ErrorType_RoundTripsAsStringWhenCallerOptsIntoJsonStringEnumConverter()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var error = Error.Conflict(ErrorCodes.Conflict.Default, "Duplicate.");

        var json = JsonSerializer.Serialize(error, options);
        Assert.Contains("\"Conflict\"", json);

        var roundTripped = JsonSerializer.Deserialize<Error>(json, options);
        Assert.Equal(error, roundTripped);
    }
}
