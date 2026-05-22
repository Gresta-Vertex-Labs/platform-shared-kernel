namespace SharedKernel.Domain.Policies;

/// <summary>
/// A composite policy that is compliant only when both sub-policies are compliant
/// (logical AND).
/// </summary>
/// <typeparam name="T">The type of domain object evaluated.</typeparam>
public sealed class AndPolicy<T> : IPolicy<T>
{
    private readonly IPolicy<T> _left;
    private readonly IPolicy<T> _right;

    /// <summary>
    /// Initialises a new <see cref="AndPolicy{T}"/> combining <paramref name="left"/>
    /// and <paramref name="right"/>.
    /// </summary>
    public AndPolicy(IPolicy<T> left, IPolicy<T> right)
    {
        _left = left;
        _right = right;
    }

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="true"/> only when both sub-policies are compliant.</remarks>
    public bool IsCompliant(T subject) => _left.IsCompliant(subject) && _right.IsCompliant(subject);
}
