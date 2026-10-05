namespace SharedKernel.Storage;

/// <summary>
/// An object opened for reading by <see cref="IFileStorage.DownloadAsync"/>: its content stream and properties.
/// Dispose it to release the connection.
/// </summary>
/// <remarks>
/// <see cref="Content"/> reads straight from the provider's response; nothing is buffered, and it is usually not
/// seekable. Always use <c>await using</c>: an undisposed download keeps a pooled HTTP connection busy. Read the
/// stream once; a failure while reading (for example a dropped connection) throws from the stream.
/// </remarks>
public sealed class FileDownload : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileDownload"/> class. Used by providers and test doubles.
    /// </summary>
    /// <param name="content">The response stream; owned and disposed by this instance.</param>
    /// <param name="properties">The properties of the whole object.</param>
    /// <param name="length">The number of bytes <paramref name="content"/> will yield; not negative.</param>
    /// <param name="range">The byte range returned, or <see langword="null"/> for the whole object.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/> or <paramref name="properties"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    public FileDownload(Stream content, FileProperties properties, long length, ByteRange? range = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Content = content;
        Properties = properties;
        Length = length;
        Range = range;
    }

    /// <summary>Gets the content stream, positioned at the first byte of the object or range.</summary>
    public Stream Content { get; }

    /// <summary>
    /// Gets the properties of the whole object, even for a range read: <see cref="FileProperties.ContentLength"/>
    /// is the full object size and <see cref="FileProperties.ETag"/> identifies the content read.
    /// </summary>
    public FileProperties Properties { get; }

    /// <summary>
    /// Gets the number of bytes <see cref="Content"/> yields: the object size, or the length of the range returned.
    /// </summary>
    public long Length { get; }

    /// <summary>
    /// Gets the inclusive byte range returned — which may end earlier than requested when the request ran past the
    /// end of the object — or <see langword="null"/> when the whole object was requested.
    /// </summary>
    public ByteRange? Range { get; }

    /// <summary>Disposes <see cref="Content"/>, releasing the connection.</summary>
    /// <returns>A task that completes when the stream is disposed.</returns>
    public ValueTask DisposeAsync() => Content.DisposeAsync();

    /// <summary>Disposes <see cref="Content"/>, releasing the connection.</summary>
    public void Dispose() => Content.Dispose();
}
