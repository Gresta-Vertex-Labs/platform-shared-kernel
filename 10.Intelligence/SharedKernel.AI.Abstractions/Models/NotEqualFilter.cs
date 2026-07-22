namespace SharedKernel.AI.Abstractions.Models;

/// <summary>An inequality predicate: <see cref="Field"/> does not equal <see cref="Value"/>.</summary>
/// <remarks>Construct only via <see cref="VectorFilter.Ne"/>.</remarks>
public sealed record NotEqualFilter : VectorFilter
{
    internal NotEqualFilter(string field, VectorValue value)
    {
        Field = field;
        Value = value;
    }

    /// <summary>Gets the field name this predicate applies to.</summary>
    public string Field { get; }

    /// <summary>Gets the value <see cref="Field"/> must not equal.</summary>
    public VectorValue Value { get; }
}
