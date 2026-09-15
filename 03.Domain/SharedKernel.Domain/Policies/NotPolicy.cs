namespace SharedKernel.Domain.Policies;

/// <summary>A policy that a subject complies with exactly when it does not comply with the inner policy.</summary>
/// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
/// <remarks>
/// <b>Explanation.</b> The inner policy's explanation describes the opposite condition, so the negated policy
/// takes its own explanation at construction.
/// </remarks>
public sealed class NotPolicy<T> : IPolicy<T>
{
    private readonly IPolicy<T> _inner;
    private readonly string _explanation;

    /// <summary>Initializes a new policy negating <paramref name="inner"/>, with its own explanation.</summary>
    /// <param name="inner">The policy to negate. Must not be null.</param>
    /// <param name="explanation">
    /// The explanation returned when a subject complies with <paramref name="inner"/>. Must not be null or
    /// whitespace.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="inner"/> or <paramref name="explanation"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="explanation"/> is empty or whitespace.</exception>
    public NotPolicy(IPolicy<T> inner, string explanation)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        _inner = inner;
        _explanation = explanation;
    }

    /// <summary>Returns whether <paramref name="subject"/> fails the inner policy.</summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns>
    /// <see langword="true"/> when the subject does not comply with the inner policy; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public bool IsCompliant(T subject) => !_inner.IsCompliant(subject);

    /// <summary>
    /// Returns the explanation supplied at construction when <paramref name="subject"/> complies with the inner
    /// policy.
    /// </summary>
    /// <param name="subject">The subject to evaluate.</param>
    /// <returns>The supplied explanation; or <see cref="string.Empty"/> when this policy is satisfied.</returns>
    public string Explain(T subject) => IsCompliant(subject) ? string.Empty : _explanation;
}
