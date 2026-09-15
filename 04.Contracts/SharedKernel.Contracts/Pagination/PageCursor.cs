using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Contracts.Pagination;

/// <summary>
/// Encodes a keyset position, the sort key and identity key of the last item on a page, into an opaque URL-safe
/// cursor, and decodes it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> After reading a page ordered by (key, id), encode the last item's key and id as
/// <see cref="CursorPagedList{T}.NextCursor"/>. On the next request, decode the cursor and pass the key and id to
/// the keyset query, such as <c>KeysetSpecification</c> in <c>SharedKernel.Domain</c> as its <c>afterKey</c> and
/// <c>afterId</c>.
/// </para>
/// <para>
/// <b>Format.</b> <c>v1.</c> followed by the base64url encoding of a two-element JSON array
/// <c>[key, id]</c>. Clients must treat it as opaque; the format can change behind the version prefix.
/// </para>
/// <para>
/// <b>Types.</b> The key and id are written with <see cref="JsonSerializer"/>, using
/// <see cref="JsonSerializerOptions.Default"/> unless you pass options. Pass the underlying value of a
/// strongly-typed identifier (<c>order.Id.Value</c>), or pass options with a converter for it. Decode with the
/// same types and options used to encode.
/// </para>
/// <para>
/// <b>Security.</b> The cursor is not signed or encrypted. Anyone can read the key and id in it, and a client can
/// forge one. That is safe when the decoded position only moves where the page starts: the query must still apply
/// every tenant and authorization filter itself. Never put anything in a cursor the caller may not see.
/// </para>
/// <para>
/// <b>Pitfall.</b> A cursor only makes sense for the query and sort order that issued it. Decoding a cursor from a
/// different sort succeeds when the types line up and silently starts at the wrong place. Keep the sort fixed per
/// endpoint, or include the sort in the key.
/// </para>
/// </remarks>
public static class PageCursor
{
    /// <summary>The longest cursor <see cref="Decode{TKey, TId}"/> accepts and <see cref="Encode{TKey, TId}"/> produces: 512 characters.</summary>
    public const int MaxLength = 512;

    private const string VersionPrefix = "v1.";

    internal static readonly Error InvalidCursorError = Error.Validation(
        PaginationErrorCodes.CursorInvalid,
        "The cursor is malformed or does not belong to this query. Request the first page again.");

    /// <summary>Encodes a keyset position as an opaque cursor.</summary>
    /// <typeparam name="TKey">The type of the sort key, such as <see cref="DateTimeOffset"/>.</typeparam>
    /// <typeparam name="TId">The type of the identity key, such as <see cref="Guid"/>.</typeparam>
    /// <param name="key">The sort key of the last item on the page. Must not be null.</param>
    /// <param name="id">The identity key of the last item on the page. Must not be null.</param>
    /// <param name="options">The serializer options for the key and id, or <see langword="null"/> for the defaults.</param>
    /// <returns>A URL-safe cursor of at most <see cref="MaxLength"/> characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="id"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The encoded cursor would be longer than <see cref="MaxLength"/>.</exception>
    /// <exception cref="NotSupportedException">The serializer cannot write <typeparamref name="TKey"/> or <typeparamref name="TId"/>.</exception>
    public static string Encode<TKey, TId>(TKey key, TId id, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(id);

        options ??= JsonSerializerOptions.Default;

        var buffer = new ArrayBufferWriter<byte>(64);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            JsonSerializer.Serialize(writer, key, options);
            JsonSerializer.Serialize(writer, id, options);
            writer.WriteEndArray();
        }

        var cursor = VersionPrefix + Base64Url.EncodeToString(buffer.WrittenSpan);
        if (cursor.Length > MaxLength)
        {
            throw new ArgumentException(
                $"The encoded cursor is {cursor.Length} characters, above the limit of {MaxLength}. Use a shorter sort key.",
                nameof(key));
        }

        return cursor;
    }

    /// <summary>Decodes a cursor produced by <see cref="Encode{TKey, TId}"/>.</summary>
    /// <typeparam name="TKey">The sort key type the cursor was encoded with.</typeparam>
    /// <typeparam name="TId">The identity key type the cursor was encoded with.</typeparam>
    /// <param name="cursor">The cursor from the client.</param>
    /// <param name="options">The serializer options the cursor was encoded with, or <see langword="null"/> for the defaults.</param>
    /// <returns>
    /// A success with the keyset position; otherwise a failure with a <see cref="PaginationErrorCodes.CursorInvalid"/>
    /// validation error when the cursor is null, blank, too long, not produced by this codec, or holds values of other
    /// types. Never throws for bad input.
    /// </returns>
    public static Result<CursorPosition<TKey, TId>> Decode<TKey, TId>(string? cursor, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(cursor)
            || cursor.Length > MaxLength
            || !cursor.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            return Result<CursorPosition<TKey, TId>>.Failure(InvalidCursorError);
        }

        options ??= JsonSerializerOptions.Default;

        try
        {
            var json = Base64Url.DecodeFromChars(cursor.AsSpan(VersionPrefix.Length));
            var reader = new Utf8JsonReader(json);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray || !reader.Read())
                return Result<CursorPosition<TKey, TId>>.Failure(InvalidCursorError);

            var key = JsonSerializer.Deserialize<TKey>(ref reader, options);
            if (key is null || !reader.Read())
                return Result<CursorPosition<TKey, TId>>.Failure(InvalidCursorError);

            var id = JsonSerializer.Deserialize<TId>(ref reader, options);
            if (id is null
                || !reader.Read()
                || reader.TokenType != JsonTokenType.EndArray
                || reader.Read())
            {
                return Result<CursorPosition<TKey, TId>>.Failure(InvalidCursorError);
            }

            return Result<CursorPosition<TKey, TId>>.Success(new CursorPosition<TKey, TId>(key, id));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or NotSupportedException or InvalidOperationException)
        {
            return Result<CursorPosition<TKey, TId>>.Failure(InvalidCursorError);
        }
    }
}
