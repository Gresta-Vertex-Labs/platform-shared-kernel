namespace SharedKernel.Domain.BusinessRules;

/// <summary>A composite business rule that holds only when both operands hold, and is broken when either is.</summary>
/// <remarks>
/// <para>
/// <b>Error code.</b> <see cref="Code"/> is the code of the first broken operand, checked left to right, so
/// the error names the actual violation. When neither operand is broken it is the left operand's code.
/// </para>
/// <para>
/// <b>Message.</b> <see cref="Message"/> joins the messages of every broken operand with <c>"; "</c>, and is
/// empty when neither is broken.
/// </para>
/// <para>
/// <b>Evaluation.</b> <see cref="Code"/> and <see cref="Message"/> are computed on each read by calling the
/// operands' <see cref="IBusinessRule.IsBroken"/> again; nothing is cached.
/// </para>
/// </remarks>
public sealed class AndBusinessRule : IBusinessRule
{
    private readonly IBusinessRule _left;
    private readonly IBusinessRule _right;

    /// <summary>
    /// Initializes a new rule that requires both <paramref name="left"/> and <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The first rule. Must not be null.</param>
    /// <param name="right">The second rule. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public AndBusinessRule(IBusinessRule left, IBusinessRule right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }

    /// <summary>
    /// Gets the code of the first broken operand, checked left to right, or the left operand's code when
    /// neither is broken.
    /// </summary>
    public string Code => !_left.IsBroken() && _right.IsBroken() ? _right.Code : _left.Code;

    /// <summary>
    /// Gets the messages of every broken operand joined with <c>"; "</c>, or an empty string when neither is
    /// broken.
    /// </summary>
    public string Message => (_left.IsBroken(), _right.IsBroken()) switch
    {
        (true, true) => $"{_left.Message}; {_right.Message}",
        (true, false) => _left.Message,
        (false, true) => _right.Message,
        _ => string.Empty,
    };

    /// <summary>
    /// Returns whether either operand is broken, evaluating the right operand only when the left holds.
    /// </summary>
    /// <returns><see langword="true"/> when either operand is broken; otherwise <see langword="false"/>.</returns>
    public bool IsBroken() => _left.IsBroken() || _right.IsBroken();
}
