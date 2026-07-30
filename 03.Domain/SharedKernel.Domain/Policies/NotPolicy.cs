namespace SharedKernel.Domain.Policies;

/// <summary>
/// A composite policy that inverts the compliance result of its inner policy.
/// </summary>
/// <typeparam name="T">The type of domain object evaluated.</typeparam>
public sealed class NotPolicy<T> : IPolicy<T>
{
    private readonly IPolicy<T> _inner;

    /// <summary>
    /// Initialises a new <see cref="NotPolicy{T}"/> that inverts <paramref name="inner"/>.
    /// </summary>
    public NotPolicy(IPolicy<T> inner) => _inner = inner;

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="true"/> when the inner policy is <em>not</em> compliant.</remarks>
    public bool IsCompliant(T subject) => !_inner.IsCompliant(subject);

    /// <inheritdoc/>
    /// <remarks>
    /// WO-051/P-312 — when non-compliant (i.e., the inner policy unexpectedly <em>was</em>
    /// compliant), returns a fixed, generic explanation — there is no sub-policy failure to
    /// delegate to, since the inner policy itself did not fail.
    /// </remarks>
    public string Explain(T subject) =>
        IsCompliant(subject)
            ? string.Empty
            : $"Negated policy was compliant, which violates NotPolicy<{typeof(T).Name}>.";
}
