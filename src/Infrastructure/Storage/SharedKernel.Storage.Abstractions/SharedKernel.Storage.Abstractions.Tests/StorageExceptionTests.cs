using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage.Abstractions.Tests;

/// <summary>
/// <see cref="StorageException"/> is a <see cref="SharedKernelException"/>, so the HTTP, gRPC and SignalR edges
/// map it by <see cref="Error.Type"/>. The status assertions belong to 14.Presentation; this pins the inputs.
/// </summary>
public sealed class StorageExceptionTests
{
    [Fact]
    public void It_is_a_sealed_SharedKernelException_that_exposes_its_error()
    {
        Error error = StorageErrors.Unavailable("invoices", "listing");

        var exception = new StorageException(error);

        exception.Should().BeAssignableTo<SharedKernelException>().And.BeAssignableTo<IHasError>();
        typeof(StorageException).IsSealed.Should().BeTrue();
        exception.Error.Should().BeSameAs(error);
        exception.Message.Should().Be(error.Message);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void The_inner_exception_is_kept_by_reference_with_its_stack()
    {
        Error error = StorageErrors.ProviderError("invoices", "listing");
        InvalidOperationException inner;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException caught)
        {
            inner = caught;
        }

        var exception = new StorageException(error, inner);

        exception.InnerException.Should().BeSameAs(inner);
        exception.InnerException!.StackTrace.Should().NotBeNullOrEmpty();
        exception.Message.Should().Be(error.Message);
    }

    [Fact]
    public void A_null_error_throws_ArgumentNullException()
    {
        Func<StorageException> single = () => new StorageException(null!);
        Func<StorageException> withInner = () => new StorageException(null!, new InvalidOperationException());

        single.Should().Throw<ArgumentNullException>();
        withInner.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_thrown_error_code_is_a_StorageErrorCodes_value()
    {
        var exception = new StorageException(StorageErrors.InvalidKey("../x", "it contains an empty, '.' or '..' segment."));

        exception.Error.Code.Should().Be(StorageErrorCodes.InvalidKey).And.Be("storage.invalid_key");
        exception.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Every_code_maps_to_the_pinned_error_type()
    {
        var table = new (Error Error, string Code, ErrorType Type)[]
        {
            (StorageErrors.NotFound("s", "k"), "storage.not_found", ErrorType.NotFound),
            (StorageErrors.AccessDenied("s"), "storage.access_denied", ErrorType.Forbidden),
            (StorageErrors.InvalidKey("k", "it is empty."), "storage.invalid_key", ErrorType.Validation),
            (StorageErrors.InvalidRequest("bad."), "storage.invalid_request", ErrorType.Validation),
            (StorageErrors.ExpiryTooLong(TimeSpan.FromDays(9), TimeSpan.FromDays(7)), "storage.expiry_too_long", ErrorType.Validation),
            (StorageErrors.AlreadyExists("s", "k"), "storage.already_exists", ErrorType.Conflict),
            (StorageErrors.PreconditionFailed("s", "k"), "storage.precondition_failed", ErrorType.Conflict),
            (StorageErrors.ChecksumMismatch("s", "k"), "storage.checksum_mismatch", ErrorType.Validation),
            (StorageErrors.InvalidRange("s", "k"), "storage.invalid_range", ErrorType.Validation),
            (StorageErrors.NotSupported("s", "tags"), "storage.not_supported", ErrorType.Unexpected),
            (StorageErrors.Unavailable("s", "upload"), "storage.unavailable", ErrorType.Unavailable),
            (StorageErrors.ProviderError("s", "copy"), "storage.provider_error", ErrorType.Unexpected),
        };

        foreach ((Error error, string code, ErrorType type) in table)
        {
            error.Code.Should().Be(code);
            error.Type.Should().Be(type, code);
            new StorageException(error).Error.Type.Should().Be(type, code);
        }
    }
}
