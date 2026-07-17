namespace SharedKernel.Storage.Abstractions.Models;

/// <summary>
/// A downloaded object returned by <see cref="Abstractions.IFileStorage.DownloadAsync"/>, wrapping
/// the provider's network stream.
/// </summary>
/// <remarks>
/// <see cref="Content"/> is provider-backed and caller-disposed — <see langword="await"/> <see langword="using"/>
/// the <see cref="FileDownload"/> (or explicitly call <see cref="DisposeAsync"/>) to release the
/// underlying network stream once consumption is complete.
/// </remarks>
public sealed class FileDownload : IAsyncDisposable
{
    /// <summary>The downloaded object's payload, backed by the provider's network stream.</summary>
    public required Stream Content { get; init; }

    /// <summary>The object's MIME type.</summary>
    public required string ContentType { get; init; }

    /// <summary>The object's size in bytes.</summary>
    public required long ContentLength { get; init; }

    /// <summary>User-supplied metadata stored alongside the object.</summary>
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    /// <summary>Releases the underlying provider network stream.</summary>
    public async ValueTask DisposeAsync() => await Content.DisposeAsync().ConfigureAwait(false);
}
