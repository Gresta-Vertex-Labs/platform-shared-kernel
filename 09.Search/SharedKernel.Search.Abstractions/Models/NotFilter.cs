namespace SharedKernel.Search.Abstractions.Models;

/// <summary>A negation (logical NOT) of <see cref="Operand"/>.</summary>
/// <remarks>Construct only via <see cref="SearchFilter.Negate"/>.</remarks>
public sealed record NotFilter : SearchFilter
{
    internal NotFilter(SearchFilter operand)
    {
        Operand = operand;
    }

    /// <summary>Gets the negated operand.</summary>
    public SearchFilter Operand { get; }
}
