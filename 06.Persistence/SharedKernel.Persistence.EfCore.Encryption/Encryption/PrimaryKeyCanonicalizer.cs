using Microsoft.EntityFrameworkCore.Metadata;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Produces a stable, deterministic byte encoding of an entity's primary key, for use as an AAD component.
/// </summary>
/// <remarks>
/// Reflection-free: reads each key property's value through EF Core's own compiled accessor delegate
/// (<see cref="IPropertyBase.GetGetter"/>), the same mechanism EF Core itself uses internally for materialization —
/// never <see cref="System.Reflection.PropertyInfo.GetValue(object?)"/>. When the property has a value converter
/// (for example <c>StronglyTypedIdValueConverter</c>), the CLR value is converted to its provider representation
/// first, so the canonical form matches what is actually stored, not an opaque wrapper type's default
/// <see cref="object.ToString"/>.
/// </remarks>
internal static class PrimaryKeyCanonicalizer
{
    /// <summary>
    /// Builds the canonical byte encoding of <paramref name="instance"/>'s primary key, as declared by
    /// <paramref name="entityType"/>.
    /// </summary>
    /// <param name="entityType">The entity type metadata — must have a primary key.</param>
    /// <param name="instance">The entity instance, with its key property values already assigned.</param>
    /// <returns>The canonical bytes, stable across processes and .NET versions for the same logical key value.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="entityType"/> has no primary key.</exception>
    public static byte[] Canonicalize(IEntityType entityType, object instance)
    {
        var key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"'{entityType.ShortName()}' has no primary key — an encrypted property needs one to bind its " +
                "associated data to the owning row.");

        using var buffer = new MemoryStream();
        foreach (var keyProperty in key.Properties)
        {
            var clrValue = keyProperty.GetGetter().GetClrValue(instance);
            var converter = keyProperty.GetValueConverter();
            var providerValue = converter is null ? clrValue : converter.ConvertToProvider(clrValue);

            var component = CanonicalizeValue(providerValue)
                ?? throw new InvalidOperationException(
                    $"'{entityType.ShortName()}.{keyProperty.Name}' resolved to a null primary key value — " +
                    "an entity must have its key assigned before an encrypted property on it can be saved.");

            WriteLengthPrefixed(buffer, component);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Builds the canonical byte encoding for a primary key already known to be a single, provider-level value
    /// (<see cref="Guid"/>, <see langword="long"/>, <see langword="int"/> or <see langword="string"/> — see
    /// <c>Rotation.RotationKeyKind</c>) — the exact same wire shape <see cref="Canonicalize"/> produces for a
    /// single-column-key entity, without needing a materialized entity instance.
    /// </summary>
    /// <remarks>
    /// Used by <c>EncryptionRotationService{TContext}</c>, which reads the raw provider value directly via ADO.NET
    /// rather than through a materialized entity. Must stay byte-for-byte identical to what
    /// <see cref="Canonicalize"/> produces for the same logical key (both route through the same
    /// <see cref="CanonicalizeValue"/> dispatch), or a row re-encrypted during rotation would carry associated data
    /// that never decrypts.
    /// </remarks>
    /// <param name="value">The primary key's raw provider value.</param>
    /// <returns>The canonical bytes.</returns>
    public static byte[] CanonicalizeSingleValue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        using var buffer = new MemoryStream();
        WriteLengthPrefixed(buffer, CanonicalizeValue(value)!);
        return buffer.ToArray();
    }

    private static byte[]? CanonicalizeValue(object? value) => value switch
    {
        null => null,
        Guid guid => guid.ToByteArray(),
        byte[] bytes => bytes,
        string text => System.Text.Encoding.UTF8.GetBytes(text),
        _ => System.Text.Encoding.UTF8.GetBytes(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
    };

    private static void WriteLengthPrefixed(Stream stream, byte[] component)
    {
        Span<byte> lengthPrefix = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, component.Length);
        stream.Write(lengthPrefix);
        stream.Write(component);
    }
}
