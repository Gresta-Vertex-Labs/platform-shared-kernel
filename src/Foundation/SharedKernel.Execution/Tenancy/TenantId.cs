using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Execution.Tenancy;

/// <summary>
/// The identifier of a tenant: a non-empty <see cref="Guid"/> that cannot be confused with any other
/// identifier at a call site.
/// </summary>
/// <remarks>
/// <para>
/// "No tenant" is expressed as <see langword="null"/> (<c>TenantId?</c>), never as
/// <see cref="Guid.Empty"/>. The constructor rejects <see cref="Guid.Empty"/>, so every constructed value
/// names a real tenant. <c>default(TenantId)</c> still exists, because a struct always has a default; it is
/// not a valid tenant, and <see cref="IsDefault"/> reports it.
/// </para>
/// <para>
/// The string form is the lowercase hyphenated <c>"D"</c> format (<c>0f8fad5b-d9cb-469f-a165-70867728950e</c>).
/// Cache keys, storage prefixes, headers, baggage and log properties all use <see cref="ToString()"/>, so
/// every layer formats a tenant the same way. JSON reads and writes the same string.
/// </para>
/// </remarks>
[JsonConverter(typeof(TenantIdJsonConverter))]
public readonly record struct TenantId : IParsable<TenantId>, ISpanFormattable
{
    /// <summary>Creates a tenant identifier.</summary>
    /// <param name="value">The tenant's <see cref="Guid"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see cref="Guid.Empty"/>.</exception>
    public TenantId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A tenant id cannot be Guid.Empty. Use a null TenantId? for \"no tenant\".", nameof(value));

        Value = value;
    }

    /// <summary>Gets the tenant's <see cref="Guid"/>.</summary>
    public Guid Value { get; }

    /// <summary>
    /// Gets a value indicating whether this is <c>default(TenantId)</c>, which names no tenant.
    /// </summary>
    public bool IsDefault => Value == Guid.Empty;

    /// <summary>
    /// Converts a nullable <see cref="Guid"/> to a nullable tenant id, treating <see langword="null"/> and
    /// <see cref="Guid.Empty"/> as "no tenant".
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The tenant id, or <see langword="null"/>.</returns>
    public static TenantId? FromNullable(Guid? value) =>
        value is { } guid && guid != Guid.Empty ? new TenantId(guid) : null;

    /// <summary>Parses the <c>"D"</c>-format string form of a tenant id.</summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">Ignored.</param>
    /// <returns>The parsed tenant id.</returns>
    /// <exception cref="FormatException"><paramref name="s"/> is not a non-empty <see cref="Guid"/>.</exception>
    public static TenantId Parse(string s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out var result)
            ? result
            : throw new FormatException("A tenant id must be a non-empty GUID.");

    /// <summary>Tries to parse the string form of a tenant id.</summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">Ignored.</param>
    /// <param name="result">The parsed tenant id, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="s"/> is a non-empty <see cref="Guid"/>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TenantId result)
    {
        if (Guid.TryParse(s, out var guid) && guid != Guid.Empty)
        {
            result = new TenantId(guid);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>Tries to parse the string form of a tenant id.</summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="result">The parsed tenant id, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="s"/> is a non-empty <see cref="Guid"/>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, out TenantId result) => TryParse(s, null, out result);

    /// <summary>Returns the lowercase hyphenated <c>"D"</c> form of the identifier.</summary>
    /// <returns>The string form.</returns>
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <inheritdoc/>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        Value.TryFormat(destination, out charsWritten, "D");

    /// <summary>Converts a tenant id to its <see cref="Guid"/>.</summary>
    /// <param name="tenantId">The tenant id.</param>
    public static explicit operator Guid(TenantId tenantId) => tenantId.Value;

    /// <summary>Converts a <see cref="Guid"/> to a tenant id.</summary>
    /// <param name="value">The <see cref="Guid"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see cref="Guid.Empty"/>.</exception>
    public static explicit operator TenantId(Guid value) => new(value);

    /// <summary>Reads and writes a <see cref="TenantId"/> as its <c>"D"</c>-format string.</summary>
    public sealed class TenantIdJsonConverter : JsonConverter<TenantId>
    {
        /// <inheritdoc/>
        public override TenantId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var guid) && guid != Guid.Empty)
                return new TenantId(guid);

            throw new JsonException("A tenant id must be a non-empty GUID string.");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, TenantId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.Value);
        }
    }
}
