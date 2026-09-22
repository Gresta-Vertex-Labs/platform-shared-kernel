namespace SharedKernel.Storage;

/// <summary>
/// An inclusive byte range of an object, as in an HTTP <c>Range: bytes=From-To</c> header. Pass it as
/// <see cref="FileDownloadOptions.Range"/> to read part of an object.
/// </summary>
/// <remarks>
/// A range whose <see cref="To"/> is past the end of the object is shortened to the object by the provider; one
/// whose <see cref="From"/> is at or past the end fails with <see cref="StorageErrorCodes.InvalidRange"/>.
/// <c>default(ByteRange)</c> is <c>bytes=0-</c>, the whole object. Suffix ranges (the last N bytes) are not
/// expressible; read <see cref="FileProperties.ContentLength"/> first.
/// </remarks>
public readonly record struct ByteRange
{
    /// <summary>Initializes a new instance of the <see cref="ByteRange"/> struct.</summary>
    /// <param name="from">The first byte, zero-based; not negative.</param>
    /// <param name="to">
    /// The last byte, inclusive; at least <paramref name="from"/>. <see langword="null"/> reads to the end of the
    /// object.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="from"/> is negative, or <paramref name="to"/> is less than <paramref name="from"/>.
    /// </exception>
    public ByteRange(long from, long? to = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        if (to is { } last)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(last, from, nameof(to));
        }

        From = from;
        To = to;
    }

    /// <summary>Gets the first byte, zero-based.</summary>
    public long From { get; }

    /// <summary>Gets the last byte, inclusive, or <see langword="null"/> for the end of the object.</summary>
    public long? To { get; }

    /// <summary>Returns the range of the first <paramref name="length"/> bytes.</summary>
    /// <param name="length">The number of bytes; at least 1.</param>
    /// <returns>The range <c>0</c> to <c>length - 1</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is less than 1.</exception>
    public static ByteRange FirstBytes(long length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        return new ByteRange(0, length - 1);
    }

    /// <summary>Formats the range as an HTTP <c>Range</c> header value.</summary>
    /// <returns><c>bytes=From-To</c>, or <c>bytes=From-</c> when open-ended.</returns>
    public override string ToString() => To is { } to ? $"bytes={From}-{to}" : $"bytes={From}-";
}
