using System.Collections.Frozen;
using System.Text.RegularExpressions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Validation.FluentValidation.Internal;

/// <summary>
/// Maps the error code of a FluentValidation failure to a kernel error code. FluentValidation names a
/// failure after the built-in validator that raised it (<c>NotEmptyValidator</c>); that PascalCase
/// name is not a kernel code, so it is translated and the original name is not kept.
/// </summary>
internal static partial class FluentValidationErrorCodeMap
{
    // The built-in validator names of the pinned FluentValidation version. Anything not listed (a
    // custom validator, Must/PredicateValidator, Equal, Enum, Null, ...) is a plain validation failure.
    private static readonly FrozenDictionary<string, string> BuiltInValidators =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NotEmptyValidator"] = ErrorCodes.Validation.Required,
            ["NotNullValidator"] = ErrorCodes.Validation.Required,
            ["MaximumLengthValidator"] = ErrorCodes.Validation.MaxLength,
            ["MinimumLengthValidator"] = ErrorCodes.Validation.MinLength,
            ["LengthValidator"] = ErrorCodes.Validation.OutOfRange,
            ["ExactLengthValidator"] = ErrorCodes.Validation.OutOfRange,
            ["InclusiveBetweenValidator"] = ErrorCodes.Validation.OutOfRange,
            ["ExclusiveBetweenValidator"] = ErrorCodes.Validation.OutOfRange,
            ["GreaterThanValidator"] = ErrorCodes.Validation.OutOfRange,
            ["GreaterThanOrEqualValidator"] = ErrorCodes.Validation.OutOfRange,
            ["LessThanValidator"] = ErrorCodes.Validation.OutOfRange,
            ["LessThanOrEqualValidator"] = ErrorCodes.Validation.OutOfRange,
            ["EmailValidator"] = ErrorCodes.Validation.InvalidFormat,
            ["RegularExpressionValidator"] = ErrorCodes.Validation.InvalidFormat,
            ["CreditCardValidator"] = ErrorCodes.Validation.InvalidFormat,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Returns the kernel code for a failure's <paramref name="errorCode"/>.</summary>
    internal static string Map(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            return ErrorCodes.Validation.Failed;

        // A code that already follows the kernel format (the identifier rules, WithErrorCode("order.x")) passes through.
        if (CodeFormat().IsMatch(errorCode))
            return errorCode;

        return BuiltInValidators.TryGetValue(errorCode, out var mapped) ? mapped : ErrorCodes.Validation.Failed;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CodeFormat();
}
