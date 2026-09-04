using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Abstractions.Errors;
using SharedKernel.Reporting.Abstractions.Models;

namespace SharedKernel.Reporting.Abstractions.Validation;

/// <summary>
/// Shared precondition checks every provider applies before encoding begins — validating the
/// <see cref="ReportDefinition{TRow}"/> and, when exporting to storage, the
/// <see cref="ReportDestination"/>. Lives here so all three providers apply the identical checks
/// rather than three subtly different ones — never re-implemented per provider.
/// </summary>
public static class ReportExportPreconditions
{
    /// <summary>Validates a <see cref="ReportDefinition{TRow}"/> alone — the <c>ExportToStreamAsync</c> path.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="definition">The definition to validate.</param>
    /// <returns>A successful <see cref="Result"/>, or a failure from <see cref="ReportingErrors"/>.</returns>
    public static Result ValidateDefinition<TRow>(ReportDefinition<TRow> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.Columns.Count == 0
            ? Result.Failure(ReportingErrors.EmptyColumns())
            : Result.Success();
    }

    /// <summary>Validates a <see cref="ReportDefinition{TRow}"/> and <see cref="ReportDestination"/> together — the <c>ExportAsync</c> path.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="definition">The definition to validate.</param>
    /// <param name="destination">The destination to validate.</param>
    /// <returns>A successful <see cref="Result"/>, or a failure from <see cref="ReportingErrors"/>.</returns>
    public static Result ValidateDefinitionAndDestination<TRow>(ReportDefinition<TRow> definition, ReportDestination destination)
    {
        var definitionResult = ValidateDefinition(definition);
        if (definitionResult.IsFailure)
        {
            return definitionResult;
        }

        ArgumentNullException.ThrowIfNull(destination);

        if (string.IsNullOrWhiteSpace(destination.Bucket))
        {
            return Result.Failure(ReportingErrors.InvalidDestination("Bucket is required."));
        }

        if (string.IsNullOrWhiteSpace(destination.Key))
        {
            return Result.Failure(ReportingErrors.InvalidDestination("Key is required."));
        }

        return Result.Success();
    }
}
