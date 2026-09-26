using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Reporting.Spreadsheet;

/// <summary>How Excel exports are written. Bound from <c>SharedKernel:Reporting:Spreadsheet</c>; every setting is optional.</summary>
public sealed class SpreadsheetExportOptions : ISectionBoundOptions, IValidatableObject
{
    /// <summary>The most data rows an Excel worksheet holds (1,048,576 rows, one of them the header).</summary>
    public const int ExcelMaxDataRows = 1_048_575;

    /// <summary>Gets the configuration section: <c>SharedKernel:Reporting:Spreadsheet</c>.</summary>
    public static string SectionName => "SharedKernel:Reporting:Spreadsheet";

    /// <summary>
    /// Gets or sets the worksheet name used when the report has no title. Defaults to <c>"Report"</c>. At most 31
    /// characters, none of <c>\ / ? * [ ] :</c>.
    /// </summary>
    [Required]
    public string DefaultSheetName { get; set; } = "Report";

    /// <summary>Gets or sets whether the header row is bold. Defaults to <see langword="true"/>.</summary>
    public bool BoldHeaderRow { get; set; } = true;

    /// <summary>Gets or sets whether the header row stays visible while scrolling. Defaults to <see langword="true"/>.</summary>
    public bool FreezeHeaderRow { get; set; } = true;

    /// <summary>Gets or sets whether the header row gets filter buttons. Defaults to <see langword="true"/>.</summary>
    public bool AutoFilter { get; set; } = true;

    /// <summary>
    /// Gets or sets the most data rows an export may have; more fail with <c>reporting.row_limit_exceeded</c> and store
    /// nothing. Defaults to, and cannot exceed, <see cref="ExcelMaxDataRows"/>.
    /// </summary>
    [Range(1, ExcelMaxDataRows)]
    public int MaxRows { get; set; } = ExcelMaxDataRows;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DefaultSheetName is { } name && (name.Length > 31 || name.IndexOfAny(ExcelFormats.InvalidSheetNameChars) >= 0))
        {
            yield return new ValidationResult(
                $"DefaultSheetName '{name}' must be at most 31 characters, none of \\ / ? * [ ] :.",
                [nameof(DefaultSheetName)]);
        }
    }
}
