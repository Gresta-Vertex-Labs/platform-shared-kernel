namespace SharedKernel.AI.Abstractions.Models;

/// <summary>A set-membership predicate: <see cref="Field"/> is one of <see cref="Values"/>.</summary>
/// <remarks>Construct only via <see cref="VectorFilter.In"/>.</remarks>
public sealed record InFilter : VectorFilter
{
    internal InFilter(string field, IReadOnlyList<VectorValue> values)
    {
        Field = field;
        Values = values;
    }

    /// <summary>Gets the field name this predicate applies to.</summary>
    public string Field { get; }

    /// <summary>Gets the set of legal values.</summary>
    public IReadOnlyList<VectorValue> Values { get; }
}
