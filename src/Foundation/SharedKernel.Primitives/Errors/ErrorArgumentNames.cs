namespace SharedKernel.Primitives.Errors;

/// <summary>
/// Well-known keys in <see cref="Error.MessageArguments"/> that more than one package reads. Use
/// these constants rather than the literal strings.
/// </summary>
/// <remarks>
/// The names match FluentValidation's own message placeholders, so a translation written for a
/// FluentValidation message, such as <c>"{PropertyName} alanı zorunludur."</c>, fills in the same way
/// for an error from any other source.
/// </remarks>
public static class ErrorArgumentNames
{
    /// <summary>
    /// The path of the field a validation error refers to, such as <c>Accounts[0].Iban</c>. The
    /// HTTP boundary groups a validation response's <c>errors</c> and <c>errorCodes</c> by it; an
    /// error without it is grouped by its code.
    /// </summary>
    public const string PropertyPath = "PropertyPath";

    /// <summary>
    /// The display name of that field, such as <c>IBAN</c>, for use inside a message:
    /// <c>"{PropertyName} is required."</c>.
    /// </summary>
    public const string PropertyName = "PropertyName";
}
