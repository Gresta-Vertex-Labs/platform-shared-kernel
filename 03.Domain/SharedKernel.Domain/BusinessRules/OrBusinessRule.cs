namespace SharedKernel.Domain.BusinessRules;

/// <summary>
/// A composite business rule that is broken only when both sub-rules are broken
/// (logical OR — at least one must pass for the composite to pass).
/// </summary>
public sealed class OrBusinessRule : IBusinessRule
{
    private readonly IBusinessRule _left;
    private readonly IBusinessRule _right;

    /// <summary>
    /// Initialises a new <see cref="OrBusinessRule"/> combining <paramref name="left"/>
    /// and <paramref name="right"/>.
    /// </summary>
    public OrBusinessRule(IBusinessRule left, IBusinessRule right)
    {
        _left = left;
        _right = right;
    }

    /// <inheritdoc/>
    public string Message => $"{_left.Message} or {_right.Message}";

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="true"/> only when both sub-rules are broken.</remarks>
    public bool IsBroken() => _left.IsBroken() && _right.IsBroken();
}
