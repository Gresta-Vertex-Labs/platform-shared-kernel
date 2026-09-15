namespace SharedKernel.Domain.BusinessRules;

/// <summary>A rule that holds when at least one operand holds; it is broken only when both are.</summary>
/// <remarks>
/// A broken <see cref="OrBusinessRule"/> reports the left operand's <see cref="Code"/> and a
/// <see cref="Message"/> naming both alternatives. When neither code describes the combined rule well,
/// write a dedicated <see cref="IBusinessRule"/> instead.
/// </remarks>
public sealed class OrBusinessRule : IBusinessRule
{
    private readonly IBusinessRule _left;
    private readonly IBusinessRule _right;

    /// <summary>Combines two alternative rules, at least one of which must hold.</summary>
    /// <param name="left">The first alternative.</param>
    /// <param name="right">The second alternative.</param>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public OrBusinessRule(IBusinessRule left, IBusinessRule right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }

    /// <inheritdoc/>
    public string Code => _left.Code;

    /// <inheritdoc/>
    public string Message => $"At least one of these must hold: {_left.Message}; {_right.Message}";

    /// <inheritdoc/>
    public bool IsBroken() => _left.IsBroken() && _right.IsBroken();
}
