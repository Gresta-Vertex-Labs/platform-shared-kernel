namespace SharedKernel.Search.Abstractions.Models;

/// <summary>A disjunction (logical OR) of <see cref="Operands"/>.</summary>
/// <remarks>Construct only via <see cref="SearchFilter.Any"/>.</remarks>
public sealed record OrFilter : SearchFilter
{
    internal OrFilter(IReadOnlyList<SearchFilter> operands)
    {
        Operands = operands;
    }

    /// <summary>Gets the operands this disjunction combines.</summary>
    public IReadOnlyList<SearchFilter> Operands { get; }
}
