namespace SharedKernel.Domain.BusinessRules;

/// <summary>A rule that holds when its inner rule is broken, and is broken when the inner rule holds.</summary>
/// <remarks>
/// A negated rule cannot reuse its inner rule's code or message, which describe the opposite condition, so
/// both are supplied explicitly.
/// </remarks>
public sealed class NotBusinessRule : IBusinessRule
{
    private readonly IBusinessRule _inner;

    /// <summary>Negates <paramref name="inner"/>, describing the negated condition with its own code and message.</summary>
    /// <param name="inner">The rule to negate.</param>
    /// <param name="code">The code reported when the negated rule is broken.</param>
    /// <param name="message">The message reported when the negated rule is broken.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> or <paramref name="message"/> is null, empty or whitespace.</exception>
    public NotBusinessRule(IBusinessRule inner, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _inner = inner;
        Code = code;
        Message = message;
    }

    /// <inheritdoc/>
    public string Code { get; }

    /// <inheritdoc/>
    public string Message { get; }

    /// <inheritdoc/>
    public bool IsBroken() => !_inner.IsBroken();
}
