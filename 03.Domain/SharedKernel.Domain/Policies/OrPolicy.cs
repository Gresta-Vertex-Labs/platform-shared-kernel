namespace SharedKernel.Domain.Policies;

/// <summary>A composite policy that a subject complies with when it complies with at least one operand.</summary>
/// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
public sealed class OrPolicy<T> : IPolicy<T>
{
    private readonly IPolicy<T> _left;
    private readonly IPolicy<T> _right;

    /// <summary>
    /// Initializes a new policy that requires <paramref name="left"/>, <paramref name="right"/>, or both.
    /// </summary>
    /// <param name="left">The first alternative. Must not be null.</param>
    /// <param name="right">The second alternative. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public OrPolicy(IPolicy<T> left, IPolicy<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }

    /// <summary>
    /// Returns whether <paramref name="subject"/> complies with at least one operand, evaluating the right
    /// operand only when the left one does not comply.
    /// </summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns>
    /// <see langword="true"/> when the subject complies with either operand; otherwise <see langword="false"/>.
    /// </returns>
    public bool IsCompliant(T subject) => _left.IsCompliant(subject) || _right.IsCompliant(subject);

    /// <summary>Returns an explanation naming both alternatives when <paramref name="subject"/> fails both.</summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns>
    /// <see cref="string.Empty"/> when the subject complies with either operand; otherwise
    /// <c>At least one of these must be satisfied: {left}; {right}</c>.
    /// </returns>
    public string Explain(T subject) =>
        IsCompliant(subject)
            ? string.Empty
            : $"At least one of these must be satisfied: {_left.Explain(subject)}; {_right.Explain(subject)}";
}
