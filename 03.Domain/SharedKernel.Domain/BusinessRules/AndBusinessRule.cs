namespace SharedKernel.Domain.BusinessRules;

/// <summary>
/// A composite business rule that is broken when either the left or right sub-rule is broken
/// (logical AND — both must pass for the composite to pass).
/// </summary>
/// <remarks>
/// <see cref="Message"/> aggregates the messages of all currently-broken sub-rules,
/// separated by <c>"; "</c>. If neither sub-rule is broken, <see cref="Message"/> returns
/// an empty string.
/// </remarks>
public sealed class AndBusinessRule : IBusinessRule
{
    private readonly IBusinessRule _left;
    private readonly IBusinessRule _right;

    /// <summary>
    /// Initialises a new <see cref="AndBusinessRule"/> combining <paramref name="left"/>
    /// and <paramref name="right"/>.
    /// </summary>
    public AndBusinessRule(IBusinessRule left, IBusinessRule right)
    {
        _left = left;
        _right = right;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Aggregates the messages of broken sub-rules with <c>"; "</c> as a delimiter.
    /// Returns an empty string when no sub-rules are broken.
    /// </remarks>
    public string Message =>
        string.Join("; ", new[] { _left, _right }.Where(r => r.IsBroken()).Select(r => r.Message));

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="true"/> when either sub-rule is broken.</remarks>
    public bool IsBroken() => _left.IsBroken() || _right.IsBroken();
}
