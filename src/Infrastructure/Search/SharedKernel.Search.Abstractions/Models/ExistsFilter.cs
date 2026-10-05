namespace SharedKernel.Search.Abstractions.Models;

/// <summary>A field-existence predicate: <see cref="Field"/> is present on the document.</summary>
/// <remarks>Construct only via <see cref="SearchFilter.Exists"/>.</remarks>
public sealed record ExistsFilter : SearchFilter
{
    internal ExistsFilter(string field)
    {
        Field = field;
    }

    /// <summary>Gets the field name this predicate applies to.</summary>
    public string Field { get; }
}
