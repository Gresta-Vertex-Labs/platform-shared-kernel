using SharedKernel.Primitives.Errors;

namespace SharedKernel.Reporting.Abstractions.Errors;

/// <summary>
/// Canonical <see cref="Error"/> factory for this domain's own failure cases — an empty
/// <c>ReportDefinition&lt;TRow&gt;.Columns</c> or an invalid <c>ReportDestination</c>. Distinct from
/// and never duplicating <c>SharedKernel.Storage.StorageErrors</c>, which
/// <see cref="Delivery.StorageStreamingWriter"/> surfaces unchanged for genuine storage-layer
/// failures (upload rejected, presign expiry too long, etc.).
/// </summary>
public static class ReportingErrors
{
    private const string EmptyColumnsCode = "reporting.empty_columns";
    private const string InvalidDestinationCode = "reporting.invalid_destination";

    /// <summary>The report definition declares no columns.</summary>
    public static Error EmptyColumns() =>
        Error.Validation(EmptyColumnsCode, "ReportDefinition<TRow>.Columns must contain at least one column.");

    /// <summary>The report destination is missing a required field or is otherwise malformed.</summary>
    /// <param name="reason">A human-readable description of what is wrong with the destination.</param>
    public static Error InvalidDestination(string reason) =>
        Error.Validation(InvalidDestinationCode, $"Report destination is invalid: {reason}");
}
