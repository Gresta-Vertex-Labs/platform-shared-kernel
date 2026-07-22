namespace SharedKernel.AI.Abstractions.Models;

/// <summary>A negation (logical NOT) of <see cref="Operand"/>.</summary>
/// <remarks>Construct only via <see cref="VectorFilter.Negate"/>.</remarks>
public sealed record NotFilter : VectorFilter
{
    internal NotFilter(VectorFilter operand)
    {
        Operand = operand;
    }

    /// <summary>Gets the negated operand.</summary>
    public VectorFilter Operand { get; }
}
