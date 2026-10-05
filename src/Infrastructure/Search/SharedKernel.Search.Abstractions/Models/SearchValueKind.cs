namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The closed set of scalar kinds a <see cref="SearchValue"/> may hold.
/// </summary>
public enum SearchValueKind
{
    /// <summary>A UTF-8 text value.</summary>
    String = 0,

    /// <summary>A 64-bit signed integer value.</summary>
    Int64 = 1,

    /// <summary>A double-precision floating point value.</summary>
    Double = 2,

    /// <summary>A boolean value.</summary>
    Boolean = 3,

    /// <summary>A point-in-time value with an offset from UTC.</summary>
    DateTimeOffset = 4,
}
