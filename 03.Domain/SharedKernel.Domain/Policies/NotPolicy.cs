namespace SharedKernel.Domain.Policies;

/// <summary>A policy that a subject complies with when it does not comply with the inner policy.</summary>
/// <typeparam name="T">The type of subject the policy evaluates.</typeparam>
/// <remarks>
/// The inner policy's explanation describes the opposite condition, so a negated policy takes its own
/// explanation.
/// </remarks>
public sealed class NotPolicy<T> : IPolicy<T>
{
    private readonly IPolicy<T> _inner;
    private readonly string _explanation;

    /// <summary>Negates <paramref name="inner"/>.</summary>
    /// <param name="inner">The policy to negate.</param>
    /// <param name="explanation">The explanation returned when a subject complies with <paramref name="inner"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="explanation"/> is null, empty or whitespace.</exception>
    public NotPolicy(IPolicy<T> inner, string explanation)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        _inner = inner;
        _explanation = explanation;
    }

    /// <inheritdoc/>
    public bool IsCompliant(T subject) => !_inner.IsCompliant(subject);

    /// <inheritdoc/>
    public string Explain(T subject) => IsCompliant(subject) ? string.Empty : _explanation;
}
