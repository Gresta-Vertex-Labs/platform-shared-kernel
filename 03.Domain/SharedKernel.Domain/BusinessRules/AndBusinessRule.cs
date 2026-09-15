namespace SharedKernel.Domain.BusinessRules;

/// <summary>A rule that holds only when both operands hold; it is broken when either one is.</summary>
/// <remarks>
/// <see cref="Code"/> is the code of the first broken operand, checked left to right, so the error names the
/// actual violation. <see cref="Message"/> joins the messages of every broken operand with "; ", and is empty when neither is broken.
/// </remarks>
public sealed class AndBusinessRule : IBusinessRule
{
    private readonly IBusinessRule _left;
    private readonly IBusinessRule _right;

    /// <summary>Combines two rules that must both hold.</summary>
    /// <param name="left">The first rule.</param>
    /// <param name="right">The second rule.</param>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public AndBusinessRule(IBusinessRule left, IBusinessRule right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }

    /// <inheritdoc/>
    public string Code => !_left.IsBroken() && _right.IsBroken() ? _right.Code : _left.Code;

    /// <inheritdoc/>
    public string Message => (_left.IsBroken(), _right.IsBroken()) switch
    {
        (true, true) => $"{_left.Message}; {_right.Message}",
        (true, false) => _left.Message,
        (false, true) => _right.Message,
        _ => string.Empty,
    };

    /// <inheritdoc/>
    public bool IsBroken() => _left.IsBroken() || _right.IsBroken();
}
