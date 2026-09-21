using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// The stored version of an aggregate, for optimistic concurrency over HTTP (<c>ETag</c> / <c>If-Match</c>).
/// Opaque: send it to the client as text and parse what comes back; never compute with it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Round trip:</strong> <c>response.Headers.ETag = $"\"{version}\""</c>; on the next request
/// <c>EntityVersion.TryParse(request.Headers.IfMatch, out var expected)</c> (surrounding quotes and a weak
/// <c>W/</c> prefix are accepted) and pass <c>expected</c> to <c>IRepository.UpdateAsync(aggregate, expected)</c>
/// or <c>DeleteAsync(aggregate, expected)</c>.
/// </para>
/// <para>
/// The representation is the provider's row version (PostgreSQL's <c>xmin</c>) and may change between
/// providers; <see cref="None"/> is the version of an aggregate that was never saved.
/// </para>
/// </remarks>
public readonly struct EntityVersion : IEquatable<EntityVersion>, ISpanFormattable, IParsable<EntityVersion>
{
    private readonly ulong _value;

    private EntityVersion(ulong value) => _value = value;

    /// <summary>Gets the version of an aggregate that was never saved.</summary>
    public static EntityVersion None => default;

    /// <summary>Creates a version from a provider's raw row version. For persistence providers, not application code.</summary>
    /// <param name="rowVersion">The provider's row version.</param>
    /// <returns>The version.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static EntityVersion FromRowVersion(ulong rowVersion) => new(rowVersion);

    /// <summary>Returns the provider's raw row version. For persistence providers, not application code.</summary>
    /// <returns>The raw row version.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ulong ToRowVersion() => _value;

    /// <summary>Parses the text form (<see cref="ToString()"/>), optionally quoted and with a weak <c>W/</c> prefix.</summary>
    /// <param name="value">The text, such as an <c>If-Match</c> header value.</param>
    /// <param name="version">The parsed version.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a version.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out EntityVersion version) =>
        TryParse(value, provider: null, out version);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out EntityVersion result)
    {
        result = default;
        if (s is null)
            return false;

        var span = s.AsSpan().Trim();
        if (span.StartsWith("W/", StringComparison.Ordinal))
            span = span[2..];

        if (span.Length >= 2 && span[0] == '"' && span[^1] == '"')
            span = span[1..^1];

        if (span.IsEmpty || span[0] is '+' or '-')
            return false;

        if (!ulong.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return false;

        result = new EntityVersion(value);
        return true;
    }

    /// <inheritdoc />
    public static EntityVersion Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var version)
            ? version
            : throw new FormatException($"'{s}' is not an entity version.");

    /// <summary>Parses the text form; see <see cref="TryParse(string?, out EntityVersion)"/>.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The version.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a version.</exception>
    public static EntityVersion Parse(string value) => Parse(value, provider: null);

    /// <summary>Returns the text form, safe to put between the quotes of an <c>ETag</c>.</summary>
    /// <returns>The version as text.</returns>
    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        _value.TryFormat(destination, out charsWritten, default, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public bool Equals(EntityVersion other) => _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EntityVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();

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
