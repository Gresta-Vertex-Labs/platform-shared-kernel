namespace SharedKernel.AI.Abstractions.Models;

/// <summary>A disjunction (logical OR) of <see cref="Operands"/>.</summary>
/// <remarks>Construct only via <see cref="VectorFilter.Any"/>.</remarks>
public sealed record OrFilter : VectorFilter
{
    internal OrFilter(IReadOnlyList<VectorFilter> operands)
    {
        Operands = operands;
    }

    /// <summary>Gets the operands this disjunction combines.</summary>
    public IReadOnlyList<VectorFilter> Operands { get; }
}
