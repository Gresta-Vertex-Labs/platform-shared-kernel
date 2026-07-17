using SharedKernel.Primitives.Errors;

namespace SharedKernel.Storage.Abstractions.Errors;

/// <summary>
/// Canonical <see cref="Error"/> factory for <see cref="Abstractions.IFileStorage"/> and
/// <see cref="Abstractions.IBlobUriGenerator"/>. Provider implementations return these values — they
/// never construct ad-hoc <see cref="Error"/> instances inline.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Primitives.Errors.ErrorType"/> has no dedicated "Forbidden" or "Failure" member, so
/// this factory maps onto the closest existing kind: <see cref="AccessDenied"/> uses
/// <see cref="ErrorType.Unauthorized"/> ("the caller is not authorized to perform the operation"),
/// and the four provider-rejection factories (<see cref="UploadFailed"/>, <see cref="CopyFailed"/>,
/// <see cref="BatchDeleteFailed"/>, <see cref="ConnectivityFailure"/>) use
/// <see cref="ErrorType.Unexpected"/> ("an unexpected or unclassified failure ... external service
/// fault").
/// </para>
/// </remarks>
public static class StorageErrors
{
    private const string NotFoundCode = "storage.not_found";
    private const string AccessDeniedCode = "storage.access_denied";
    private const string InvalidBucketCode = "storage.invalid_bucket";
    private const string InvalidKeyCode = "storage.invalid_key";
    private const string ExpiryTooLongCode = "storage.expiry_too_long";
    private const string UploadFailedCode = "storage.upload_failed";
    private const string CopyFailedCode = "storage.copy_failed";
    private const string BatchDeleteFailedCode = "storage.batch_delete_failed";
    private const string ConnectivityFailureCode = "storage.connectivity_failure";

    /// <summary>The requested object or bucket could not be located.</summary>
    /// <param name="bucket">The bucket that was probed.</param>
    /// <param name="key">The object key that was probed.</param>
    public static Error NotFound(string bucket, string key) =>
        Error.NotFound(NotFoundCode, $"Object '{key}' was not found in bucket '{bucket}'.");

    /// <summary>The caller's credentials do not permit the requested operation.</summary>
    /// <param name="bucket">The bucket the operation targeted.</param>
    /// <param name="key">The object key the operation targeted.</param>
    public static Error AccessDenied(string bucket, string key) =>
        Error.Unauthorized(AccessDeniedCode, $"Access to object '{key}' in bucket '{bucket}' was denied.");

    /// <summary>The supplied bucket name is empty or malformed.</summary>
    /// <param name="bucket">The invalid bucket name.</param>
    public static Error InvalidBucket(string bucket) =>
        Error.Validation(InvalidBucketCode, $"Bucket name '{bucket}' is invalid.");

    /// <summary>The supplied object key is empty or malformed.</summary>
    /// <param name="key">The invalid object key.</param>
    public static Error InvalidKey(string key) =>
        Error.Validation(InvalidKeyCode, $"Object key '{key}' is invalid.");

    /// <summary>The requested presigned-URL expiry exceeds the provider's maximum.</summary>
    /// <param name="requested">The requested expiry.</param>
    /// <param name="max">The provider's maximum permitted expiry.</param>
    public static Error ExpiryTooLong(TimeSpan requested, TimeSpan max) =>
        Error.Validation(
            ExpiryTooLongCode,
            $"Requested presign expiry '{requested}' exceeds the provider maximum of '{max}'.");

    /// <summary>The provider rejected the write.</summary>
    /// <param name="bucket">The bucket the upload targeted.</param>
    /// <param name="key">The object key the upload targeted.</param>
    public static Error UploadFailed(string bucket, string key) =>
        Error.Unexpected(UploadFailedCode, $"Failed to upload object '{key}' to bucket '{bucket}'.");

    /// <summary>The provider rejected the server-side copy.</summary>
    /// <param name="sourceBucket">The bucket the source object is stored in.</param>
    /// <param name="sourceKey">The source object's key.</param>
    /// <param name="destinationBucket">The bucket the copy was written to.</param>
    /// <param name="destinationKey">The destination object's key.</param>
    public static Error CopyFailed(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey) =>
        Error.Unexpected(
            CopyFailedCode,
            $"Failed to copy object '{sourceKey}' from bucket '{sourceBucket}' to '{destinationKey}' in bucket '{destinationBucket}'.");

    /// <summary>
    /// The outer <see cref="Abstractions.IFileStorage.DeleteManyAsync"/> call-level batch failed.
    /// Per-key failures inside the returned <see cref="Models.FileDeleteOutcome"/> list reuse
    /// <see cref="NotFound"/>/<see cref="AccessDenied"/> instead.
    /// </summary>
    /// <param name="bucket">The bucket the batch delete targeted.</param>
    public static Error BatchDeleteFailed(string bucket) =>
        Error.Unexpected(BatchDeleteFailedCode, $"Batch delete failed for bucket '{bucket}'.");

    /// <summary>The <see cref="Abstractions.IFileStorage.CheckHealthAsync"/> connectivity probe failed.</summary>
    /// <param name="bucket">The bucket the probe targeted.</param>
    public static Error ConnectivityFailure(string bucket) =>
        Error.Unexpected(ConnectivityFailureCode, $"Storage connectivity check failed for bucket '{bucket}'.");
}
