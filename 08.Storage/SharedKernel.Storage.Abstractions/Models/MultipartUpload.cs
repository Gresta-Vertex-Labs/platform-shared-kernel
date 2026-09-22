namespace SharedKernel.Storage;

/// <summary>
/// A multipart upload in progress, returned by <see cref="IFileStorage.StartMultipartUploadAsync"/>. Pass it
/// unchanged to <see cref="IFileStorage.CreateUploadPartUrlAsync"/>,
/// <see cref="IFileStorage.CompleteMultipartUploadAsync"/> and <see cref="IFileStorage.AbortMultipartUploadAsync"/> on
/// the same store (and tenant view).
/// </summary>
/// <remarks>
/// Both values are needed to finish the upload later; persist them if the client uploads across requests. The
/// upload id is not a secret on its own — every part URL is still signed — but only hand it to the client that
/// owns the upload.
/// </remarks>
/// <param name="Key">The object key the upload will create, relative to the store and tenant.</param>
/// <param name="UploadId">The provider's upload id; not empty.</param>
public sealed record MultipartUpload(string Key, string UploadId);

/// <summary>One uploaded part of a multipart upload, as reported by the client that uploaded it.</summary>
/// <param name="PartNumber">The part number, 1 to <see cref="StorageValidation.MaxPartNumber"/>.</param>
/// <param name="ETag">
/// The <c>ETag</c> response header returned when the part was uploaded, with or without quotes; not empty.
/// </param>
public sealed record UploadedPart(int PartNumber, string ETag);
