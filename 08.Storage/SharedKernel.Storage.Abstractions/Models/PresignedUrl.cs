namespace SharedKernel.Storage.Abstractions.Models;

/// <summary>
/// A presigned URL returned by <see cref="Abstractions.IBlobUriGenerator"/>, allowing a client to
/// upload or download an object directly against storage without routing bytes through the service.
/// </summary>
public sealed record PresignedUrl
{
    /// <summary>The presigned URL.</summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// The absolute point in time this URL stops being valid. Derived once at generation time —
    /// never recompute this from the originating request's relative <see cref="TimeSpan"/>.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
