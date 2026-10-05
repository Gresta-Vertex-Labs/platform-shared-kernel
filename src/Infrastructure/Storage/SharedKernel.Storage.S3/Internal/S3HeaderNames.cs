namespace SharedKernel.Storage.S3.Internal;

/// <summary>HTTP header and form field names of the S3 wire protocol that clients of presigned requests must send.</summary>
internal static class S3HeaderNames
{
    public const string ContentType = "Content-Type";
    public const string IfNoneMatch = "If-None-Match";
    public const string ChecksumSha256 = "x-amz-checksum-sha256";
    public const string ServerSideEncryption = "x-amz-server-side-encryption";
    public const string KmsKeyId = "x-amz-server-side-encryption-aws-kms-key-id";
    public const string StorageClass = "x-amz-storage-class";
    public const string MetadataPrefix = "x-amz-meta-";
    public const string AnyETag = "*";
}

/// <summary>S3 error codes the provider maps to specific storage errors.</summary>
internal static class S3ErrorCodes
{
    public const string NoSuchBucket = "NoSuchBucket";
    public const string NoSuchKey = "NoSuchKey";
    public const string NoSuchUpload = "NoSuchUpload";
    public const string ConditionalRequestConflict = "ConditionalRequestConflict";
    public const string BadDigest = "BadDigest";
    public const string InvalidDigest = "InvalidDigest";
    public const string ChecksumMismatch = "XAmzContentChecksumMismatch";
    public const string Sha256Mismatch = "XAmzContentSHA256Mismatch";
    public const string SlowDown = "SlowDown";
    public const string RequestTimeout = "RequestTimeout";
    public const string InvalidPart = "InvalidPart";
    public const string InvalidPartOrder = "InvalidPartOrder";
    public const string EntityTooSmall = "EntityTooSmall";
    public const string AccessDenied = "AccessDenied";
    public const string PermanentRedirect = "PermanentRedirect";
    public const string AuthorizationHeaderMalformed = "AuthorizationHeaderMalformed";
}
