namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// The closed set of scalar kinds a <see cref="VectorValue"/> may hold.
/// </summary>
/// <remarks>
/// Deliberately identical in shape to <c>VectorFieldKind</c> — a metadata field's declared
/// <c>VectorFieldKind</c> is exactly which <see cref="VectorValue"/> accessor a filter against it must
/// use.
/// </remarks>
public enum VectorValueKind
{
    /// <summary>A UTF-16 string value.</summary>
    String = 0,

    /// <summary>A 64-bit signed integer value.</summary>
    Int64 = 1,

    /// <summary>A double-precision floating-point value.</summary>
    Double = 2,

    /// <summary>A boolean value.</summary>
    Boolean = 3,

    /// <summary>A point-in-time value with an offset.</summary>
    DateTimeOffset = 4,
}
