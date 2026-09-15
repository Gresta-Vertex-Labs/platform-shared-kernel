namespace SharedKernel.Domain.BusinessRules;

/// <summary>
/// A composite business rule that holds when at least one operand holds, and is broken only when both are.
/// </summary>
/// <remarks>
/// <para>
/// <b>Error code.</b> <see cref="Code"/> is always the left operand's code, and <see cref="Message"/> names both
/// alternatives.
/// </para>
/// <para>
/// <b>Pitfall.</b> Clients branching on the code see only the left alternative. When neither operand's code
/// describes the combined rule, write a dedicated <see cref="IBusinessRule"/> with its own code instead.
/// </para>
/// </remarks>
public sealed class OrBusinessRule : IBusinessRule
{
    private readonly IBusinessRule _left;
    private readonly IBusinessRule _right;

    /// <summary>
    /// Initializes a new rule that requires <paramref name="left"/>, <paramref name="right"/>, or both.
    /// </summary>
    /// <param name="left">The first alternative, whose code the combined rule reports. Must not be null.</param>
    /// <param name="right">The second alternative. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public OrBusinessRule(IBusinessRule left, IBusinessRule right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }

    /// <summary>Gets the left operand's code.</summary>
    public string Code => _left.Code;

    /// <summary>
    /// Gets a message of the form <c>At least one of these must hold: {left}; {right}</c>, whether or not
    /// the rule is broken.
    /// </summary>
    public string Message => $"At least one of these must hold: {_left.Message}; {_right.Message}";

    /// <summary>
    /// Returns whether both operands are broken, evaluating the right operand only when the left is broken.
    /// </summary>
    /// <returns><see langword="true"/> when both operands are broken; otherwise <see langword="false"/>.</returns>
    public bool IsBroken() => _left.IsBroken() && _right.IsBroken();
}
