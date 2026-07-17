using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Storage.Abstractions.Errors;

namespace SharedKernel.Storage.Abstractions.Tests.Errors;

/// <summary>
/// T-01: <see cref="StorageErrors"/> factory tests — all nine members return the correct
/// <see cref="ErrorType"/> and a stable, non-empty <see cref="Error.Code"/>.
/// </summary>
/// <remarks>
/// <see cref="ErrorType"/> (01.Core/SharedKernel.Primitives) has no dedicated "Forbidden" or
/// "Failure" member — verified directly against <c>ErrorType.cs</c> and <c>StorageErrors.cs</c>
/// on disk. <see cref="StorageErrors.AccessDenied"/> maps onto <see cref="ErrorType.Unauthorized"/>,
/// and the four provider-rejection factories (<see cref="StorageErrors.UploadFailed"/>,
/// <see cref="StorageErrors.CopyFailed"/>, <see cref="StorageErrors.BatchDeleteFailed"/>,
/// <see cref="StorageErrors.ConnectivityFailure"/>) all map onto <see cref="ErrorType.Unexpected"/>.
/// </remarks>
public sealed class StorageErrorsTests
{
    [Fact]
    public void NotFound_ReturnsNotFoundErrorType_WithBucketAndKeyInMessage()
    {
        var error = StorageErrors.NotFound("my-bucket", "my-key");

        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("storage.not_found");
        error.Message.Should().Contain("my-bucket").And.Contain("my-key");
    }

    [Fact]
    public void AccessDenied_ReturnsUnauthorizedErrorType_WithBucketAndKeyInMessage()
    {
        var error = StorageErrors.AccessDenied("my-bucket", "my-key");

        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Code.Should().Be("storage.access_denied");
        error.Message.Should().Contain("my-bucket").And.Contain("my-key");
    }

    [Fact]
    public void InvalidBucket_ReturnsValidationErrorType()
    {
        var error = StorageErrors.InvalidBucket(string.Empty);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("storage.invalid_bucket");
    }

    [Fact]
    public void InvalidKey_ReturnsValidationErrorType()
    {
        var error = StorageErrors.InvalidKey(string.Empty);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("storage.invalid_key");
    }

    [Fact]
    public void ExpiryTooLong_ReturnsValidationErrorType_WithRequestedAndMaxInMessage()
    {
        var requested = TimeSpan.FromDays(10);
        var max = TimeSpan.FromDays(7);

        var error = StorageErrors.ExpiryTooLong(requested, max);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("storage.expiry_too_long");
        error.Message.Should().Contain(requested.ToString()).And.Contain(max.ToString());
    }

    [Fact]
    public void UploadFailed_ReturnsUnexpectedErrorType()
    {
        var error = StorageErrors.UploadFailed("my-bucket", "my-key");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("storage.upload_failed");
        error.Message.Should().Contain("my-bucket").And.Contain("my-key");
    }

    [Fact]
    public void CopyFailed_ReturnsUnexpectedErrorType_WithAllFourLocationsInMessage()
    {
        var error = StorageErrors.CopyFailed("src-bucket", "src-key", "dst-bucket", "dst-key");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("storage.copy_failed");
        error.Message.Should()
            .Contain("src-bucket").And.Contain("src-key")
            .And.Contain("dst-bucket").And.Contain("dst-key");
    }

    [Fact]
    public void BatchDeleteFailed_ReturnsUnexpectedErrorType()
    {
        var error = StorageErrors.BatchDeleteFailed("my-bucket");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("storage.batch_delete_failed");
        error.Message.Should().Contain("my-bucket");
    }

    [Fact]
    public void ConnectivityFailure_ReturnsUnexpectedErrorType()
    {
        var error = StorageErrors.ConnectivityFailure("my-bucket");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("storage.connectivity_failure");
        error.Message.Should().Contain("my-bucket");
    }

    [Fact]
    public void AllNineFactories_ProduceCodes_ThatArePairwiseUnique_AndPrefixedWithStorage()
    {
        Error[] errors =
        [
            StorageErrors.NotFound("b", "k"),
            StorageErrors.AccessDenied("b", "k"),
            StorageErrors.InvalidBucket("b"),
            StorageErrors.InvalidKey("k"),
            StorageErrors.ExpiryTooLong(TimeSpan.FromDays(8), TimeSpan.FromDays(7)),
            StorageErrors.UploadFailed("b", "k"),
            StorageErrors.CopyFailed("b", "k", "b2", "k2"),
            StorageErrors.BatchDeleteFailed("b"),
            StorageErrors.ConnectivityFailure("b"),
        ];

        errors.Should().HaveCount(9);
        errors.Select(e => e.Code).Should().OnlyHaveUniqueItems();
        errors.Select(e => e.Code).Should().OnlyContain(code => code.StartsWith("storage.", StringComparison.Ordinal));
    }
}
