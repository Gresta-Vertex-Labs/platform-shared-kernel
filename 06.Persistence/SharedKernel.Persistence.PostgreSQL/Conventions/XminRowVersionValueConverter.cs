using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SharedKernel.Persistence.PostgreSQL.Conventions;

/// <summary>
/// Round-trips the domain's <see cref="System.Array">byte[]</see> <c>RowVersion</c> representation
/// to/from Npgsql's native <see cref="uint"/> <c>xid</c> value.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL's <c>xmin</c> system column is a 4-byte unsigned transaction identifier
/// (<c>xid</c>), which Npgsql maps to <see cref="uint"/>. The domain-facing
/// <see cref="SharedKernel.Domain.Abstractions.IHasConcurrency.RowVersion"/> contract is a
/// provider-neutral <c>byte[]</c>, so this converter bridges the two representations using
/// <see cref="BinaryPrimitives"/> big-endian encode/decode — BCL-only, zero reflection.
/// </para>
/// <para>
/// Applied automatically by <see cref="XminConcurrencyTokenConvention"/> to the property already
/// marked <c>.IsConcurrencyToken()</c> by <c>EntityTypeConfigurationBase</c> — never instantiated
/// directly in application or entity-configuration code.
/// </para>
/// </remarks>
public sealed class XminRowVersionValueConverter : ValueConverter<byte[], uint>
{
    /// <summary>Initialises a new <see cref="XminRowVersionValueConverter"/>.</summary>
    public XminRowVersionValueConverter()
        : base(
            model => ToProvider(model),
            provider => FromProvider(provider))
    {
    }

    // byte[] (big-endian, 4 bytes) -> uint. Any non-4-byte input (including a freshly-constructed
    // aggregate's default/empty RowVersion, which is never sent to the server for a value-generated
    // OnAddOrUpdate column) converts to 0 rather than throwing.
    private static uint ToProvider(byte[] model) =>
        model is { Length: 4 } ? BinaryPrimitives.ReadUInt32BigEndian(model) : 0u;

    // uint -> byte[] (big-endian, 4 bytes) — always a well-formed 4-byte array.
    private static byte[] FromProvider(uint provider)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, provider);
        return bytes;
    }
}
