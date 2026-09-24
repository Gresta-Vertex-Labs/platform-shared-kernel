using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// The version of an aggregate for optimistic concurrency over HTTP (<c>ETag</c> / <c>If-Match</c>): an opaque,
/// sealed token.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Round trip:</strong> <c>response.Headers.ETag = $"\"{version}\""</c>; on the next request
/// <c>EntityVersion.TryParse(request.Headers.IfMatch, out var expected)</c> (surrounding quotes and a weak
/// <c>W/</c> prefix are accepted) and pass <c>expected</c> to <c>IRepository.UpdateAsync(aggregate, expected)</c>
/// or <c>DeleteAsync(aggregate, expected)</c>.
/// </para>
/// <para>
/// <strong>Opaque by construction.</strong> A version holds only the token its persistence provider sealed, never a
/// database value. <c>SharedKernel.Persistence.EfCore</c> seals PostgreSQL's <c>xmin</c> together with the identity
/// of its aggregate under a key derived from the service's own key, so the token reveals nothing about the database
/// (in particular not how many transactions it committed) to anyone without that key. <see cref="ToString()"/>,
/// string interpolation, <see cref="TryFormat"/> and JSON can therefore only ever produce the token; there is no way
/// to put a raw row version on the wire through this type.
/// </para>
/// <para>
/// <strong>Parsing checks the shape only</strong> — 28 characters of unpadded Base64Url in the version-1 format. A
/// plain number is not a version. Whether a well-formed token is a version of <em>the aggregate being changed</em> is
/// decided when it is used (<c>UpdateAsync</c>/<c>DeleteAsync</c>): a token of another aggregate, an altered token or
/// one sealed with a key the service does not know is treated as a stale version — a <c>ConflictException</c>
/// (<c>persistence.concurrency_conflict</c>), answered with 412 on an endpoint that requires <c>If-Match</c>.
/// </para>
/// <para>
/// <strong>Deterministic:</strong> the same version of the same aggregate always yields the same token while the
/// service's key is unchanged, so <c>If-None-Match</c> works. A key rotation changes every token once.
/// </para>
/// <para>
/// <see cref="None"/> is the version of an aggregate that was never saved. It has no text form
/// (<see cref="ToString()"/> returns an empty string, JSON <c>null</c>), and no text parses to it.
/// </para>
/// </remarks>
[JsonConverter(typeof(EntityVersionJsonConverter))]
public readonly struct EntityVersion : IEquatable<EntityVersion>, ISpanFormattable, IParsable<EntityVersion>
{
    /// <summary>The length of a version's text: 21 bytes as unpadded Base64Url.</summary>
    private const int TextLength = 28;

    /// <summary>The length of a sealed token: the format byte and the provider's 20 bytes.</summary>
    private const int TokenLength = 21;

    /// <summary>The first byte of every version-1 token.</summary>
    private const byte FormatV1 = 0x01;

    // The 21 token bytes, kept as values so the struct stays a plain, allocation-free value:
    // [format][head: 4 bytes][body: 16 bytes]. A default instance (format 0) is None.
    private readonly byte _format;
    private readonly uint _head;
    private readonly ulong _body0;
    private readonly ulong _body1;

    private EntityVersion(ReadOnlySpan<byte> token)
    {
        _format = token[0];
        _head = BinaryPrimitives.ReadUInt32BigEndian(token[1..]);
        _body0 = BinaryPrimitives.ReadUInt64BigEndian(token[5..]);
        _body1 = BinaryPrimitives.ReadUInt64BigEndian(token[13..]);
    }

    /// <summary>Gets the version of an aggregate that was never saved. It has no text form.</summary>
    public static EntityVersion None => default;

    /// <summary>Parses the text form (<see cref="ToString()"/>), optionally quoted and with a weak <c>W/</c> prefix.</summary>
    /// <param name="value">The text, such as an <c>If-Match</c> header value.</param>
    /// <param name="version">The parsed version.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="value"/> has the shape of a version. A plain number never does.
    /// </returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out EntityVersion version) =>
        TryParse(value, provider: null, out version);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out EntityVersion result)
    {
        result = default;
        if (s is null)
            return false;

        var text = s.AsSpan().Trim();
        if (text.StartsWith("W/", StringComparison.Ordinal))
            text = text[2..];

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1];

        // Exactly 28 Base64Url characters: 21 bytes encode without padding and without spare bits, so every token has
        // exactly one text form. IsValid is checked first because TryDecodeFromChars throws on a foreign character.
        if (text.Length != TextLength || !Base64Url.IsValid(text, out var decodedLength) || decodedLength != TokenLength)
            return false;

        Span<byte> token = stackalloc byte[TokenLength];
        if (!Base64Url.TryDecodeFromChars(text, token, out var written) || written != TokenLength || token[0] != FormatV1)
            return false;

        result = new EntityVersion(token);
        return true;
    }

    /// <inheritdoc />
    public static EntityVersion Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var version)
            ? version
            : throw new FormatException("The value is not an entity version.");

    /// <summary>Parses the text form; see <see cref="TryParse(string?, out EntityVersion)"/>.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The version.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a version.</exception>
    public static EntityVersion Parse(string value) => Parse(value, provider: null);

    /// <summary>Returns the token text, safe to put between the quotes of an <c>ETag</c>; empty for <see cref="None"/>.</summary>
    /// <returns>The version as text.</returns>
    public override string ToString()
    {
        if (_format == 0)
            return string.Empty;

        Span<char> text = stackalloc char[TextLength];
        _ = TryFormat(text, out var written, default, provider: null);
        return new string(text[..written]);
    }

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (_format == 0)
        {
            charsWritten = 0;
            return true;
        }

        if (destination.Length < TextLength)
        {
            charsWritten = 0;
            return false;
        }

        Span<byte> token = stackalloc byte[TokenLength];
        token[0] = _format;
        BinaryPrimitives.WriteUInt32BigEndian(token[1..], _head);
        BinaryPrimitives.WriteUInt64BigEndian(token[5..], _body0);
        BinaryPrimitives.WriteUInt64BigEndian(token[13..], _body1);
        return Base64Url.TryEncodeToChars(token, destination, out charsWritten);
    }

    /// <inheritdoc />
    public bool Equals(EntityVersion other) =>
        _format == other._format && _head == other._head && _body0 == other._body0 && _body1 == other._body1;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EntityVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_format, _head, _body0, _body1);

    /// <summary>Compares two versions for equality.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when they are the same version.</returns>
    public static bool operator ==(EntityVersion left, EntityVersion right) => left.Equals(right);

    /// <summary>Compares two versions for inequality.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(EntityVersion left, EntityVersion right) => !left.Equals(right);
}
