namespace SharedKernel.AI.Abstractions.Models;

/// <summary>An equality predicate: <see cref="Field"/> equals <see cref="Value"/>.</summary>
/// <remarks>Construct only via <see cref="VectorFilter.Eq"/>.</remarks>
public sealed record EqualFilter : VectorFilter
{
    internal EqualFilter(string field, VectorValue value)
    {
        Field = field;
        Value = value;
    }

    /// <summary>Gets the field name this predicate applies to.</summary>
    public string Field { get; }

    /// <summary>Gets the value <see cref="Field"/> must equal.</summary>
    public VectorValue Value { get; }
}
