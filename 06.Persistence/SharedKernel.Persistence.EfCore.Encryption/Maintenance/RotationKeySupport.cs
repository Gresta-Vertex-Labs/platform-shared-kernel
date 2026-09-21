namespace SharedKernel.Persistence.EfCore.Encryption.Maintenance;

/// <summary>The primary-key provider shapes the maintenance job can rotate.</summary>
/// <remarks>
/// Covers <c>StronglyTypedId&lt;Guid&gt;</c>/<c>&lt;long&gt;</c>/<c>&lt;string&gt;</c> and a bare <see langword="int"/>
/// key — the realistic single-column primary-key shapes on this platform. A composite key, or a single-column key
/// whose provider type is none of these, is unsupported — see <see cref="RotationKeySupport.Classify"/>.
/// </remarks>
internal enum RotationKeyKind
{
    /// <summary><see cref="Guid"/>.</summary>
    Guid,

    /// <summary><see langword="long"/> (<see cref="Int64"/>).</summary>
    Int64,

    /// <summary><see langword="int"/> (<see cref="Int32"/>).</summary>
    Int32,

    /// <summary><see langword="string"/>.</summary>
    String,
}

/// <summary>
/// Classifies a primary key's provider CLR type into a <see cref="RotationKeyKind"/>, or reports it unsupported.
/// </summary>
/// <remarks>
/// Shared by the model convention (which validates at model-build time — every entity type with
/// an <c>.Encrypt(...)</c> property must have a supported key shape BEFORE the host accepts traffic, not only when
/// a rotation happens to run) and the maintenance job (which uses the classification
/// to read/write the key's raw ADO.NET value with the correct type).
/// </remarks>
internal static class RotationKeySupport
{
    /// <summary>Classifies <paramref name="providerClrType"/>, or returns <see langword="null"/> when unsupported.</summary>
    /// <param name="providerClrType">A primary key property's provider (post-value-converter) CLR type.</param>
    public static RotationKeyKind? Classify(Type providerClrType)
    {
        var type = Nullable.GetUnderlyingType(providerClrType) ?? providerClrType;

        if (type == typeof(Guid))
            return RotationKeyKind.Guid;

        if (type == typeof(long))
            return RotationKeyKind.Int64;

        if (type == typeof(int))
            return RotationKeyKind.Int32;

        if (type == typeof(string))
            return RotationKeyKind.String;

        return null;
    }
}
