namespace SharedKernel.Domain.Policies;

/// <summary>
/// A composite policy that is compliant when at least one sub-policy is compliant
/// (logical OR).
/// </summary>
/// <typeparam name="T">The type of domain object evaluated.</typeparam>
public sealed class OrPolicy<T> : IPolicy<T>
{
    private readonly IPolicy<T> _left;
    private readonly IPolicy<T> _right;

    /// <summary>
    /// Initialises a new <see cref="OrPolicy{T}"/> combining <paramref name="left"/>
    /// and <paramref name="right"/>.
    /// </summary>
    public OrPolicy(IPolicy<T> left, IPolicy<T> right)
    {
        _left = left;
        _right = right;
    }

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="true"/> when at least one sub-policy is compliant.</remarks>
    public bool IsCompliant(T subject) => _left.IsCompliant(subject) || _right.IsCompliant(subject);

    /// <inheritdoc/>
    /// <remarks>
    /// WO-051/P-312 — non-empty only when compliance fails, which for a logical OR means
    /// <em>both</em> sub-policies failed; in that case both explanations are aggregated with
    /// <c>"; "</c>.
    /// </remarks>
    public string Explain(T subject) =>
        IsCompliant(subject)
            ? string.Empty
            : string.Join("; ", _left.Explain(subject), _right.Explain(subject));
}
