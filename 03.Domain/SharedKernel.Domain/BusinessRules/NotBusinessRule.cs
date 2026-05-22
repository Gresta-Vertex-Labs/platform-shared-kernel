namespace SharedKernel.Domain.BusinessRules;

/// <summary>
/// A composite business rule that inverts the broken state of its inner rule.
/// Broken when the inner rule is not broken, and not broken when the inner rule is broken.
/// </summary>
public sealed class NotBusinessRule : IBusinessRule
{
    private readonly IBusinessRule _inner;

    /// <summary>
    /// Initialises a new <see cref="NotBusinessRule"/> that inverts <paramref name="inner"/>.
    /// </summary>
    public NotBusinessRule(IBusinessRule inner) => _inner = inner;

    /// <inheritdoc/>
    public string Message => $"Not: {_inner.Message}";

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="true"/> when the inner rule is <em>not</em> broken.</remarks>
    public bool IsBroken() => !_inner.IsBroken();
}
