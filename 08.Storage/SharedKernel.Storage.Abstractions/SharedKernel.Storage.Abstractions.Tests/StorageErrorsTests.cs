using FluentAssertions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Storage.Abstractions.Tests;

/// <summary>
/// <see cref="StorageErrors"/> pairs every <see cref="StorageErrorCodes"/> code with the
/// <see cref="ErrorType"/> its documentation states, which decides the HTTP status at the boundary.
/// </summary>
public sealed class StorageErrorsTests
{
    [Fact]
    public void Unavailable_is_an_Unavailable_error_not_Unexpected()
    {
        // P-562: an unreachable or throttling provider is retryable, so it must reach the HTTP
        // boundary as 503 rather than 500. The code string is a wire contract and is unchanged.
        Error error = StorageErrors.Unavailable("invoices", "upload");

        error.Type.Should().Be(ErrorType.Unavailable);
        error.Code.Should().Be(StorageErrorCodes.Unavailable).And.Be("storage.unavailable");
        error.Message.Should().Be("Store 'invoices' is unavailable; the upload can be retried later.");
    }

    [Fact]
    public void Every_factory_returns_the_documented_error_type()
    {
        var expected = new (Error Error, ErrorType Type)[]
        {
            (StorageErrors.NotFound("s", "k"), ErrorType.NotFound),
            (StorageErrors.AccessDenied("s"), ErrorType.Forbidden),
            (StorageErrors.InvalidKey("k", "it is empty."), ErrorType.Validation),
            (StorageErrors.InvalidTenant("t"), ErrorType.Validation),
            (StorageErrors.InvalidRequest("bad."), ErrorType.Validation),
            (StorageErrors.ExpiryTooLong(TimeSpan.FromDays(9), TimeSpan.FromDays(7)), ErrorType.Validation),
            (StorageErrors.AlreadyExists("s", "k"), ErrorType.Conflict),
            (StorageErrors.PreconditionFailed("s", "k"), ErrorType.Conflict),
            (StorageErrors.ChecksumMismatch("s", "k"), ErrorType.Validation),
            (StorageErrors.InvalidRange("s", "k"), ErrorType.Validation),
            (StorageErrors.NotSupported("s", "conditional writes"), ErrorType.Unexpected),
            (StorageErrors.Unavailable("s", "upload"), ErrorType.Unavailable),
            (StorageErrors.ProviderError("s", "copy"), ErrorType.Unexpected),
        };

        foreach ((Error error, ErrorType type) in expected)
        {
            error.Type.Should().Be(type, $"{error.Code} is documented as {type}");
        }

        expected.Select(e => e.Error.Code).Should().OnlyHaveUniqueItems()
            .And.OnlyContain(code => code.StartsWith("storage.", StringComparison.Ordinal));
    }
}
