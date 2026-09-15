namespace SharedKernel.Domain.Policies;

/// <summary>A policy that a subject complies with when it complies with at least one operand.</summary>
/// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
public sealed class OrPolicy<T> : IPolicy<T>
{
    private readonly IPolicy<T> _left;
    private readonly IPolicy<T> _right;

    /// <summary>Combines two alternative policies, at least one of which must be satisfied.</summary>
    /// <param name="left">The first alternative.</param>
    /// <param name="right">The second alternative.</param>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public OrPolicy(IPolicy<T> left, IPolicy<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
    }

    /// <inheritdoc/>
    public bool IsCompliant(T subject) => _left.IsCompliant(subject) || _right.IsCompliant(subject);

    /// <inheritdoc/>
    /// <remarks>When the subject fails both alternatives, names both explanations.</remarks>
    public string Explain(T subject) =>
        IsCompliant(subject)
            ? string.Empty
            : $"At least one of these must be satisfied: {_left.Explain(subject)}; {_right.Explain(subject)}";
}
