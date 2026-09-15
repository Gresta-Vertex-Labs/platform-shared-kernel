namespace SharedKernel.Domain.Policies;

/// <summary>A policy that a subject complies with only when it complies with both operands.</summary>
/// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
public sealed class AndPolicy<T> : IPolicy<T>
{
    private readonly IPolicy<T> _left;
    private readonly IPolicy<T> _right;

    /// <summary>Combines two policies that must both be satisfied.</summary>
    /// <param name="left">The first policy.</param>
    /// <param name="right">The second policy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public AndPolicy(IPolicy<T> left, IPolicy<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }

    /// <inheritdoc/>
    public bool IsCompliant(T subject) => _left.IsCompliant(subject) && _right.IsCompliant(subject);

    /// <inheritdoc/>
    /// <remarks>Combines the explanations of every operand the subject fails.</remarks>
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
