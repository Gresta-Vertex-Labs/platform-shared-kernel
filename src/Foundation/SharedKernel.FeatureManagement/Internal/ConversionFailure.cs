namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// Why a variant's configuration could not be read into a flag's type. A closed vocabulary: the reason is a fixed
/// sentence, never exception text and never a configuration value.
/// </summary>
internal enum ConversionFailure
{
    /// <summary>An object was expected but the value is a scalar or a list.</summary>
    ExpectedObject,

    /// <summary>A list was expected but the value is not one.</summary>
    ExpectedList,

    /// <summary>A value is not <c>true</c> or <c>false</c>.</summary>
    NotBoolean,

    /// <summary>A value is not a number.</summary>
    NotNumber,

    /// <summary>The value is nested deeper than the converter allows.</summary>
    NestedTooDeeply,

    /// <summary>A single value was expected but the value is an object or a list.</summary>
    NotSingleValue,

    /// <summary>The value was well-formed but the target type rejected it.</summary>
    DoesNotFit,
}

/// <summary>The fixed sentence for each <see cref="ConversionFailure"/>.</summary>
internal static class ConversionFailures
{
    public static string Reason(this ConversionFailure failure) => failure switch
    {
        ConversionFailure.ExpectedObject => "an object was expected",
        ConversionFailure.ExpectedList => "a list was expected",
        ConversionFailure.NotBoolean => "a value is not true or false",
        ConversionFailure.NotNumber => "a value is not a number",
        ConversionFailure.NestedTooDeeply => "the value is nested too deeply",
        ConversionFailure.NotSingleValue => "a single value was expected, not an object or a list",
        ConversionFailure.DoesNotFit => "the value does not fit the flag's type",
        _ => "the value could not be read",
    };
}

/// <summary>Carries a <see cref="ConversionFailure"/> out of the converter's recursion. Never escapes the converter.</summary>
internal sealed class ConversionException : Exception
{
    public ConversionException(ConversionFailure failure)
        : base(failure.Reason())
    {
        Failure = failure;
    }

    public ConversionFailure Failure { get; }
}
