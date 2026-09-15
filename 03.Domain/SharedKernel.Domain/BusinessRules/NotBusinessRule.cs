namespace SharedKernel.Domain.BusinessRules;

/// <summary>A business rule that holds when its inner rule is broken, and is broken when that rule holds.</summary>
/// <remarks>
/// <b>Error code.</b> The inner rule's code and message describe the opposite condition, so the negated rule
/// takes its own <see cref="Code"/> and <see cref="Message"/>.
/// </remarks>
public sealed class NotBusinessRule : IBusinessRule
{
    private readonly IBusinessRule _inner;

    /// <summary>Initializes a new rule negating <paramref name="inner"/>, with its own code and message.</summary>
    /// <param name="inner">The rule to negate. Must not be null.</param>
    /// <param name="code">
    /// The error code reported when the negated rule is broken. Must not be null or whitespace.
    /// </param>
    /// <param name="message">
    /// The message reported when the negated rule is broken. Must not be null or whitespace.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="inner"/>, <paramref name="code"/> or <paramref name="message"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> or <paramref name="message"/> is empty or whitespace.
    /// </exception>
    public NotBusinessRule(IBusinessRule inner, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _inner = inner;
        Code = code;
        Message = message;
    }

    /// <summary>Gets the error code supplied at construction.</summary>
    public string Code { get; }

    /// <summary>Gets the message supplied at construction.</summary>
    public string Message { get; }

    /// <summary>Returns whether the inner rule holds.</summary>
    /// <returns><see langword="true"/> when the inner rule is not broken; otherwise <see langword="false"/>.</returns>
    public bool IsBroken() => !_inner.IsBroken();
}
