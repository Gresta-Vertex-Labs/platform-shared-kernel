namespace SharedKernel.Domain.Policies;

/// <summary>A composite policy that a subject complies with only when it complies with both operands.</summary>
/// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
public sealed class AndPolicy<T> : IPolicy<T>
{
    private readonly IPolicy<T> _left;
    private readonly IPolicy<T> _right;

    /// <summary>
    /// Initializes a new policy that requires both <paramref name="left"/> and <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The first policy. Must not be null.</param>
    /// <param name="right">The second policy. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public AndPolicy(IPolicy<T> left, IPolicy<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }

    /// <summary>
    /// Returns whether <paramref name="subject"/> complies with both operands, evaluating the right operand only
    /// when the left one complies.
    /// </summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns>
    /// <see langword="true"/> when the subject complies with both operands; otherwise <see langword="false"/>.
    /// </returns>
    public bool IsCompliant(T subject) => _left.IsCompliant(subject) && _right.IsCompliant(subject);

    /// <summary>
    /// Returns the explanations of every operand <paramref name="subject"/> fails, joined with <c>"; "</c>.
    /// </summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns>
    /// The joined explanations, or <see cref="string.Empty"/> when the subject complies with both operands.
    /// </returns>
    public string Explain(T subject)
    {
        var failures = new List<string>(2);
        if (!_left.IsCompliant(subject))
            failures.Add(_left.Explain(subject));
        if (!_right.IsCompliant(subject))
            failures.Add(_right.Explain(subject));
        return string.Join("; ", failures);
    }
}
