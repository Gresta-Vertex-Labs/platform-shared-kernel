using System.Text.RegularExpressions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Testing.Telemetry;

/// <summary>
/// The telemetry shapes the assertions pin, written out literally. They deliberately do not call the Primitives
/// recorder (<c>ActivityFailureExtensions</c>) to compute expectations: a regression there must fail the assertion,
/// not move with it.
/// </summary>
internal static partial class TelemetryShapes
{
    /// <summary>The <c>error.type</c> value for an <see cref="ErrorType"/> member the shape does not know.</summary>
    public const string OtherErrorType = "_OTHER";

    /// <summary>The name of the exception event added by <c>Activity.AddException</c>.</summary>
    public const string ExceptionEventName = "exception";

    public const string ExceptionTypeTag = "exception.type";
    public const string ExceptionMessageTag = "exception.message";
    public const string ExceptionStackTraceTag = "exception.stacktrace";

    /// <summary>The closed <c>outcome</c> vocabulary.</summary>
    public static readonly string[] Outcomes =
    [
        WellKnownOutcomes.Success,
        WellKnownOutcomes.Failure,
        WellKnownOutcomes.Exception,
        WellKnownOutcomes.Canceled,
    ];

    /// <summary>The expected <c>error.type</c> for a failed <c>Result</c>: the member name, <c>_OTHER</c> when undefined.</summary>
    public static string ErrorTypeName(ErrorType type) => type switch
    {
        ErrorType.None => "None",
        ErrorType.Unexpected => "Unexpected",
        ErrorType.Validation => "Validation",
        ErrorType.NotFound => "NotFound",
        ErrorType.Conflict => "Conflict",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.BusinessRule => "BusinessRule",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.Unavailable => "Unavailable",
        ErrorType.Timeout => "Timeout",
        _ => OtherErrorType,
    };

    public static bool IsOutcome(string? value) => value is not null && Array.IndexOf(Outcomes, value) >= 0;

    public static string OutcomeList => string.Join(", ", Outcomes.Select(o => $"'{o}'"));

    public static bool IsTagKey(string key) => TagKeyPattern().IsMatch(key);

    public static bool IsSpanSource(string name) => SourcePattern().IsMatch(name);

    public static bool IsSpanName(string name) => SpanNamePattern().IsMatch(name);

    public static bool IsMeterName(string name) => SourcePattern().IsMatch(name);

    public static bool IsInstrumentName(string name) => InstrumentNamePattern().IsMatch(name);

    public static void ThrowIfAnyEmpty(string[] forbiddenValues)
    {
        ArgumentNullException.ThrowIfNull(forbiddenValues);
        foreach (var value in forbiddenValues)
        {
            ArgumentException.ThrowIfNullOrEmpty(value, nameof(forbiddenValues));
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$")]
    private static partial Regex TagKeyPattern();

    [GeneratedRegex(@"^SharedKernel\.[A-Z]\w+$")]
    private static partial Regex SourcePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9]*( [a-z0-9][a-z0-9._\-]*)+$")]
    private static partial Regex SpanNamePattern();

    [GeneratedRegex(@"^sharedkernel\.[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9_]*)+$")]
    private static partial Regex InstrumentNamePattern();
}
