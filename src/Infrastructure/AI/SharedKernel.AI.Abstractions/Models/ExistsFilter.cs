namespace SharedKernel.AI.Abstractions.Models;

/// <summary>A field-existence predicate: <see cref="Field"/> is present on the record.</summary>
/// <remarks>Construct only via <see cref="VectorFilter.Exists"/>.</remarks>
public sealed record ExistsFilter : VectorFilter
{
    internal ExistsFilter(string field)
    {
        Field = field;
    }

    /// <summary>Gets the field name this predicate applies to.</summary>
    public string Field { get; }
}
