using SharedKernel.Primitives.Errors;

namespace SharedKernel.Reporting;

/// <summary>The <see cref="Error.Code"/> values of every reporting failure, for matching in callers and tests.</summary>
/// <remarks>
/// Storage failures (an unknown bucket, a failed write condition, an outage) keep their own <c>storage.*</c> codes.
/// </remarks>
public static class ReportingErrorCodes
{
    /// <summary><c>reporting.invalid_definition</c> (<c>Validation</c>): the report definition has no columns or an invalid column.</summary>
    public const string InvalidDefinition = "reporting.invalid_definition";

    /// <summary><c>reporting.invalid_destination</c> (<c>Validation</c>): the destination is missing a store or key, or is malformed.</summary>
    public const string InvalidDestination = "reporting.invalid_destination";

    /// <summary><c>reporting.unsupported_format</c> (<c>Validation</c>): no exporter is registered for the requested format.</summary>
    public const string UnsupportedFormat = "reporting.unsupported_format";

    /// <summary>
    /// <c>reporting.row_limit_exceeded</c> (<c>Validation</c>): the rows exceed what the format or its configured limit
    /// allows. Nothing is stored.
    /// </summary>
    public const string RowLimitExceeded = "reporting.row_limit_exceeded";

    /// <summary><c>reporting.invalid_request</c> (<c>Validation</c>): the HTML or the conversion options are invalid.</summary>
    public const string InvalidRequest = "reporting.invalid_request";

    /// <summary><c>reporting.conversion_failed</c> (<c>Validation</c>): the converter refused the document.</summary>
    public const string ConversionFailed = "reporting.conversion_failed";

    /// <summary><c>reporting.converter_unavailable</c> (<c>Unavailable</c>): the converter could not be reached or failed.</summary>
    public const string ConverterUnavailable = "reporting.converter_unavailable";

    /// <summary><c>reporting.conversion_timeout</c> (<c>Timeout</c>): the conversion did not finish in time.</summary>
    public const string ConversionTimeout = "reporting.conversion_timeout";
}

/// <summary>Creates the reporting <see cref="Error"/>s; the codes are in <see cref="ReportingErrorCodes"/>.</summary>
public static class ReportingErrors
{
    /// <summary>The report definition is invalid.</summary>
    /// <param name="reason">What is wrong, e.g. <c>"Columns must contain at least one column."</c>.</param>
    /// <returns>A validation error.</returns>
    public static Error InvalidDefinition(string reason) =>
        Error.Validation(ReportingErrorCodes.InvalidDefinition, $"The report definition is invalid: {reason}");

    /// <summary>The report destination is invalid.</summary>
    /// <param name="reason">What is wrong, e.g. <c>"Key is required."</c>.</param>
    /// <returns>A validation error.</returns>
    public static Error InvalidDestination(string reason) =>
        Error.Validation(ReportingErrorCodes.InvalidDestination, $"The report destination is invalid: {reason}");

    /// <summary>No exporter is registered for a format.</summary>
    /// <param name="format">The requested format, as the caller sent it.</param>
    /// <param name="supported">The formats that are registered.</param>
    /// <returns>A validation error.</returns>
    public static Error UnsupportedFormat(string? format, IEnumerable<ReportFormat> supported)
    {
        ArgumentNullException.ThrowIfNull(supported);
        return Error.Validation(
            ReportingErrorCodes.UnsupportedFormat,
            $"The report format '{format}' is not supported. Supported formats: {string.Join(", ", supported.Select(f => f.Name))}.");
    }

    /// <summary>The rows exceed the limit of a format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="maxRows">The limit.</param>
    /// <returns>A validation error.</returns>
    public static Error RowLimitExceeded(ReportFormat format, long maxRows)
    {
        ArgumentNullException.ThrowIfNull(format);
        return Error.Validation(
            ReportingErrorCodes.RowLimitExceeded,
            $"The report has more than {maxRows} rows, the most the '{format.Name}' format allows here. Export it as CSV, or narrow the query.");
    }

    /// <summary>The HTML or the conversion options are invalid.</summary>
    /// <param name="reason">What is wrong.</param>
    /// <returns>A validation error.</returns>
    public static Error InvalidRequest(string reason) =>
        Error.Validation(ReportingErrorCodes.InvalidRequest, $"The conversion request is invalid: {reason}");

    /// <summary>The converter refused the document.</summary>
    /// <param name="reason">The converter's explanation.</param>
    /// <returns>A validation error.</returns>
    public static Error ConversionFailed(string reason) =>
        Error.Validation(ReportingErrorCodes.ConversionFailed, $"The document could not be converted: {reason}");

    /// <summary>The converter could not be reached, or failed.</summary>
    /// <param name="reason">What happened.</param>
    /// <returns>An unavailable error.</returns>
    public static Error ConverterUnavailable(string reason) =>
        Error.Unavailable(ReportingErrorCodes.ConverterUnavailable, $"The document converter is unavailable: {reason}");

    /// <summary>The conversion did not finish in time.</summary>
    /// <param name="timeout">The time allowed.</param>
    /// <returns>A timeout error.</returns>
    public static Error ConversionTimeout(TimeSpan timeout) =>
        Error.Timeout(ReportingErrorCodes.ConversionTimeout, $"The document conversion did not finish within {timeout}.");
}
