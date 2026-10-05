namespace SharedKernel.Search.Abstractions.Models;

/// <summary>A conjunction (logical AND) of <see cref="Operands"/>.</summary>
/// <remarks>Construct only via <see cref="SearchFilter.All"/>.</remarks>
public sealed record AndFilter : SearchFilter
{
    internal AndFilter(IReadOnlyList<SearchFilter> operands)
    {
        Operands = operands;
    }

    /// <summary>Gets the operands this conjunction combines.</summary>
    public IReadOnlyList<SearchFilter> Operands { get; }
}
