using SharedKernel.Primitives.Results;

namespace SharedKernel.Validation.Internal;

/// <summary>The shared <see cref="IParsable{TSelf}"/> implementation, built on <see cref="IValidatedValue{TSelf}.Create"/>.</summary>
internal static class ValueParsing
{
    public static T Parse<T>(string s)
        where T : struct, IValidatedValue<T>
    {
        ArgumentNullException.ThrowIfNull(s);

        Result<T> result = T.Create(s);
        return result.IsSuccess ? result.Value : throw new FormatException(result.Error.Message);
    }

    public static bool TryParse<T>(string? s, out T value)
        where T : struct, IValidatedValue<T>
    {
        Result<T> result = T.Create(s);
        value = result.IsSuccess ? result.Value : default;
        return result.IsSuccess;
    }
}
