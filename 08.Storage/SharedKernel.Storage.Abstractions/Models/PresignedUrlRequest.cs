namespace SharedKernel.Storage.Abstractions.Models;

/// <summary>
/// Describes a request for a presigned URL via <see cref="Abstractions.IBlobUriGenerator"/>.
/// </summary>
/// <remarks>The HTTP verb is implied by which generator method is called (upload = PUT, download = GET).</remarks>
public sealed record PresignedUrlRequest
{
    /// <summary>The bucket the presigned URL targets. Required, non-empty.</summary>
    public required string Bucket { get; init; }

    /// <summary>The object key the presigned URL targets. Required, non-empty.</summary>
    public required string Key { get; init; }

    /// <summary>The time-to-live of the presigned URL, measured from now.</summary>
    public required TimeSpan Expiry { get; init; }
}
