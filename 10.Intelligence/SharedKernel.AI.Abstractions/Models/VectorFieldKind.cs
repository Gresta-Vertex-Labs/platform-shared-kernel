namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// The closed set of scalar kinds a <see cref="VectorFieldDefinition"/> may declare.
/// </summary>
/// <remarks>
/// Intentionally identical in shape to <see cref="VectorValueKind"/> — a metadata field's declared
/// <see cref="Kind"/>-typed role is exactly which <see cref="VectorValue"/> accessor a filter against
/// it must use, where <c>Kind</c> here refers to <see cref="VectorFieldDefinition.Kind"/>.
/// </remarks>
public enum VectorFieldKind
{
    /// <summary>A UTF-16 string field.</summary>
    String = 0,

    /// <summary>A 64-bit signed integer field.</summary>
    Int64 = 1,

    /// <summary>A double-precision floating-point field.</summary>
    Double = 2,

    /// <summary>A boolean field.</summary>
    Boolean = 3,

    /// <summary>A point-in-time field with an offset.</summary>
    DateTimeOffset = 4,
}
