namespace SharedKernel.AI.Abstractions.Models;

/// <summary>A conjunction (logical AND) of <see cref="Operands"/>.</summary>
/// <remarks>Construct only via <see cref="VectorFilter.All"/>.</remarks>
public sealed record AndFilter : VectorFilter
{
    internal AndFilter(IReadOnlyList<VectorFilter> operands)
    {
        Operands = operands;
    }

    /// <summary>Gets the operands this conjunction combines.</summary>
    public IReadOnlyList<VectorFilter> Operands { get; }
}
